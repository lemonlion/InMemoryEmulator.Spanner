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

		// Apply set operations
		if (fullQuery.SetOps is { Count: > 0 })
		{
			foreach (var setOp in fullQuery.SetOps)
			{
				var rightResult = Execute(setOp.Right, parameters, cteMap);
				result = ApplySetOperation(result, rightResult, setOp.Type);
			}
		}

		return result;
	}

	/// <summary>
	/// Executes a parsed SELECT statement and returns the result as a ResultSet protobuf.
	/// </summary>
	public ResultSet Execute(SelectStatement select, IDictionary<string, object?>? parameters,
		Dictionary<string, SelectStatement>? cteMap = null)
	{
		var evaluator = new ExpressionEvaluator(parameters, this, cteMap);

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

			// 1b. JOINs — combine rows from multiple tables
			if (select.From.Joins != null)
			{
				foreach (var join in select.From.Joins)
				{
					rows = ExecuteJoin(rows, join, evaluator);
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
		rows = EvaluateWindowFunctions(select.Columns, rows, evaluator);

		// 6. ORDER BY — sort before projection so all source columns are available
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
		//   ORDER BY can reference columns not in the SELECT list.
		if (select.OrderBy != null && select.OrderBy.Count > 0)
		{
			rows = OrderRows(select.OrderBy, rows, evaluator);
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
				if (sourceTable == null)
					throw new InvalidOperationException("SELECT * requires a FROM clause.");

				foreach (var tablCol in sourceTable.Columns)
				{
					expandedColumns.Add((new ColumnRefExpr(null, tablCol.Name), tablCol.Name));
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
			outputColumns.Add(new ColumnDef(name, typeCode));
		}

		// Project each row
		foreach (var row in rows)
		{
			var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			foreach (var (expr, name) in expandedColumns)
			{
				// If the value is already in the row (e.g., from aggregation or window functions), use it directly
				if (row.ContainsKey(name) && (IsAggregate(expr) || expr is WindowExpr))
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

		return (outputColumns.AsReadOnly(), projectedRows);
	}

	private static string InferColumnName(SqlExpression expr) => expr switch
	{
		ColumnRefExpr col => col.Column,
		FunctionCallExpr func => func.Name,
		CountStarExpr => "COUNT(*)",
		CastExpr cast => InferColumnName(cast.Value),
		WindowExpr win => InferColumnName(win.Function),
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
		UnaryExpr un => InferType(un.Operand, table),
		WindowExpr win => InferType(win.Function, table),
		ArrayAccessExpr => TypeCode.String, // Element type unknown at inference time
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

		// Functions that return the same type as their first argument
		if (name is "ABS" or "MOD" or "SIGN" or "ROUND" or "GREATEST" or "LEAST"
				or "SAFE_ADD" or "SAFE_SUBTRACT" or "SAFE_MULTIPLY" or "SAFE_NEGATE"
				or "NULLIF" or "COALESCE"
			&& func.Arguments.Count > 0)
		{
			return InferType(func.Arguments[0], table);
		}

		// IF(cond, then, else) / IFNULL(expr, default) — infer from the result expression
		if (name is "IF" && func.Arguments.Count > 1)
			return InferType(func.Arguments[1], table);
		if (name is "IFNULL" && func.Arguments.Count > 0)
			return InferType(func.Arguments[0], table);

		return name switch
		{
			"COUNT" or "COUNTIF" => TypeCode.Int64,
			"AVG" or "IEEE_DIVIDE" or "SAFE_DIVIDE" => TypeCode.Float64,
			"LOGICAL_AND" or "LOGICAL_OR" => TypeCode.Bool,
			"STRING_AGG" => TypeCode.String,
			"ARRAY_AGG" => TypeCode.Array,

			// Window functions returning INT64
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
			"ROW_NUMBER" or "RANK" or "DENSE_RANK" => TypeCode.Int64,

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

			_ => TypeCode.String
		};
	}

	private static TypeCode InferBinaryType(BinaryExpr bin, TableDefinition? table) => bin.Op switch
	{
		BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.LessThan or BinaryOp.GreaterThan
			or BinaryOp.LessThanOrEqual or BinaryOp.GreaterThanOrEqual or BinaryOp.And or BinaryOp.Or => TypeCode.Bool,
		BinaryOp.Concat => TypeCode.String,
		BinaryOp.Add or BinaryOp.Subtract or BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.Modulo =>
			InferType(bin.Left, table),
		_ => TypeCode.String
	};

	private static bool HasAggregates(List<SelectColumn> columns)
	{
		return columns.Any(c => IsAggregate(c.Expr));
	}

	private static bool IsAggregate(SqlExpression expr) => expr switch
	{
		CountStarExpr => true,
		FunctionCallExpr func => func.Name.ToUpperInvariant() is
			"COUNT" or "SUM" or "AVG" or "MIN" or "MAX"
			or "ARRAY_AGG" or "STRING_AGG" or "COUNTIF" or "ANY_VALUE"
			or "LOGICAL_AND" or "LOGICAL_OR"
			or "BIT_AND" or "BIT_OR" or "BIT_XOR",
		_ => false
	};

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
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
			var windowName = col.Alias ?? InferColumnName(win);

			// Group rows by PARTITION BY expressions
			var partitions = rows.GroupBy(r =>
			{
				if (win.PartitionBy == null || win.PartitionBy.Count == 0)
					return "";
				return string.Join("|", win.PartitionBy.Select(e => evaluator.Evaluate(e, r)?.ToString() ?? "NULL"));
			}).ToList();

			foreach (var partition in partitions)
			{
				var partitionRows = partition.ToList();

				// Sort within partition by ORDER BY
				if (win.OrderBy != null && win.OrderBy.Count > 0)
				{
					partitionRows = OrderRows(win.OrderBy, partitionRows, evaluator);
				}

				// Evaluate the window function for each row in the partition
				var funcName = win.Function switch
				{
					FunctionCallExpr f => f.Name.ToUpperInvariant(),
					CountStarExpr => "COUNT",
					_ => ""
				};

				for (var i = 0; i < partitionRows.Count; i++)
				{
					var row = partitionRows[i];
					object? value = funcName switch
					{
						"ROW_NUMBER" => (long)(i + 1),
						"RANK" => ComputeRank(partitionRows, i, win.OrderBy, evaluator),
						"DENSE_RANK" => ComputeDenseRank(partitionRows, i, win.OrderBy, evaluator),
						"LAG" => i > 0
							? GetWindowFuncArg(win.Function, partitionRows[i - 1], evaluator)
							: GetWindowFuncDefault(win.Function, evaluator),
						"LEAD" => i < partitionRows.Count - 1
							? GetWindowFuncArg(win.Function, partitionRows[i + 1], evaluator)
							: GetWindowFuncDefault(win.Function, evaluator),
						"FIRST_VALUE" => GetWindowFuncArg(win.Function, partitionRows[0], evaluator),
						"LAST_VALUE" => GetWindowFuncArg(win.Function, partitionRows[^1], evaluator),
						// Aggregate window functions (SUM, COUNT, AVG, MIN, MAX OVER ...)
						_ => EvaluateAggregateWindow(win.Function, partitionRows, evaluator)
					};
					row[windowName] = value;
				}
			}
		}

		return rows;
	}

	private static object? GetWindowFuncArg(SqlExpression func, Dictionary<string, object?> row, ExpressionEvaluator evaluator)
	{
		if (func is FunctionCallExpr f && f.Arguments.Count > 0)
			return evaluator.Evaluate(f.Arguments[0], row);
		return null;
	}

	private static object? GetWindowFuncDefault(SqlExpression func, ExpressionEvaluator evaluator)
	{
		if (func is FunctionCallExpr f && f.Arguments.Count >= 3)
			return evaluator.Evaluate(f.Arguments[2], new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
		return null;
	}

	private static long ComputeRank(
		List<Dictionary<string, object?>> partitionRows, int index,
		List<OrderByColumn>? orderBy, ExpressionEvaluator evaluator)
	{
		if (orderBy == null || orderBy.Count == 0 || index == 0)
			return 1L;

		long rank = 1;
		for (var j = 0; j < index; j++)
		{
			if (!RowsEqualByOrderBy(partitionRows[j], partitionRows[index], orderBy, evaluator))
			{
				rank = j + 2;
			}
		}
		return rank;
	}

	private static long ComputeDenseRank(
		List<Dictionary<string, object?>> partitionRows, int index,
		List<OrderByColumn>? orderBy, ExpressionEvaluator evaluator)
	{
		if (orderBy == null || orderBy.Count == 0 || index == 0)
			return 1L;

		var distinctRank = 1L;
		for (var j = 1; j <= index; j++)
		{
			if (!RowsEqualByOrderBy(partitionRows[j - 1], partitionRows[j], orderBy, evaluator))
			{
				distinctRank++;
			}
		}
		return distinctRank;
	}

	private static bool RowsEqualByOrderBy(
		Dictionary<string, object?> a, Dictionary<string, object?> b,
		List<OrderByColumn> orderBy, ExpressionEvaluator evaluator)
	{
		foreach (var col in orderBy)
		{
			var va = evaluator.Evaluate(col.Expr, a);
			var vb = evaluator.Evaluate(col.Expr, b);
			if (!Equals(va, vb))
				return false;
		}
		return true;
	}

	private static object? EvaluateAggregateWindow(
		SqlExpression func, List<Dictionary<string, object?>> partitionRows, ExpressionEvaluator evaluator)
	{
		var funcName = func switch
		{
			FunctionCallExpr f => f.Name.ToUpperInvariant(),
			CountStarExpr => "COUNT",
			_ => ""
		};

		var values = func switch
		{
			FunctionCallExpr f when f.Arguments.Count > 0 =>
				partitionRows.Select(r => evaluator.Evaluate(f.Arguments[0], r)).ToList(),
			CountStarExpr => partitionRows.Select(_ => (object?)(long)1).ToList(),
			_ => new List<object?>()
		};

		return funcName switch
		{
			"COUNT" => func is CountStarExpr
				? (long)partitionRows.Count
				: (long)values.Count(v => v != null),
			"SUM" => values.Where(v => v != null).Aggregate(0.0, (acc, v) => acc + Convert.ToDouble(v)) is var sum
				? (values.Any(v => v is long) ? (object)(long)sum : sum)
				: null,
			"AVG" => values.Where(v => v != null) is var nonNull && nonNull.Any()
				? nonNull.Average(v => Convert.ToDouble(v))
				: null,
			"MIN" => values.Where(v => v != null).DefaultIfEmpty(null)
				.Min(v => v is IComparable ? v : null),
			"MAX" => values.Where(v => v != null).DefaultIfEmpty(null)
				.Max(v => v is IComparable ? v : null),
			_ => null
		};
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
			foreach (var col in select.Columns)
			{
				var name = col.Alias ?? InferColumnName(col.Expr);
				if (IsAggregate(col.Expr))
				{
					outputRow[name] = EvaluateAggregate(col.Expr, groupRows, evaluator);
				}
				else if (!outputRow.ContainsKey(name))
				{
					// Non-aggregate columns should be group-by columns
					outputRow[name] = evaluator.Evaluate(col.Expr, groupRows[0]);
				}
			}

			result.Add(outputRow);
		}

		return result;
	}

	private List<Dictionary<string, object?>> ExecuteWholeTableAggregation(
		SelectStatement select,
		List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator)
	{
		var outputRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

		foreach (var col in select.Columns)
		{
			var name = col.Alias ?? InferColumnName(col.Expr);
			if (IsAggregate(col.Expr))
			{
				outputRow[name] = EvaluateAggregate(col.Expr, rows, evaluator);
			}
			else
			{
				outputRow[name] = rows.Count > 0 ? evaluator.Evaluate(col.Expr, rows[0]) : null;
			}
		}

		return new List<Dictionary<string, object?>> { outputRow };
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
				"ARRAY_AGG" => values.ToList(),
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

	// STRING_AGG(expr, delimiter): concatenates string values with a delimiter.
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	private static object? StringAgg(FunctionCallExpr func, List<Dictionary<string, object?>> rows,
		ExpressionEvaluator evaluator, List<object?> values)
	{
		if (values.Count == 0) return null;
		var delimiter = func.Arguments.Count > 1
			? evaluator.Evaluate(func.Arguments[1], rows[0])?.ToString() ?? ","
			: ",";
		return string.Join(delimiter, values.Select(v => v?.ToString()));
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
		IDictionary<string, object?>? parameters, Dictionary<string, SelectStatement>? cteMap = null)
	{
		var result = Execute(subquery, parameters, cteMap);
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
		ExpressionEvaluator evaluator)
	{
		if (!_database.Schema.TryGetTable(join.Table, out var rightTable) || rightTable == null)
			throw new InvalidOperationException($"Table '{join.Table}' not found.");

		var rightAlias = join.Alias ?? join.Table;
		var rightRows = rightTable.Rows.Values
			.Select(r => PrefixRow(r.Columns, rightAlias))
			.ToList();

		return join.Type switch
		{
			JoinType.Inner => InnerJoin(leftRows, rightRows, join.On, evaluator),
			JoinType.Left => LeftJoin(leftRows, rightRows, join.On, evaluator, rightTable, rightAlias),
			JoinType.Right => RightJoin(leftRows, rightRows, join.On, evaluator),
			JoinType.Full => FullJoin(leftRows, rightRows, join.On, evaluator, rightTable, rightAlias),
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

	private static Dictionary<string, object?> NullRow(TableDefinition table, string alias)
	{
		var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var col in table.Columns)
		{
			row[col.Name] = null;
			row[$"{alias}.{col.Name}"] = null;
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
		TableDefinition rightTable,
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
				var nullRight = NullRow(rightTable, rightAlias);
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
		TableDefinition rightTable,
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
				var nullRight = NullRow(rightTable, rightAlias);
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
