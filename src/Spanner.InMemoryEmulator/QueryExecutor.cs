using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Parsing;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Executes parsed SELECT statements against the in-memory database.
/// Pipeline: FROM → JOIN → WHERE → GROUP BY → HAVING → SELECT → DISTINCT → ORDER BY → LIMIT/OFFSET
/// </summary>
internal class QueryExecutor
{
	private readonly InMemorySpannerDatabase _database;

	public QueryExecutor(InMemorySpannerDatabase database)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
	}

	internal SchemaRegistry Schema => _database.Schema;

	/// <summary>
	/// Executes a FullQuery (with optional CTEs and set operations).
	/// </summary>
	public ResultSet Execute(FullQuery fullQuery, IDictionary<string, object?>? parameters)
	{
		// Register CTEs as virtual tables for subquery resolution
		Dictionary<string, SelectStatement>? cteMap = null;
		if (fullQuery.Ctes is { Count: > 0 })
		{
			cteMap = new Dictionary<string, SelectStatement>(StringComparer.OrdinalIgnoreCase);
			foreach (var cte in fullQuery.Ctes)
			{
				cteMap[cte.Name] = cte.Query;
			}
		}

		var result = Execute(fullQuery.Select, parameters, cteMap);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
		// Apply set operations. A trailing ORDER BY applies to the final combined result,
		// not to the last SELECT in the chain.
		if (fullQuery.SetOps is { Count: > 0 })
		{
			// Extract the trailing ORDER BY from the last right SELECT (it belongs to the full query)
			var lastRight = fullQuery.SetOps[^1].Right;
			var finalOrderBy = lastRight.OrderBy;
			var finalLimit = lastRight.Limit;
			var finalOffset = lastRight.Offset;

			foreach (var setOp in fullQuery.SetOps)
			{
				// For the last set op, strip ORDER BY/LIMIT from the right SELECT
				var rightSelect = setOp == fullQuery.SetOps[^1] && finalOrderBy != null
					? setOp.Right with { OrderBy = null, Limit = null, Offset = null }
					: setOp.Right;
				var rightResult = Execute(rightSelect, parameters, cteMap);
				result = ApplySetOperation(result, rightResult, setOp.Type);
			}

			// Apply ORDER BY to the combined result
			if (finalOrderBy is { Count: > 0 })
			{
				SortResultSetRows(result, finalOrderBy);
			}

			// Apply LIMIT/OFFSET
			if (finalLimit != null || finalOffset != null)
			{
				var offset = (int)(finalOffset ?? 0);
				var rows = result.Rows.ToList();
				rows = rows.Skip(offset).ToList();
				if (finalLimit != null)
					rows = rows.Take((int)finalLimit.Value).ToList();
				result.Rows.Clear();
				result.Rows.AddRange(rows);
			}
		}

		return result;
	}

	/// <summary>
	/// Sorts a ResultSet's rows based on ORDER BY columns (used for set operation results).
	/// </summary>
	private static void SortResultSetRows(ResultSet result, List<OrderByColumn> orderBy)
	{
		var fields = result.Metadata.RowType.Fields;
		var fieldMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < fields.Count; i++)
			fieldMap[fields[i].Name] = i;

		var rows = result.Rows.ToList();
		rows.Sort((a, b) =>
		{
			foreach (var col in orderBy)
			{
				var colName = col.Expr is ColumnRefExpr cref ? cref.Column : InferColumnNameStatic(col.Expr);
				if (!fieldMap.TryGetValue(colName, out var idx)) continue;

				int cmp = CompareProtoValues(a.Values[idx], b.Values[idx]);
				if (cmp != 0) return col.Order == SortOrder.Desc ? -cmp : cmp;
			}
			return 0;
		});
		result.Rows.Clear();
		result.Rows.AddRange(rows);
	}

	/// <summary>
	/// Compares two protobuf Values for ordering.
	/// Spanner INT64 values are represented as strings in protobuf.
	/// </summary>
	private static int CompareProtoValues(Google.Protobuf.WellKnownTypes.Value a, Google.Protobuf.WellKnownTypes.Value b)
	{
		if (a.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue &&
			b.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue) return 0;
		if (a.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue) return -1;
		if (b.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue) return 1;

		if (a.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue)
		{
			// Spanner encodes INT64/NUMERIC as strings — try numeric comparison first
			if (long.TryParse(a.StringValue, out var la) && long.TryParse(b.StringValue, out var lb))
				return la.CompareTo(lb);
			if (double.TryParse(a.StringValue, out var da) && double.TryParse(b.StringValue, out var db))
				return da.CompareTo(db);
			return string.Compare(a.StringValue, b.StringValue, StringComparison.Ordinal);
		}
		if (a.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue)
			return a.NumberValue.CompareTo(b.NumberValue);
		if (a.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.BoolValue)
			return a.BoolValue.CompareTo(b.BoolValue);
		return 0;
	}

	/// <summary>
	/// Executes a parsed SELECT statement and returns the result as a ResultSet protobuf.
	/// </summary>
	public ResultSet Execute(SelectStatement select, IDictionary<string, object?>? parameters,
		Dictionary<string, SelectStatement>? cteMap = null, Dictionary<string, object?>? outerRow = null)
	{
		var evaluator = new ExpressionEvaluator(parameters, this, cteMap, outerRow);

		// 1. FROM — get all rows from the source table
		List<Dictionary<string, object?>> rows;
		TableDefinition? sourceTable = null;

		if (select.From != null)
		{
			if (select.From is SubqueryFromClause subFrom)
			{
				// FROM (SELECT ...) AS alias
				var subResult = Execute(subFrom.Subquery, parameters, cteMap);
				rows = ResultSetToRows(subResult);
				var leftAlias = subFrom.Alias!;
				rows = rows.Select(r => PrefixRow(r, leftAlias)).ToList();
			}
			else if (cteMap != null && cteMap.TryGetValue(select.From.Table, out var cteQuery))
			{
				// FROM references a CTE
				var cteResult = Execute(cteQuery, parameters, cteMap);
				rows = ResultSetToRows(cteResult);
				var leftAlias = select.From.Alias ?? select.From.Table;
				rows = rows.Select(r => PrefixRow(r, leftAlias)).ToList();
			}
			else if (select.From.Table.StartsWith("INFORMATION_SCHEMA.", StringComparison.OrdinalIgnoreCase))
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema
				var infoSchemaTable = select.From.Table["INFORMATION_SCHEMA.".Length..];
				var provider = new InformationSchemaProvider(_database.Schema);
				var (virtualTable, virtualRows) = provider.GetVirtualTable(infoSchemaTable);
				sourceTable = virtualTable;
				var leftAlias = select.From.Alias ?? select.From.Table;
				rows = virtualRows.Select(r => PrefixRow(r, leftAlias)).ToList();
			}
			else if (_database.Schema.TryGetView(select.From.Table, out var viewDef) && viewDef != null)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_view
				// Expand view by parsing and executing its SQL body.
				var viewEngine = new SqlEngine(_database);
				var viewResult = viewEngine.ExecuteSql(viewDef.SqlBody, parameters);
				rows = ResultSetToRows(viewResult);
				var leftAlias = select.From.Alias ?? select.From.Table;
				rows = rows.Select(r => PrefixRow(r, leftAlias)).ToList();
			}
			else if (select.From is UnnestFromClause unnestFrom)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#unnest_operator
				var arrayValue = evaluator.Evaluate(unnestFrom.ArrayExpr, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
				if (arrayValue is IList<object?> list)
				{
					var alias = unnestFrom.Alias ?? "unnest";
					rows = new List<Dictionary<string, object?>>();
					for (var i = 0; i < list.Count; i++)
					{
						var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
						{
							[alias] = list[i]
						};
						if (unnestFrom.WithOffset)
						{
							row[unnestFrom.OffsetAlias ?? "offset"] = (long)i;
						}
						rows.Add(row);
					}
				}
				else
				{
					rows = new List<Dictionary<string, object?>>();
				}
			}
			else
			{
				if (!_database.Schema.TryGetTable(select.From.Table, out sourceTable) || sourceTable == null)
				{
					throw new InvalidOperationException($"Table '{select.From.Table}' not found.");
				}

				var leftAlias = select.From.Alias ?? select.From.Table;
				rows = sourceTable.Rows.Values
					.Select(r => PrefixRow(r.Columns, leftAlias))
					.ToList();
			}

			// 1a½. TABLESAMPLE — random sampling
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
			if (select.From.TableSample is { } sample)
			{
				rows = ApplyTableSample(rows, sample);
			}

			// 1b. JOINs — combine rows from multiple tables
			if (select.From.Joins != null)
			{
				foreach (var join in select.From.Joins)
				{
					rows = ExecuteJoin(rows, join, evaluator, cteMap);
				}
			}
		}
		else
		{
			// SELECT without FROM (e.g., SELECT 1, SELECT CURRENT_TIMESTAMP())
			rows = new List<Dictionary<string, object?>> { new(StringComparer.OrdinalIgnoreCase) };
		}

		// 2. WHERE — filter rows
		if (select.Where != null)
		{
			rows = rows.Where(r => evaluator.EvaluateAsBool(select.Where, r)).ToList();
		}

		// 3. GROUP BY + aggregation
		if (select.GroupBy != null && select.GroupBy.Count > 0)
		{
			rows = ExecuteGroupBy(select, rows, evaluator);
		}
		else if (HasAggregates(select.Columns))
		{
			// Whole-table aggregation (no GROUP BY but SELECT has aggregates)
			rows = ExecuteWholeTableAggregation(select, rows, evaluator);
		}

		// 4. HAVING — filter after aggregation
		if (select.Having != null)
		{
			rows = rows.Where(r => evaluator.EvaluateAsBool(select.Having, r)).ToList();
		}

		// 5. Window functions — pre-compute window function results into rows
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
		//   Window functions are computed after WHERE/GROUP BY/HAVING but before ORDER BY.
		//   Also include window functions referenced in QUALIFY clause.
		var windowColumns = select.Columns.ToList();
		if (select.Qualify != null)
		{
			foreach (var winExpr in ExtractWindowExprs(select.Qualify))
			{
				var synthName = InferColumnName(winExpr);
				windowColumns.Add(new SelectColumn(winExpr, synthName));
			}
		}
		rows = EvaluateWindowFunctions(windowColumns, rows, evaluator);

		// 5b. QUALIFY — filter after window function evaluation
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#qualify_clause
		//   "QUALIFY filters the results of window functions."
		if (select.Qualify != null)
		{
			rows = rows.Where(r => evaluator.EvaluateAsBool(select.Qualify, r)).ToList();
		}

		// 6. ORDER BY — sort before projection so all source columns are available
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
		//   ORDER BY can reference columns not in the SELECT list, and can also use SELECT aliases.
		if (select.OrderBy != null && select.OrderBy.Count > 0)
		{
			// Resolve SELECT aliases in ORDER BY expressions.
			// After GROUP BY, alias values are already computed in output rows — resolving aliases
			// back to source expressions would fail because source columns are no longer available.
			// Also skip resolution for window/aggregate expressions (pre-computed under alias key).
			var hasGroupBy = select.GroupBy is { Count: > 0 } || HasAggregates(select.Columns);
			var resolvedOrderBy = select.OrderBy.Select(ob =>
			{
				if (hasGroupBy) return ob;
				if (ob.Expr is ColumnRefExpr colRef && colRef.TableAlias == null)
				{
					var alias = select.Columns.FirstOrDefault(c =>
						string.Equals(c.Alias, colRef.Column, StringComparison.OrdinalIgnoreCase));
					if (alias != null && alias.Expr is not WindowExpr && !IsAggregate(alias.Expr))
						return new OrderByColumn(alias.Expr, ob.Order);
				}
				return ob;
			}).ToList();
			rows = OrderRows(resolvedOrderBy, rows, evaluator);
		}

		// 7. SELECT — project columns and evaluate expressions
		var (outputColumns, projectedRows) = ProjectColumns(select, rows, evaluator, sourceTable);

		// 7. DISTINCT
		if (select.IsDistinct)
		{
			projectedRows = DistinctRows(projectedRows, outputColumns);
		}

		// 8. OFFSET
		if (select.Offset.HasValue)
		{
			projectedRows = projectedRows.Skip((int)select.Offset.Value).ToList();
		}

		// 9. LIMIT
		if (select.Limit.HasValue)
		{
			projectedRows = projectedRows.Take((int)select.Limit.Value).ToList();
		}

		return ResultSetBuilder.Build(outputColumns, projectedRows);
	}

	private (IReadOnlyList<ColumnDef> columns, List<Dictionary<string, object?>> rows) ProjectColumns(
		SelectStatement select,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator,
		TableDefinition? sourceTable)
	{
		var outputColumns = new List<ColumnDef>();
		var projectedRows = new List<Dictionary<string, object?>>();

		// Determine output columns from SELECT list
		var expandedColumns = new List<(SqlExpression Expr, string Name)>();

		foreach (var col in select.Columns)
		{
			if (col.Expr is StarExpr)
			{
				// SELECT * — expand to all columns from source table
				// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
				//   HIDDEN columns are excluded from SELECT * expansion.
				if (sourceTable != null)
				{
					foreach (var tablCol in sourceTable.Columns)
					{
						if (tablCol.IsHidden) continue;
						expandedColumns.Add((new ColumnRefExpr(null, tablCol.Name), tablCol.Name));
					}
				}
				else if (rows.Count > 0)
				{
					// No sourceTable (subquery/CTE) — use row keys
					foreach (var key in rows[0].Keys)
					{
						expandedColumns.Add((new ColumnRefExpr(null, key), key));
					}
				}
			}
			else if (col.Expr is ColumnRefExpr starRef && starRef.Column == "*" && starRef.TableAlias != null)
			{
				// SELECT t.* — expand to all columns with the given alias prefix
				var prefix = starRef.TableAlias + ".";
				if (rows.Count > 0)
				{
					foreach (var key in rows[0].Keys)
					{
						if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						{
							var colName = key[prefix.Length..];
							expandedColumns.Add((new ColumnRefExpr(starRef.TableAlias, colName), colName));
						}
					}
				}
				else if (sourceTable != null)
				{
					foreach (var tablCol in sourceTable.Columns)
					{
						if (tablCol.IsHidden) continue;
						expandedColumns.Add((new ColumnRefExpr(starRef.TableAlias, tablCol.Name), tablCol.Name));
					}
				}
			}
			else
			{
				var name = col.Alias ?? InferColumnName(col.Expr);
				expandedColumns.Add((col.Expr, name));
			}
		}

		// Build output column definitions
		foreach (var (expr, name) in expandedColumns)
		{
			var typeCode = InferType(expr, sourceTable);
			TypeCode? arrayElementType = null;
			if (typeCode == TypeCode.Array)
				arrayElementType = InferArrayElementType(expr, sourceTable);
			outputColumns.Add(new ColumnDef(name, typeCode, arrayElementType: arrayElementType));
		}

		// Determine if we're in a GROUP BY / aggregation context where rows have
		// precomputed values but no original source columns.
		var isAggregationResult = select.GroupBy is { Count: > 0 } || HasAggregates(select.Columns);

		// Project each row
		foreach (var row in rows)
		{
			var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			foreach (var (expr, name) in expandedColumns)
			{
				// If the value is already in the row, use it directly for:
				// - Aggregate/window expressions (always precomputed)
				// - Any expression in GROUP BY/aggregation context (source columns unavailable)
				if (row.ContainsKey(name) && (isAggregationResult || IsAggregate(expr) || expr is WindowExpr))
				{
					projected[name] = row[name];
				}
				else
				{
					projected[name] = evaluator.Evaluate(expr, row);
				}
			}
			projectedRows.Add(projected);
		}

		// When sourceTable is null (CTE, subquery, UNNEST, etc.), InferType may default to String.
		// Fix column types by inspecting actual row values.
		if (projectedRows.Count > 0)
		{
			for (int i = 0; i < outputColumns.Count; i++)
			{
				var name = outputColumns[i].Name;
				if (outputColumns[i].SpannerType == TypeCode.String)
				{
					var sample = projectedRows.FirstOrDefault(r => r.TryGetValue(name, out var v) && v != null)?[name];
					if (sample != null)
					{
						var inferred = InferTypeFromValue(sample);
						if (inferred == TypeCode.Array && sample is IList<object?> list)
						{
							var elemType = list.OfType<object>().Select(InferTypeFromValue).FirstOrDefault(TypeCode.String);
							outputColumns[i] = new ColumnDef(name, TypeCode.Array, arrayElementType: elemType);
						}
						else if (inferred != TypeCode.String)
							outputColumns[i] = new ColumnDef(name, inferred);
					}
				}
				// Also fix Array columns that have no element type
				else if (outputColumns[i].SpannerType == TypeCode.Array && outputColumns[i].ArrayElementType == null)
				{
					var sample = projectedRows.FirstOrDefault(r => r.TryGetValue(name, out var v) && v is IList<object?>)?[name];
					if (sample is IList<object?> list2)
					{
						var elemType = list2.OfType<object>().Select(InferTypeFromValue).FirstOrDefault(TypeCode.String);
						outputColumns[i] = new ColumnDef(name, TypeCode.Array, arrayElementType: elemType);
					}
					else
					{
						// Default to STRING element type to avoid null ArrayElementType
						outputColumns[i] = new ColumnDef(name, TypeCode.Array, arrayElementType: TypeCode.String);
					}
				}
			}
		}

		return (outputColumns.AsReadOnly(), projectedRows);
	}

	private static TypeCode InferTypeFromValue(object value) => value switch
	{
		long => TypeCode.Int64,
		double => TypeCode.Float64,
		float => TypeCode.Float32,
		bool => TypeCode.Bool,
		DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? TypeCode.Date : TypeCode.Timestamp,
		byte[] => TypeCode.Bytes,
		IList<object?> => TypeCode.Array,
		_ => TypeCode.String
	};

	private static TypeCode InferArrayElementType(SqlExpression expr, TableDefinition? table)
	{
		if (expr is FunctionCallExpr func)
		{
			var name = func.Name.ToUpperInvariant();
			// ARRAY_AGG returns array of argument type
			if (name == "ARRAY_AGG" && func.Arguments.Count > 0)
				return InferType(func.Arguments[0], table);
			// GENERATE_ARRAY typically returns INT64 or FLOAT64 array
			if (name == "GENERATE_ARRAY" && func.Arguments.Count > 0)
				return InferType(func.Arguments[0], table);
			// SPLIT returns ARRAY<STRING>
			if (name == "SPLIT") return TypeCode.String;
			// ARRAY_CONCAT — infer from first argument's element type
			if (name == "ARRAY_CONCAT" && func.Arguments.Count > 0)
				return InferArrayElementType(func.Arguments[0], table);
		}
		if (expr is ArrayLiteralExpr arr && arr.Elements.Count > 0)
			return InferType(arr.Elements[0], table);
		if (expr is ColumnRefExpr col && table != null)
		{
			var colDef = table.Columns.FirstOrDefault(c => string.Equals(c.Name, col.Column, StringComparison.OrdinalIgnoreCase));
			return colDef?.ArrayElementType ?? TypeCode.String;
		}
		// ArraySubqueryExpr — infer from subquery result
		return TypeCode.String; // Default element type
	}

	private static string InferColumnName(SqlExpression expr) => InferColumnNameStatic(expr);

	/// <summary>
	/// Recursively extracts all WindowExpr nodes from an expression tree.
	/// Used to find window functions in QUALIFY clauses that need pre-computation.
	/// </summary>
	private static IEnumerable<WindowExpr> ExtractWindowExprs(SqlExpression expr)
	{
		switch (expr)
		{
			case WindowExpr win:
				yield return win;
				break;
			case BinaryExpr bin:
				foreach (var w in ExtractWindowExprs(bin.Left)) yield return w;
				foreach (var w in ExtractWindowExprs(bin.Right)) yield return w;
				break;
			case UnaryExpr un:
				foreach (var w in ExtractWindowExprs(un.Operand)) yield return w;
				break;
			case FunctionCallExpr func:
				foreach (var arg in func.Arguments)
					foreach (var w in ExtractWindowExprs(arg)) yield return w;
				break;
			case CaseExpr caseExpr:
				if (caseExpr.Operand != null)
					foreach (var w in ExtractWindowExprs(caseExpr.Operand)) yield return w;
				foreach (var when in caseExpr.Whens)
				{
					foreach (var w in ExtractWindowExprs(when.Condition)) yield return w;
					foreach (var w in ExtractWindowExprs(when.Result)) yield return w;
				}
				if (caseExpr.Else != null)
					foreach (var w in ExtractWindowExprs(caseExpr.Else)) yield return w;
				break;
			case IsNullExpr isNull:
				foreach (var w in ExtractWindowExprs(isNull.Value)) yield return w;
				break;
			case BetweenExpr bet:
				foreach (var w in ExtractWindowExprs(bet.Value)) yield return w;
				foreach (var w in ExtractWindowExprs(bet.Low)) yield return w;
				foreach (var w in ExtractWindowExprs(bet.High)) yield return w;
				break;
			case InExpr inExpr:
				foreach (var w in ExtractWindowExprs(inExpr.Value)) yield return w;
				foreach (var item in inExpr.List)
					foreach (var w in ExtractWindowExprs(item)) yield return w;
				break;
		}
	}

	internal static string InferColumnNameStatic(SqlExpression expr) => expr switch
	{
		ColumnRefExpr col => col.Column,
		FunctionCallExpr func => func.Name,
		CountStarExpr => "COUNT(*)",
		CastExpr cast => InferColumnNameStatic(cast.Value),
		WindowExpr win => InferColumnNameStatic(win.Function),
		_ => ""
	};

	private static TypeCode InferType(SqlExpression expr, TableDefinition? table) => expr switch
	{
		ColumnRefExpr col when table != null =>
			table.Columns.FirstOrDefault(c => string.Equals(c.Name, col.Column, StringComparison.OrdinalIgnoreCase))?.SpannerType ?? TypeCode.String,
		LiteralExpr lit => lit.Value switch
		{
			null => TypeCode.String,
			long => TypeCode.Int64,
			double => TypeCode.Float64,
			bool => TypeCode.Bool,
			string => TypeCode.String,
			DateTime => TypeCode.Timestamp,
			_ => TypeCode.String
		},
		CountStarExpr => TypeCode.Int64,
		FunctionCallExpr func => InferFunctionReturnType(func, table),
		CastExpr cast => cast.TargetType,
		BinaryExpr bin => InferBinaryType(bin, table),
		UnaryExpr un => un.Op == UnaryOp.Not ? TypeCode.Bool : InferType(un.Operand, table),
		WindowExpr win => InferType(win.Function, table),
		ArrayAccessExpr acc => acc.Array is ArrayLiteralExpr arr && arr.Elements.Count > 0
			? InferType(arr.Elements[0], table)
			: TypeCode.String, // Element type unknown at inference time
		// Boolean-returning expressions
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
		IsNullExpr => TypeCode.Bool,
		BetweenExpr => TypeCode.Bool,
		InExpr => TypeCode.Bool,
		InSubqueryExpr => TypeCode.Bool,
		InUnnestExpr => TypeCode.Bool,
		ExistsExpr => TypeCode.Bool,
		// CASE WHEN — infer from THEN/ELSE branches
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case
		CaseExpr c => c.Whens.Count > 0 ? InferType(c.Whens[0].Result, table)
			: c.Else != null ? InferType(c.Else, table)
			: TypeCode.String,
		// Scalar subquery — infer from the first output column
		ScalarSubqueryExpr => TypeCode.String, // Cannot easily infer without executing
		ParameterExpr => TypeCode.String, // Parameter types not known at inference time
		_ => TypeCode.String
	};

	private static TypeCode InferFunctionReturnType(FunctionCallExpr func, TableDefinition? table)
	{
		var name = func.Name.ToUpperInvariant();

		// Aggregate functions whose return type depends on the argument type
		if (name is "SUM" or "MIN" or "MAX" or "ANY_VALUE" or "BIT_AND" or "BIT_OR" or "BIT_XOR"
			&& func.Arguments.Count > 0)
		{
			return InferType(func.Arguments[0], table);
		}

		// Functions that return the same type as their first non-null argument
		if (name is "ABS" or "MOD" or "SIGN" or "ROUND"
				or "SAFE_ADD" or "SAFE_SUBTRACT" or "SAFE_MULTIPLY" or "SAFE_NEGATE"
				or "NULLIF"
			&& func.Arguments.Count > 0)
		{
			return InferType(func.Arguments[0], table);
		}

		// GREATEST/LEAST/COALESCE — infer from first non-null-literal argument
		if (name is "GREATEST" or "LEAST" or "COALESCE" && func.Arguments.Count > 0)
		{
			return InferFirstNonNullArgType(func, table);
		}

		// IF(cond, then, else) — infer from the then expression
		if (name is "IF" && func.Arguments.Count > 1)
			return InferType(func.Arguments[1], table);
		// IFNULL(expr, default) — infer from the first non-null argument type
		if (name is "IFNULL" && func.Arguments.Count > 1)
			return InferFirstNonNullArgType(func, table);
		if (name is "IFNULL" && func.Arguments.Count > 0)
			return InferType(func.Arguments[0], table);

		// Navigation window functions — return type of their first argument
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions
		if (name is "LAG" or "LEAD" or "FIRST_VALUE" or "LAST_VALUE" or "NTH_VALUE"
			&& func.Arguments.Count > 0)
		{
			return InferType(func.Arguments[0], table);
		}

		return name switch
		{
			"COUNT" or "COUNTIF" => TypeCode.Int64,
			"AVG" or "IEEE_DIVIDE" or "SAFE_DIVIDE" => TypeCode.Float64,
			"LOGICAL_AND" or "LOGICAL_OR" => TypeCode.Bool,
			"STRING_AGG" => TypeCode.String,
			"ARRAY_AGG" => TypeCode.Array,

			// Window functions returning INT64
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
			"ROW_NUMBER" or "RANK" or "DENSE_RANK" or "NTILE" => TypeCode.Int64,

			// Window functions returning FLOAT64
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
			"PERCENT_RANK" or "CUME_DIST" => TypeCode.Float64,

			// String functions returning INT64
			"LENGTH" or "CHAR_LENGTH" or "CHARACTER_LENGTH" or "STRPOS" or "INSTR"
				or "BYTE_LENGTH" or "ASCII" or "UNICODE" => TypeCode.Int64,

			// String functions returning STRING
			"LOWER" or "UPPER" or "LCASE" or "UCASE" or "TRIM" or "LTRIM" or "RTRIM"
				or "CONCAT" or "SUBSTR" or "SUBSTRING" or "REPLACE" or "REVERSE"
				or "LPAD" or "RPAD" or "REPEAT" or "FORMAT" or "LEFT" or "RIGHT"
				or "REGEXP_EXTRACT" or "REGEXP_REPLACE" or "INITCAP" or "TRANSLATE"
				or "TO_HEX" or "CHR" or "CODE_POINTS_TO_STRING" or "SOUNDEX"
				or "SPLIT" => TypeCode.String,

			// String functions returning BOOL
			"STARTS_WITH" or "ENDS_WITH" or "REGEXP_CONTAINS" or "CONTAINS_SUBSTR" => TypeCode.Bool,

			// Math functions returning FLOAT64
			"CEIL" or "CEILING" or "FLOOR" or "TRUNC" or "SQRT" or "POW" or "POWER"
				or "EXP" or "LN" or "LOG" or "LOG10" or "RAND" => TypeCode.Float64,

			// Math functions returning INT64
			"DIV" or "RANGE_BUCKET" => TypeCode.Int64,

			// Math functions returning BOOL
			"IS_NAN" or "IS_INF" => TypeCode.Bool,

			// Date/Time
			"CURRENT_TIMESTAMP" or "TIMESTAMP" or "TIMESTAMP_ADD" or "TIMESTAMP_SUB"
				or "TIMESTAMP_TRUNC" or "PARSE_TIMESTAMP" or "TIMESTAMP_SECONDS"
				or "TIMESTAMP_MILLIS" or "TIMESTAMP_MICROS" or "PENDING_COMMIT_TIMESTAMP" => TypeCode.Timestamp,
			"CURRENT_DATE" or "DATE" or "DATE_ADD" or "DATE_SUB" or "DATE_TRUNC"
				or "PARSE_DATE" => TypeCode.Date,

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions
			"__INTERVAL__" or "MAKE_INTERVAL" or "JUSTIFY_DAYS" or "JUSTIFY_HOURS"
				or "JUSTIFY_INTERVAL" => TypeCode.Interval,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
			// EXTRACT(DATE FROM ...) returns DATE; all other parts return INT64.
			"EXTRACT" when func.Arguments.Count > 0 && func.Arguments[0] is LiteralExpr lit
				&& string.Equals(lit.Value?.ToString(), "DATE", StringComparison.OrdinalIgnoreCase) => TypeCode.Date,
			"EXTRACT" or "TIMESTAMP_DIFF" or "DATE_DIFF" or "UNIX_SECONDS"
				or "UNIX_MILLIS" or "UNIX_MICROS"
				or "GET_NEXT_SEQUENCE_VALUE" or "GET_INTERNAL_SEQUENCE_STATE" => TypeCode.Int64,
			"FORMAT_TIMESTAMP" or "FORMAT_DATE" => TypeCode.String,

			// Conversion
			"TO_JSON" or "TO_JSON_STRING" => TypeCode.String,
			"PARSE_JSON" => TypeCode.Json,

			// Array
			"ARRAY_LENGTH" => TypeCode.Int64,
			"ARRAY_CONCAT" or "ARRAY_REVERSE" or "GENERATE_ARRAY" => TypeCode.Array,
			"ARRAY_TO_STRING" => TypeCode.String,
			"ARRAY_INCLUDES" => TypeCode.Bool,

			// JSON
			"JSON_VALUE" or "JSON_QUERY" or "JSON_TYPE" => TypeCode.String,
			"JSON_QUERY_ARRAY" => TypeCode.Array,

			// Byte
			"FROM_HEX" => TypeCode.Bytes,

			// Full-text search: boolean functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
			"SEARCH" or "SEARCH_SUBSTRING" or "SEARCH_NGRAMS" => TypeCode.Bool,
			// Full-text search: scoring functions (return FLOAT64)
			"SCORE" or "SCORE_NGRAMS" => TypeCode.Float64,
			// Full-text search: SNIPPET returns JSON string
			"SNIPPET" => TypeCode.String,
			// Full-text search: DEBUG_TOKENLIST returns STRING
			"DEBUG_TOKENLIST" => TypeCode.String,
			// TOKENLIST-producing functions — use Unspecified as placeholder
			// (TOKENLIST values are internal, not projected to result sets)
			"TOKEN" or "TOKENIZE_FULLTEXT" or "TOKENIZE_SUBSTRING" or "TOKENIZE_NGRAMS"
				or "TOKENIZE_NUMBER" or "TOKENIZE_BOOL" or "TOKENIZE_JSON"
				or "TOKENLIST_CONCAT" => TypeCode.Unspecified,

			_ => TypeCode.String
		};
	}

	private static TypeCode InferBinaryType(BinaryExpr bin, TableDefinition? table) => bin.Op switch
	{
		BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.LessThan or BinaryOp.GreaterThan
			or BinaryOp.LessThanOrEqual or BinaryOp.GreaterThanOrEqual or BinaryOp.And or BinaryOp.Or => TypeCode.Bool,
		BinaryOp.Concat => TypeCode.String,
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
		//   If either operand is FLOAT64, the result is FLOAT64.
		BinaryOp.Add or BinaryOp.Subtract or BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.Modulo =>
			InferType(bin.Left, table) == TypeCode.Float64 || InferType(bin.Right, table) == TypeCode.Float64
				? TypeCode.Float64
				: InferType(bin.Left, table),
		_ => TypeCode.String
	};

	private static TypeCode InferFirstNonNullArgType(FunctionCallExpr func, TableDefinition? table)
	{
		foreach (var arg in func.Arguments)
		{
			var t = InferType(arg, table);
			if (t != TypeCode.String || arg is not LiteralExpr { Value: null })
				return t;
		}
		return TypeCode.String;
	}

	private static bool HasAggregates(List<SelectColumn> columns)
	{
		return columns.Any(c => ContainsAggregate(c.Expr));
	}

	private static bool IsAggregate(SqlExpression expr) => expr switch
	{
		CountStarExpr => true,
		FunctionCallExpr func => func.Name.ToUpperInvariant() is
			"COUNT" or "SUM" or "AVG" or "MIN" or "MAX"
			or "ARRAY_AGG" or "STRING_AGG" or "COUNTIF" or "ANY_VALUE"
			or "LOGICAL_AND" or "LOGICAL_OR"
			or "BIT_AND" or "BIT_OR" or "BIT_XOR"
			or "STDDEV" or "STDDEV_SAMP" or "VAR_SAMP" or "VARIANCE"
			or "ARRAY_CONCAT_AGG",
		_ => false
	};

	/// <summary>
	/// Recursively checks whether an expression contains any aggregate function call.
	/// </summary>
	private static bool ContainsAggregate(SqlExpression expr) => expr switch
	{
		_ when IsAggregate(expr) => true,
		FunctionCallExpr func => func.Arguments.Any(ContainsAggregate),
		BinaryExpr bin => ContainsAggregate(bin.Left) || ContainsAggregate(bin.Right),
		UnaryExpr un => ContainsAggregate(un.Operand),
		CaseExpr cs => cs.Whens.Any(w => ContainsAggregate(w.Condition) || ContainsAggregate(w.Result))
			|| (cs.Else != null && ContainsAggregate(cs.Else)),
		_ => false
	};

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
	//   Supported numbering / window functions:
	//   IS_FIRST(k) — GCP Spanner native
	//   ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST — standard SQL
	//   (supported by Go emulator via ZetaSQL; not supported by real GCP Spanner)
	private List<Dictionary<string, object?>> EvaluateWindowFunctions(
		List<SelectColumn> columns,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		var windowCols = columns.Where(c => c.Expr is WindowExpr).ToList();
		if (windowCols.Count == 0)
			return rows;

		foreach (var col in windowCols)
		{
			var win = (WindowExpr)col.Expr;
			var funcName = win.Function switch
			{
				FunctionCallExpr f => f.Name.ToUpperInvariant(),
				CountStarExpr => "$COUNT_STAR",
				_ => ""
			};

			var partitions = PartitionRows(rows, win.PartitionBy, evaluator);
			var resultAlias = col.Alias ?? InferColumnNameStatic(col.Expr);

			foreach (var partition in partitions)
			{
				var sorted = SortRows(partition, win.OrderBy, evaluator);

				switch (funcName)
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#is_first
					//   IS_FIRST(k) returns TRUE if the current row is among the first k rows
					//   in its partition, ordered by the ORDER BY clause.
					case "IS_FIRST":
					{
						var isFirstFunc = (FunctionCallExpr)win.Function;
						if (isFirstFunc.Arguments.Count != 1)
							throw new InvalidOperationException("IS_FIRST requires exactly 1 argument.");
						var kVal = evaluator.Evaluate(isFirstFunc.Arguments[0], rows.FirstOrDefault() ?? new Dictionary<string, object?>());
						int k = Convert.ToInt32(kVal ?? throw new InvalidOperationException("IS_FIRST: argument cannot be NULL."));
						if (k < 0) throw new InvalidOperationException("IS_FIRST: argument must be non-negative.");
						for (int i = 0; i < sorted.Count; i++)
							sorted[i][resultAlias] = i < k;
						break;
					}

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number
					//   ROW_NUMBER() returns a sequential integer starting at 1.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "ROW_NUMBER":
						for (int i = 0; i < sorted.Count; i++)
							sorted[i][resultAlias] = (long)(i + 1);
						break;

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank
					//   RANK() returns the rank with gaps for ties.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "RANK":
					{
						for (int i = 0; i < sorted.Count; i++)
						{
							if (i == 0)
							{
								sorted[i][resultAlias] = 1L;
							}
							else
							{
								bool tie = win.OrderBy != null && win.OrderBy.All(ob =>
								{
									var prev = evaluator.Evaluate(ob.Expr, sorted[i - 1]);
									var curr = evaluator.Evaluate(ob.Expr, sorted[i]);
									return CompareNullable(prev, curr) == 0;
								});
								sorted[i][resultAlias] = tie ? (long)sorted[i - 1][resultAlias]! : (long)(i + 1);
							}
						}
						break;
					}

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#dense_rank
					//   DENSE_RANK() returns the rank without gaps for ties.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "DENSE_RANK":
					{
						long currentRank = 0;
						for (int i = 0; i < sorted.Count; i++)
						{
							if (i == 0)
							{
								currentRank = 1;
							}
							else
							{
								bool tie = win.OrderBy != null && win.OrderBy.All(ob =>
								{
									var prev = evaluator.Evaluate(ob.Expr, sorted[i - 1]);
									var curr = evaluator.Evaluate(ob.Expr, sorted[i]);
									return CompareNullable(prev, curr) == 0;
								});
								if (!tie) currentRank++;
							}
							sorted[i][resultAlias] = currentRank;
						}
						break;
					}

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#ntile
					//   NTILE(num_buckets) divides rows into roughly equal buckets.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "NTILE":
					{
						var ntileFunc = (FunctionCallExpr)win.Function;
						if (ntileFunc.Arguments.Count != 1)
							throw new InvalidOperationException("NTILE requires exactly 1 argument.");
						var nVal = evaluator.Evaluate(ntileFunc.Arguments[0], rows.FirstOrDefault() ?? new Dictionary<string, object?>());
						int numBuckets = Convert.ToInt32(nVal ?? throw new InvalidOperationException("NTILE: argument cannot be NULL."));
						if (numBuckets <= 0) throw new InvalidOperationException("NTILE: argument must be positive.");
						int total = sorted.Count;
						int baseSize = total / numBuckets;
						int extra = total % numBuckets;
						int idx = 0;
						for (int bucket = 1; bucket <= numBuckets && idx < total; bucket++)
						{
							int bucketSize = baseSize + (bucket <= extra ? 1 : 0);
							for (int j = 0; j < bucketSize && idx < total; j++, idx++)
								sorted[idx][resultAlias] = (long)bucket;
						}
						break;
					}

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#percent_rank
					//   PERCENT_RANK() = (rank - 1) / (partition_size - 1). Returns 0 for single-row partitions.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "PERCENT_RANK":
					{
						// First compute RANK values
						var ranks = new long[sorted.Count];
						for (int i = 0; i < sorted.Count; i++)
						{
							if (i == 0) { ranks[i] = 1; continue; }
							bool tie = win.OrderBy != null && win.OrderBy.All(ob =>
							{
								var prev = evaluator.Evaluate(ob.Expr, sorted[i - 1]);
								var curr = evaluator.Evaluate(ob.Expr, sorted[i]);
								return CompareNullable(prev, curr) == 0;
							});
							ranks[i] = tie ? ranks[i - 1] : (long)(i + 1);
						}
						int n = sorted.Count;
						for (int i = 0; i < n; i++)
							sorted[i][resultAlias] = n <= 1 ? 0.0 : (double)(ranks[i] - 1) / (n - 1);
						break;
					}

					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#cume_dist
					//   CUME_DIST() = count(rows with value <= current) / partition_size.
					//   Note: Not supported by real GCP Spanner — Go emulator parity via ZetaSQL.
					case "CUME_DIST":
					{
						int n = sorted.Count;
						for (int i = 0; i < n; i++)
						{
							// Count how many rows have ORDER BY values <= this row's values
							// All rows with the same ORDER BY values get the same cume_dist
							int countLeq = 0;
							for (int j = 0; j < n; j++)
							{
								bool leq = win.OrderBy == null || win.OrderBy.All(ob =>
								{
									var vj = evaluator.Evaluate(ob.Expr, sorted[j]);
									var vi = evaluator.Evaluate(ob.Expr, sorted[i]);
									int cmp = CompareNullable(vj, vi);
									return ob.Order == SortOrder.Desc ? cmp >= 0 : cmp <= 0;
								});
								if (leq) countLeq++;
							}
							sorted[i][resultAlias] = (double)countLeq / n;
						}
						break;
					}

					default:
						throw new NotSupportedException($"Unsupported built-in function: {funcName}.");
				}
			}
		}

		return rows;
	}

	private List<List<Dictionary<string, object?>>> PartitionRows(
		List<Dictionary<string, object?>> rows,
		List<SqlExpression>? partitionBy,
		ExpressionEvaluator evaluator)
	{
		if (partitionBy == null || partitionBy.Count == 0)
			return new List<List<Dictionary<string, object?>>> { rows };

		return rows.GroupBy(r =>
		{
			var key = new List<object?>();
			foreach (var expr in partitionBy)
				key.Add(evaluator.Evaluate(expr, r));
			return string.Join("|", key.Select(k => k?.ToString() ?? "NULL"));
		}).Select(g => g.ToList()).ToList();
	}

	private List<Dictionary<string, object?>> SortRows(
		List<Dictionary<string, object?>> rows,
		List<OrderByColumn>? orderBy,
		ExpressionEvaluator evaluator)
	{
		if (orderBy == null || orderBy.Count == 0) return rows;

		rows.Sort((a, b) =>
		{
			foreach (var clause in orderBy)
			{
				var va = evaluator.Evaluate(clause.Expr, a);
				var vb = evaluator.Evaluate(clause.Expr, b);
				int cmp = CompareNullable(va, vb);
				if (clause.Order == SortOrder.Desc) cmp = -cmp;
				if (cmp != 0) return cmp;
			}
			return 0;
		});
		return rows;
	}

	private static int CompareNullable(object? a, object? b)
	{
		if (a == null && b == null) return 0;
		if (a == null) return -1;
		if (b == null) return 1;
		if (a is IComparable ca) return ca.CompareTo(Convert.ChangeType(b, a.GetType()));
		return 0;
	}

	private List<Dictionary<string, object?>> ExecuteGroupBy(
		SelectStatement select,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		var groups = rows.GroupBy(r =>
		{
			var key = new List<object?>();
			foreach (var groupExpr in select.GroupBy!)
			{
				key.Add(evaluator.Evaluate(groupExpr, r));
			}
			return new GroupKey(key);
		}, new GroupKeyComparer());

		var result = new List<Dictionary<string, object?>>();
		foreach (var group in groups)
		{
			var groupRows = group.ToList();
			var outputRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

			// Set group key columns
			for (int i = 0; i < select.GroupBy!.Count; i++)
			{
				var name = InferColumnName(select.GroupBy[i]);
				outputRow[name] = group.Key.Values[i];
			}

			// Evaluate SELECT columns, computing aggregates over the group
			// First, precompute all aggregates (including nested ones) so
			// the expression evaluator can find them in the row.
			foreach (var col in select.Columns)
			{
				PrecomputeAggregatesForHaving(col.Expr, groupRows, evaluator, outputRow);
			}

			foreach (var col in select.Columns)
			{
				var name = col.Alias ?? InferColumnName(col.Expr);
				if (IsAggregate(col.Expr))
				{
					outputRow[name] = EvaluateAggregate(col.Expr, groupRows, evaluator);
				}
				else if (!outputRow.ContainsKey(name))
				{
					// Non-aggregate columns should be group-by columns, but may contain
					// nested aggregates (e.g., CASE WHEN SUM(x) > ... ). Merge outputRow
					// (with precomputed aggregates) into groupRows[0] for evaluation.
					var evalRow = MergeRows(outputRow, groupRows[0]);
					outputRow[name] = evaluator.Evaluate(col.Expr, evalRow);
				}
			}

			// Pre-compute aggregate expressions in HAVING clause
			// so HAVING evaluation can look them up as row values
			if (select.Having != null)
			{
				PrecomputeAggregatesForHaving(select.Having, groupRows, evaluator, outputRow);
			}

			result.Add(outputRow);
		}

		return result;
	}

	/// <summary>
	/// Walks the HAVING expression tree, finds aggregate subexpressions, evaluates
	/// them against the group, and stores results in the output row using their
	/// canonical name so the expression evaluator can resolve them later.
	/// </summary>
	private static void PrecomputeAggregatesForHaving(
		SqlExpression expr,
		List<Dictionary<string, object?>> groupRows,
		ExpressionEvaluator evaluator,
		Dictionary<string, object?> outputRow)
	{
		switch (expr)
		{
			case CountStarExpr:
				outputRow["COUNT(*)"] = (long)groupRows.Count;
				break;
			case FunctionCallExpr func when IsAggregate(func):
				var name = InferColumnName(func);
				if (!outputRow.ContainsKey(name))
					outputRow[name] = EvaluateAggregate(func, groupRows, evaluator);
				break;
			case FunctionCallExpr func:
				// Non-aggregate function — recurse into arguments to find nested aggregates
				foreach (var arg in func.Arguments)
					PrecomputeAggregatesForHaving(arg, groupRows, evaluator, outputRow);
				break;
			case BinaryExpr bin:
				PrecomputeAggregatesForHaving(bin.Left, groupRows, evaluator, outputRow);
				PrecomputeAggregatesForHaving(bin.Right, groupRows, evaluator, outputRow);
				break;
			case UnaryExpr un:
				PrecomputeAggregatesForHaving(un.Operand, groupRows, evaluator, outputRow);
				break;
			case CaseExpr caseExpr:
				foreach (var when in caseExpr.Whens)
				{
					PrecomputeAggregatesForHaving(when.Condition, groupRows, evaluator, outputRow);
					PrecomputeAggregatesForHaving(when.Result, groupRows, evaluator, outputRow);
				}
				if (caseExpr.Else != null)
					PrecomputeAggregatesForHaving(caseExpr.Else, groupRows, evaluator, outputRow);
				break;
			case InExpr inExpr:
				PrecomputeAggregatesForHaving(inExpr.Value, groupRows, evaluator, outputRow);
				foreach (var item in inExpr.List)
					PrecomputeAggregatesForHaving(item, groupRows, evaluator, outputRow);
				break;
			case BetweenExpr betweenExpr:
				PrecomputeAggregatesForHaving(betweenExpr.Value, groupRows, evaluator, outputRow);
				PrecomputeAggregatesForHaving(betweenExpr.Low, groupRows, evaluator, outputRow);
				PrecomputeAggregatesForHaving(betweenExpr.High, groupRows, evaluator, outputRow);
				break;
		}
	}

	private List<Dictionary<string, object?>> ExecuteWholeTableAggregation(
		SelectStatement select,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		var outputRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

		// Precompute all nested aggregates (e.g., ARRAY_LENGTH(ARRAY_AGG(Name)))
		// so the expression evaluator can find them in the row.
		foreach (var col in select.Columns)
		{
			PrecomputeAggregatesForHaving(col.Expr, rows, evaluator, outputRow);
		}

		foreach (var col in select.Columns)
		{
			var name = col.Alias ?? InferColumnName(col.Expr);
			if (IsAggregate(col.Expr))
			{
				outputRow[name] = EvaluateAggregate(col.Expr, rows, evaluator);
			}
			else if (!outputRow.ContainsKey(name))
			{
				outputRow[name] = rows.Count > 0 ? evaluator.Evaluate(col.Expr, outputRow.Count > 0 ? MergeRows(outputRow, rows[0]) : rows[0]) : null;
			}
		}

		return new List<Dictionary<string, object?>> { outputRow };
	}

	/// <summary>
	/// Merges precomputed aggregate values into a source row for expression evaluation.
	/// </summary>
	private static Dictionary<string, object?> MergeRows(Dictionary<string, object?> precomputed, Dictionary<string, object?> source)
	{
		var merged = new Dictionary<string, object?>(source, StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in precomputed)
		{
			merged[kvp.Key] = kvp.Value;
		}
		return merged;
	}

	private static object? EvaluateAggregate(SqlExpression expr, List<Dictionary<string, object?>> rows, ExpressionEvaluator evaluator)
	{
		if (expr is CountStarExpr)
		{
			return (long)rows.Count;
		}

		if (expr is FunctionCallExpr func)
		{
			var funcName = func.Name.ToUpperInvariant();
			var values = rows
				.Select(r => evaluator.Evaluate(func.Arguments[0], r))
				.Where(v => v != null)
				.ToList();

			if (func.IsDistinct)
			{
				values = values.Distinct(new ObjectValueComparer()).ToList();
			}

			return funcName switch
			{
				"COUNT" => (long)values.Count,
				"SUM" => values.Count == 0 ? null : SumValues(values),
				"AVG" => values.Count == 0 ? null : AvgValues(values),
				"MIN" => values.Count == 0 ? null : values.Aggregate((a, b) =>
					ExpressionEvaluator.CompareValues(a, b) <= 0 ? a : b),
				"MAX" => values.Count == 0 ? null : values.Aggregate((a, b) =>
					ExpressionEvaluator.CompareValues(a, b) >= 0 ? a : b),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
				"ARRAY_AGG" => ArrayAggOrdered(func, rows, evaluator, values),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
				"STRING_AGG" => StringAgg(func, rows, evaluator, values),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#countif
				"COUNTIF" => CountIf(func, rows, evaluator),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#any_value
				"ANY_VALUE" => values.Count == 0 ? null : values[0],
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and
				"LOGICAL_AND" => values.Count == 0 ? null : values.All(v => v is true),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_or
				"LOGICAL_OR" => values.Count == 0 ? null : values.Any(v => v is true),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_and
				"BIT_AND" => values.Count == 0 ? null : values.Aggregate((a, b) =>
					(long)a! & (long)b!),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_or
				"BIT_OR" => values.Count == 0 ? null : values.Aggregate((a, b) =>
					(long)a! | (long)b!),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_xor
				"BIT_XOR" => values.Count == 0 ? null : values.Aggregate((a, b) =>
					(long)a! ^ (long)b!),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#stddev
				"STDDEV" or "STDDEV_SAMP" => StddevSamp(values),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#var_samp
				"VAR_SAMP" or "VARIANCE" => VarSamp(values),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_concat_agg
				"ARRAY_CONCAT_AGG" => ArrayConcatAgg(values),
				_ => throw new NotSupportedException($"Aggregate function '{func.Name}' not supported.")
			};
		}

		throw new NotSupportedException($"Cannot evaluate aggregate: {expr.GetType().Name}");
	}

	private static object? SumValues(List<object?> values)
	{
		if (values[0] is long)
			return values.Sum(v => (long)v!);
		return values.Sum(v => Convert.ToDouble(v));
	}

	private static object? AvgValues(List<object?> values)
	{
		if (values[0] is long)
			return (double)values.Sum(v => (long)v!) / values.Count;
		return values.Sum(v => Convert.ToDouble(v)) / values.Count;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#stddev
	//   "Returns the sample standard deviation of non-NULL values."
	private static object? StddevSamp(List<object?> values)
	{
		if (values.Count < 2) return null;
		var doubles = values.Select(v => Convert.ToDouble(v)).ToArray();
		var mean = doubles.Average();
		var variance = doubles.Sum(v => (v - mean) * (v - mean)) / (doubles.Length - 1);
		return Math.Sqrt(variance);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#var_samp
	//   "Returns the sample variance of non-NULL values."
	private static object? VarSamp(List<object?> values)
	{
		if (values.Count < 2) return null;
		var doubles = values.Select(v => Convert.ToDouble(v)).ToArray();
		var mean = doubles.Average();
		return doubles.Sum(v => (v - mean) * (v - mean)) / (doubles.Length - 1);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_concat_agg
	//   "Concatenates elements from arrays into a single array."
	private static object? ArrayConcatAgg(List<object?> values)
	{
		if (values.Count == 0) return null;
		var result = new List<object?>();
		foreach (var val in values)
		{
			if (val is System.Collections.IList list)
				foreach (var item in list) result.Add(item);
		}
		return result;
	}

	// STRING_AGG(expr, delimiter [ORDER BY ...]): concatenates string values with a delimiter.
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	private static object? StringAgg(FunctionCallExpr func, List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator, List<object?> values)
	{
		if (values.Count == 0) return null;
		var delimiter = func.Arguments.Count > 1
			? evaluator.Evaluate(func.Arguments[1], rows[0])?.ToString() ?? ","
			: ",";

		// Apply ORDER BY within the aggregate if specified
		if (func.AggregateOrderBy != null && func.AggregateOrderBy.Count > 0)
		{
			var sortedRows = OrderRows(func.AggregateOrderBy, rows, evaluator);
			var sortedValues = sortedRows
				.Select(r => evaluator.Evaluate(func.Arguments[0], r))
				.Where(v => v != null);
			if (func.IsDistinct)
				sortedValues = sortedValues.Distinct(new ObjectValueComparer());
			return string.Join(delimiter, sortedValues.Select(v => v?.ToString()));
		}

		return string.Join(delimiter, values.Select(v => v?.ToString()));
	}

	// ARRAY_AGG(expr [ORDER BY ...]): collects values into an array.
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	private static object? ArrayAggOrdered(FunctionCallExpr func, List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator, List<object?> values)
	{
		if (func.AggregateOrderBy != null && func.AggregateOrderBy.Count > 0)
		{
			var sortedRows = OrderRows(func.AggregateOrderBy, rows, evaluator);
			var sortedValues = sortedRows
				.Select(r => evaluator.Evaluate(func.Arguments[0], r))
				.Where(v => v != null);
			if (func.IsDistinct)
				sortedValues = sortedValues.Distinct(new ObjectValueComparer());
			return sortedValues.ToList();
		}
		return values.ToList();
	}

	// COUNTIF(expr): counts rows where the boolean expression is true.
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#countif
	private static object CountIf(FunctionCallExpr func, List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		return (long)rows.Count(r =>
		{
			var val = evaluator.Evaluate(func.Arguments[0], r);
			return val is true;
		});
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
	// TABLESAMPLE BERNOULLI: each row independently selected with probability size%.
	// TABLESAMPLE RESERVOIR: exactly size rows chosen uniformly at random (or all rows if fewer).
	private static List<Dictionary<string, object?>> ApplyTableSample(
		List<Dictionary<string, object?>> rows, TableSampleClause sample)
	{
		var rng = new Random();

		return sample.Method switch
		{
			TableSampleMethod.Bernoulli => rows.Where(_ => rng.NextDouble() * 100 < sample.Size).ToList(),
			TableSampleMethod.Reservoir => ReservoirSample(rows, (int)sample.Size, rng),
			_ => rows
		};
	}

	private static List<Dictionary<string, object?>> ReservoirSample(
		List<Dictionary<string, object?>> rows, int k, Random rng)
	{
		if (k >= rows.Count) return new List<Dictionary<string, object?>>(rows);

		var reservoir = new List<Dictionary<string, object?>>(rows.Take(k));
		for (var i = k; i < rows.Count; i++)
		{
			var j = rng.Next(i + 1);
			if (j < k) reservoir[j] = rows[i];
		}

		return reservoir;
	}

	private static List<Dictionary<string, object?>> DistinctRows(
		List<Dictionary<string, object?>> rows, IReadOnlyList<ColumnDef> columns)
	{
		var seen = new HashSet<string>();
		var result = new List<Dictionary<string, object?>>();

		foreach (var row in rows)
		{
			var key = string.Join("|", columns.Select(c =>
				row.TryGetValue(c.Name, out var v) ? v?.ToString() ?? "NULL" : "NULL"));
			if (seen.Add(key))
			{
				result.Add(row);
			}
		}

		return result;
	}

	private static List<Dictionary<string, object?>> OrderRows(
		List<OrderByColumn> orderBy,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

		for (int i = 0; i < orderBy.Count; i++)
		{
			var ob = orderBy[i];
			var idx = i;

			if (idx == 0)
			{
				ordered = ob.Order == SortOrder.Asc
					? rows.OrderBy(r => evaluator.Evaluate(ob.Expr, r), ValueComparer.Instance)
					: rows.OrderByDescending(r => evaluator.Evaluate(ob.Expr, r), ValueComparer.Instance);
			}
			else
			{
				ordered = ob.Order == SortOrder.Asc
					? ordered!.ThenBy(r => evaluator.Evaluate(ob.Expr, r), ValueComparer.Instance)
					: ordered!.ThenByDescending(r => evaluator.Evaluate(ob.Expr, r), ValueComparer.Instance);
			}
		}

		return ordered?.ToList() ?? rows;
	}

	// ── Subquery / CTE / Set Operation helpers ──

	/// <summary>Converts a ResultSet back to rows for subquery/CTE evaluation.</summary>
	private static List<Dictionary<string, object?>> ResultSetToRows(ResultSet resultSet)
	{
		var rows = new List<Dictionary<string, object?>>();
		if (resultSet.Metadata?.RowType == null) return rows;
		var fields = resultSet.Metadata.RowType.Fields;
		foreach (var row in resultSet.Rows)
		{
			var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < fields.Count && i < row.Values.Count; i++)
			{
				dict[fields[i].Name] = TypeConverter.FromProtobufValue(row.Values[i], fields[i].Type.Code);
			}
			rows.Add(dict);
		}
		return rows;
	}

	/// <summary>Executes a subquery and returns its rows as dictionaries.</summary>
	internal List<Dictionary<string, object?>> ExecuteSubquery(SelectStatement subquery,
		IDictionary<string, object?>? parameters, Dictionary<string, SelectStatement>? cteMap = null,
		Dictionary<string, object?>? outerRow = null)
	{
		var result = Execute(subquery, parameters, cteMap, outerRow);
		return ResultSetToRows(result);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	private static ResultSet ApplySetOperation(ResultSet left, ResultSet right, SetOperationType opType)
	{
		var leftRows = left.Rows.ToList();
		var rightRows = right.Rows.ToList();

		var combinedRows = opType switch
		{
			SetOperationType.UnionAll => leftRows.Concat(rightRows).ToList(),
			SetOperationType.UnionDistinct => leftRows.Concat(rightRows).Distinct(new ListValueComparer()).ToList(),
			SetOperationType.IntersectAll => IntersectAll(leftRows, rightRows),
			SetOperationType.IntersectDistinct => leftRows.Intersect(rightRows, new ListValueComparer()).ToList(),
			SetOperationType.ExceptAll => ExceptAll(leftRows, rightRows),
			SetOperationType.ExceptDistinct => leftRows.Except(rightRows, new ListValueComparer()).ToList(),
			_ => throw new NotSupportedException($"Set operation '{opType}' not supported.")
		};

		var result = new ResultSet { Metadata = left.Metadata };
		result.Rows.AddRange(combinedRows);
		return result;
	}

	private static List<Google.Protobuf.WellKnownTypes.ListValue> IntersectAll(
		List<Google.Protobuf.WellKnownTypes.ListValue> left,
		List<Google.Protobuf.WellKnownTypes.ListValue> right)
	{
		var rightBag = right.ToList();
		var comparer = new ListValueComparer();
		var result = new List<Google.Protobuf.WellKnownTypes.ListValue>();
		foreach (var row in left)
		{
			var idx = rightBag.FindIndex(r => comparer.Equals(r, row));
			if (idx >= 0)
			{
				result.Add(row);
				rightBag.RemoveAt(idx);
			}
		}
		return result;
	}

	private static List<Google.Protobuf.WellKnownTypes.ListValue> ExceptAll(
		List<Google.Protobuf.WellKnownTypes.ListValue> left,
		List<Google.Protobuf.WellKnownTypes.ListValue> right)
	{
		var rightBag = right.ToList();
		var comparer = new ListValueComparer();
		var result = new List<Google.Protobuf.WellKnownTypes.ListValue>();
		foreach (var row in left)
		{
			var idx = rightBag.FindIndex(r => comparer.Equals(r, row));
			if (idx >= 0)
			{
				rightBag.RemoveAt(idx);
			}
			else
			{
				result.Add(row);
			}
		}
		return result;
	}

	private class ListValueComparer : IEqualityComparer<Google.Protobuf.WellKnownTypes.ListValue>
	{
		public bool Equals(Google.Protobuf.WellKnownTypes.ListValue? x, Google.Protobuf.WellKnownTypes.ListValue? y)
		{
			if (x == null || y == null) return x == y;
			if (x.Values.Count != y.Values.Count) return false;
			for (int i = 0; i < x.Values.Count; i++)
			{
				if (x.Values[i].ToString() != y.Values[i].ToString()) return false;
			}
			return true;
		}

		public int GetHashCode(Google.Protobuf.WellKnownTypes.ListValue obj)
		{
			var hash = new HashCode();
			foreach (var v in obj.Values) hash.Add(v.ToString());
			return hash.ToHashCode();
		}
	}

	// ── JOIN execution ──

	/// <summary>
	/// Creates a row dictionary where each column is accessible by both
	/// its short name ("Col") and qualified name ("Alias.Col").
	/// </summary>
	private static Dictionary<string, object?> PrefixRow(
		IDictionary<string, object?> columns, string alias)
	{
		var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in columns)
		{
			row[kvp.Key] = kvp.Value;
			row[$"{alias}.{kvp.Key}"] = kvp.Value;
		}
		return row;
	}

	private List<Dictionary<string, object?>> ExecuteJoin(
		List<Dictionary<string, object?>> leftRows,
		JoinClause join,
		ExpressionEvaluator evaluator,
		Dictionary<string, SelectStatement>? cteMap = null)
	{
		List<Dictionary<string, object?>> rightRows;
		TableDefinition? rightTable = null;
		var rightAlias = join.Alias ?? join.Table;

		if (join.UnnestExpr != null)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#comma_cross_join
			//   "FROM table, UNNEST(array_col) AS alias" — implicit CROSS JOIN with UNNEST.
			//   The UNNEST expression is evaluated per left row (it can reference left columns).
			var result = new List<Dictionary<string, object?>>();
			var alias = join.Alias ?? "unnest";
			foreach (var leftRow in leftRows)
			{
				var arrayValue = evaluator.Evaluate(join.UnnestExpr, leftRow);
				if (arrayValue is IList<object?> list)
				{
					for (var i = 0; i < list.Count; i++)
					{
						var combined = new Dictionary<string, object?>(leftRow, StringComparer.OrdinalIgnoreCase)
						{
							[alias] = list[i]
						};
						if (join.UnnestWithOffset)
						{
							combined[join.UnnestOffsetAlias ?? "offset"] = (long)i;
						}
						result.Add(combined);
					}
				}
			}
			return result;
		}
		else if (join.Subquery != null)
		{
			// Subquery join: JOIN (SELECT ...) alias ON ...
			var subResult = Execute(join.Subquery, null, cteMap);
			rightRows = ResultSetToRows(subResult)
				.Select(r => PrefixRow(r, rightAlias))
				.ToList();
		}
		else if (cteMap != null && cteMap.TryGetValue(join.Table, out var cteQuery))
		{
			// CTE join: JOIN CteAlias ON ...
			var cteResult = Execute(cteQuery, null, cteMap);
			rightRows = ResultSetToRows(cteResult)
				.Select(r => PrefixRow(r, rightAlias))
				.ToList();
		}
		else
		{
			if (!_database.Schema.TryGetTable(join.Table, out rightTable) || rightTable == null)
				throw new InvalidOperationException($"Table '{join.Table}' not found.");

			rightRows = rightTable.Rows.Values
				.Select(r => PrefixRow(r.Columns, rightAlias))
				.ToList();
		}

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#using_clause
		//   "JOIN ... USING (col)" is equivalent to "JOIN ... ON left.col = right.col"
		var onExpr = join.On;
		if (onExpr == null && join.UsingColumns is { Count: > 0 })
		{
			// Determine left table prefix from existing row keys
			var leftPrefix = leftRows.Count > 0
				? leftRows[0].Keys.FirstOrDefault(k => k.Contains('.'))?.Split('.')[0]
				: null;
			SqlExpression? combined = null;
			foreach (var col in join.UsingColumns)
			{
					SqlExpression leftRef = leftPrefix != null
					? new ColumnRefExpr(leftPrefix, col)
					: new ColumnRefExpr(null, col);
				SqlExpression rightRef = new ColumnRefExpr(rightAlias, col);
				var eq = new BinaryExpr(leftRef, BinaryOp.Equal, rightRef);
				combined = combined == null ? eq : new BinaryExpr(combined, BinaryOp.And, eq);
			}
			onExpr = combined;
		}

		return join.Type switch
		{
			JoinType.Inner => InnerJoin(leftRows, rightRows, onExpr, evaluator),
			JoinType.Left => LeftJoin(leftRows, rightRows, onExpr, evaluator, rightTable, rightAlias),
			JoinType.Right => RightJoin(leftRows, rightRows, onExpr, evaluator),
			JoinType.Full => FullJoin(leftRows, rightRows, onExpr, evaluator, rightTable, rightAlias),
			JoinType.Cross => CrossJoin(leftRows, rightRows),
			_ => throw new NotSupportedException($"Join type '{join.Type}' is not supported.")
		};
	}

	private static Dictionary<string, object?> CombineRows(
		Dictionary<string, object?> left, Dictionary<string, object?> right)
	{
		var combined = new Dictionary<string, object?>(left, StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in right)
		{
			combined[kvp.Key] = kvp.Value;
		}
		return combined;
	}

	private static Dictionary<string, object?> NullRow(TableDefinition? table, string alias,
		List<Dictionary<string, object?>>? sampleRows = null)
	{
		var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		if (table != null)
		{
			foreach (var col in table.Columns)
			{
				row[col.Name] = null;
				row[$"{alias}.{col.Name}"] = null;
			}
		}
		else if (sampleRows is { Count: > 0 })
		{
			foreach (var key in sampleRows[0].Keys)
			{
				row[key] = null;
			}
		}
		return row;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#join_types
	//   "An INNER JOIN, or simply JOIN, effectively calculates the Cartesian product of the
	//    two from_items and discards all rows that do not meet the join condition."
	private static List<Dictionary<string, object?>> InnerJoin(
		List<Dictionary<string, object?>> leftRows,
		List<Dictionary<string, object?>> rightRows,
		SqlExpression? on,
		ExpressionEvaluator evaluator)
	{
		var result = new List<Dictionary<string, object?>>();
		foreach (var left in leftRows)
		{
			foreach (var right in rightRows)
			{
				var combined = CombineRows(left, right);
				if (on == null || evaluator.EvaluateAsBool(on, combined))
				{
					result.Add(combined);
				}
			}
		}
		return result;
	}

	private static List<Dictionary<string, object?>> LeftJoin(
		List<Dictionary<string, object?>> leftRows,
		List<Dictionary<string, object?>> rightRows,
		SqlExpression? on,
		ExpressionEvaluator evaluator,
		TableDefinition? rightTable,
		string rightAlias)
	{
		var result = new List<Dictionary<string, object?>>();
		foreach (var left in leftRows)
		{
			bool matched = false;
			foreach (var right in rightRows)
			{
				var combined = CombineRows(left, right);
				if (on == null || evaluator.EvaluateAsBool(on, combined))
				{
					result.Add(combined);
					matched = true;
				}
			}
			if (!matched)
			{
				var nullRight = NullRow(rightTable, rightAlias, rightRows);
				result.Add(CombineRows(left, nullRight));
			}
		}
		return result;
	}

	private static List<Dictionary<string, object?>> RightJoin(
		List<Dictionary<string, object?>> leftRows,
		List<Dictionary<string, object?>> rightRows,
		SqlExpression? on,
		ExpressionEvaluator evaluator)
	{
		// Right join = swap left/right of a left join, then unswap column order
		var result = new List<Dictionary<string, object?>>();
		foreach (var right in rightRows)
		{
			bool matched = false;
			foreach (var left in leftRows)
			{
				var combined = CombineRows(left, right);
				if (on == null || evaluator.EvaluateAsBool(on, combined))
				{
					result.Add(combined);
					matched = true;
				}
			}
			if (!matched)
			{
				// Left side is null - we need to create null entries for left columns
				var nullLeft = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				if (leftRows.Count > 0)
				{
					foreach (var key in leftRows[0].Keys)
						nullLeft[key] = null;
				}
				result.Add(CombineRows(nullLeft, right));
			}
		}
		return result;
	}

	private static List<Dictionary<string, object?>> FullJoin(
		List<Dictionary<string, object?>> leftRows,
		List<Dictionary<string, object?>> rightRows,
		SqlExpression? on,
		ExpressionEvaluator evaluator,
		TableDefinition? rightTable,
		string rightAlias)
	{
		var result = new List<Dictionary<string, object?>>();
		var matchedRight = new HashSet<int>();

		foreach (var left in leftRows)
		{
			bool matched = false;
			for (int i = 0; i < rightRows.Count; i++)
			{
				var combined = CombineRows(left, rightRows[i]);
				if (on == null || evaluator.EvaluateAsBool(on, combined))
				{
					result.Add(combined);
					matched = true;
					matchedRight.Add(i);
				}
			}
			if (!matched)
			{
				var nullRight = NullRow(rightTable, rightAlias, rightRows);
				result.Add(CombineRows(left, nullRight));
			}
		}

		// Unmatched right rows
		for (int i = 0; i < rightRows.Count; i++)
		{
			if (!matchedRight.Contains(i))
			{
				var nullLeft = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				if (leftRows.Count > 0)
				{
					foreach (var key in leftRows[0].Keys)
						nullLeft[key] = null;
				}
				result.Add(CombineRows(nullLeft, rightRows[i]));
			}
		}

		return result;
	}

	private static List<Dictionary<string, object?>> CrossJoin(
		List<Dictionary<string, object?>> leftRows,
		List<Dictionary<string, object?>> rightRows)
	{
		var result = new List<Dictionary<string, object?>>();
		foreach (var left in leftRows)
		{
			foreach (var right in rightRows)
			{
				result.Add(CombineRows(left, right));
			}
		}
		return result;
	}

	// ── Helper types ──

	private record GroupKey(List<object?> Values);

	private class GroupKeyComparer : IEqualityComparer<GroupKey>
	{
		public bool Equals(GroupKey? x, GroupKey? y)
		{
			if (x == null || y == null) return x == y;
			if (x.Values.Count != y.Values.Count) return false;
			for (int i = 0; i < x.Values.Count; i++)
			{
				if (ExpressionEvaluator.CompareValues(x.Values[i], y.Values[i]) != 0)
					return false;
			}
			return true;
		}

		public int GetHashCode(GroupKey obj)
		{
			var hash = new HashCode();
			foreach (var v in obj.Values)
				hash.Add(v?.ToString()?.ToUpperInvariant());
			return hash.ToHashCode();
		}
	}

	private class ObjectValueComparer : IEqualityComparer<object?>
	{
		public new bool Equals(object? x, object? y) => ExpressionEvaluator.CompareValues(x, y) == 0;
		public int GetHashCode(object? obj) => obj?.ToString()?.GetHashCode() ?? 0;
	}

	private class ValueComparer : IComparer<object?>
	{
		public static readonly ValueComparer Instance = new();
		public int Compare(object? x, object? y) => ExpressionEvaluator.CompareValues(x, y);
	}
}
