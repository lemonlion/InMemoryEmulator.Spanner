using Spanner.InMemoryEmulator.Parsing;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Result from a DML operation that optionally includes returned rows from a THEN RETURN clause.
/// </summary>
internal record DmlResult(int RowCount, List<Dictionary<string, object?>>? ReturnedRows = null);

/// <summary>
/// Executes parsed DML statements (INSERT, UPDATE, DELETE) against the in-memory database.
/// </summary>
internal class DmlExecutor
{
	private readonly InMemorySpannerDatabase _database;
	private readonly List<DmlUndoEntry>? _undoLog;

	public DmlExecutor(InMemorySpannerDatabase database, List<DmlUndoEntry>? undoLog = null)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_undoLog = undoLog;
	}

	/// <summary>
	/// Records a before-image for rollback support.
	/// </summary>
	private void RecordUndo(string tableName, RowKey key, RowData? originalRow)
	{
		_undoLog?.Add(new DmlUndoEntry(tableName, key, originalRow));
	}

	/// <summary>
	/// Executes an INSERT statement. Returns the number of rows inserted and optionally returned rows.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Inserts one or more rows into a table."
	public DmlResult ExecuteInsert(InsertStatement insert, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(insert.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{insert.Table}' not found.");

		var queryExecutor = new QueryExecutor(_database);
		var evaluator = new ExpressionEvaluator(parameters, queryExecutor);
		int count = 0;
		List<(Dictionary<string, object?> Row, string Action)>? affectedRows =
			insert.Returning != null ? new() : null;

		// Build value rows from VALUES clause or SELECT source
		var allValueExprs = insert.ValueRows;
		List<Dictionary<string, object?>>? selectRows = null;

		if (allValueExprs == null && insert.SelectSource != null)
		{
			// INSERT INTO ... SELECT ...
			selectRows = queryExecutor.ExecuteSubquery(insert.SelectSource, parameters);
		}

		var rowCount = selectRows?.Count ?? allValueExprs?.Count ?? 0;

		for (int rowIdx = 0; rowIdx < rowCount; rowIdx++)
		{
			var rowValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

			if (selectRows != null)
			{
				// From SELECT source
				var selectRow = selectRows[rowIdx];
				var selectValues = selectRow.Values.ToList();
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
				//   "The number of columns must match the number of values."
				if (selectValues.Count != insert.Columns.Count)
					throw new InvalidOperationException(
						$"Column count ({insert.Columns.Count}) does not match value count ({selectValues.Count}).");
				for (int i = 0; i < insert.Columns.Count; i++)
				{
					rowValues[insert.Columns[i]] = selectValues[i];
				}
			}
			else if (allValueExprs != null)
			{
				var valueExprs = allValueExprs[rowIdx];
				if (valueExprs.Count != insert.Columns.Count)
					throw new InvalidOperationException(
						$"Column count ({insert.Columns.Count}) does not match value count ({valueExprs.Count}).");

				var emptyRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < insert.Columns.Count; i++)
				{
					var colName = insert.Columns[i];
					var colDef = table.Columns.FirstOrDefault(
						c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
					if (colDef == null)
						throw new InvalidOperationException($"Column '{colName}' not found in table '{insert.Table}'.");

					rowValues[colDef.Name] = evaluator.Evaluate(valueExprs[i], emptyRow);
				}
			}

			// Populate omitted columns with null so they appear in queries (IS NULL etc.)
			foreach (var colDef in table.Columns)
			{
				if (!rowValues.ContainsKey(colDef.Name))
					rowValues[colDef.Name] = null;
			}

			// Apply DEFAULT and GENERATED column expressions
			var explicitCols = new HashSet<string>(insert.Columns, StringComparer.OrdinalIgnoreCase);
			MutationExecutor.ApplyDefaultsAndGenerated(table, rowValues, explicitCols);

			var pkValues = table.PrimaryKeyColumns
				.Select(pk =>
				{
					if (!rowValues.TryGetValue(pk, out var val))
						throw new InvalidOperationException($"Primary key column '{pk}' not provided.");
					return val;
				})
				.ToArray();
			var rowKey = new RowKey(pkValues);

			if (table.Rows.ContainsKey(rowKey))
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_or_update
				if (insert.Mode == InsertMode.InsertOrUpdate)
				{
					// Update existing row — only modify explicitly specified columns
					RecordUndo(insert.Table, rowKey, table.Rows[rowKey]);
					var existing = new Dictionary<string, object?>(table.Rows[rowKey].Columns, StringComparer.OrdinalIgnoreCase);
					foreach (var col in explicitCols)
					{
						if (rowValues.TryGetValue(col, out var val))
							existing[col] = val;
					}
					// Recompute generated columns after applying new values
					MutationExecutor.ApplyDefaultsAndGenerated(table, existing, explicitCols);
					_database.Schema.ValidateWriteConstraints(insert.Table, existing, rowKey);
					table.Rows[rowKey] = new RowData(existing, DateTimeOffset.UtcNow);
					affectedRows?.Add((new Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase), "UPDATE"));
					count++;
					continue;
				}
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_or_ignore
				else if (insert.Mode == InsertMode.InsertOrIgnore)
				{
					// Silently skip
					continue;
				}
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
				else if (insert.OnConflict != null)
				{
					if (insert.OnConflict.Action == OnConflictAction.DoNothing)
					{
						// Silently skip
						continue;
					}
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_update
					else if (insert.OnConflict.Action == OnConflictAction.DoUpdate)
					{
						RecordUndo(insert.Table, rowKey, table.Rows[rowKey]);
						var existing = table.Rows[rowKey].Columns;

						// Build a context row that includes both existing columns and EXCLUDED.col references
						var contextRow = new Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase);
						// EXCLUDED references the attempted insert values
						foreach (var kvp in rowValues)
							contextRow[$"EXCLUDED.{kvp.Key}"] = kvp.Value;

						// Evaluate WHERE condition if present
						if (insert.OnConflict.UpdateWhere != null)
						{
							var whereResult = evaluator.Evaluate(insert.OnConflict.UpdateWhere, contextRow);
							if (whereResult is not true)
							{
								// WHERE condition not met, skip update
								continue;
							}
						}

						// Apply SET clauses
						foreach (var set in insert.OnConflict.UpdateSets!)
						{
							existing[set.Column] = evaluator.Evaluate(set.Value, contextRow);
						}
						// Recompute generated columns after applying SET values
						var updateCols = new HashSet<string>(
							insert.OnConflict.UpdateSets.Select(s => s.Column), StringComparer.OrdinalIgnoreCase);
						MutationExecutor.ApplyDefaultsAndGenerated(table, existing, updateCols);
						_database.Schema.ValidateWriteConstraints(insert.Table, existing, rowKey);
						table.Rows[rowKey] = new RowData(existing, DateTimeOffset.UtcNow);
						affectedRows?.Add((new Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase), "UPDATE"));
						count++;
						continue;
					}
				}

				throw new InvalidOperationException(
					$"Row with key [{string.Join(", ", pkValues)}] already exists in table '{insert.Table}'.");
			}

			// Validate NOT NULL
			foreach (var col in table.Columns)
			{
				if (!col.IsNullable && rowValues.TryGetValue(col.Name, out var val) && val == null)
					throw new InvalidOperationException($"Column '{col.Name}' is NOT NULL but got NULL value.");
			}

			_database.Schema.ValidateWriteConstraints(insert.Table, rowValues);
			RecordUndo(insert.Table, rowKey, null); // INSERT → undo = delete
			table.Rows[rowKey] = new RowData(rowValues, DateTimeOffset.UtcNow);
			affectedRows?.Add((new Dictionary<string, object?>(rowValues, StringComparer.OrdinalIgnoreCase), "INSERT"));
			count++;
		}

		return new DmlResult(count, BuildReturnedRows(insert.Returning, affectedRows, parameters));
	}

	/// <summary>
	/// Executes an UPDATE statement. Returns the number of rows updated and optionally returned rows.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
	//   "Updates existing rows in a table."
	public DmlResult ExecuteUpdate(UpdateStatement update, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(update.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{update.Table}' not found.");

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
		//   "You cannot update primary key columns."
		foreach (var set in update.Sets)
		{
			if (table.PrimaryKeyColumns.Any(pk =>
				string.Equals(pk, set.Column, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException(
					$"Cannot UPDATE primary key column '{set.Column}' in table '{update.Table}'.");
		}

		var evaluator = new ExpressionEvaluator(parameters, new QueryExecutor(_database));
		int count = 0;
		List<(Dictionary<string, object?> Row, string Action)>? affectedRows =
			update.Returning != null ? new() : null;

		foreach (var kvp in table.Rows.ToList())
		{
			// Ref: https://cloud.google.com/spanner/docs/ttl/working-with-ttl
			//   Expired rows are not visible to DML statements.
			if (table.IsRowExpired(kvp.Value))
				continue;

			var row = new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase);

			if (update.Where != null && !evaluator.EvaluateAsBool(update.Where, row))
				continue;

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
			//   "All SET clause column value expressions are evaluated before any are assigned."
			var originalRow = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);

			foreach (var set in update.Sets)
			{
				var colDef = table.Columns.FirstOrDefault(
					c => string.Equals(c.Name, set.Column, StringComparison.OrdinalIgnoreCase));
				if (colDef == null)
					throw new InvalidOperationException($"Column '{set.Column}' not found in table '{update.Table}'.");

				row[colDef.Name] = evaluator.Evaluate(set.Value, originalRow);
			}

			// Re-evaluate generated columns after applying SET clauses
			var updateExplicitCols = new HashSet<string>(
				update.Sets.Select(s => s.Column), StringComparer.OrdinalIgnoreCase);
			MutationExecutor.ApplyDefaultsAndGenerated(table, row, updateExplicitCols);

			// Validate NOT NULL
			foreach (var col in table.Columns)
			{
				if (!col.IsNullable && row.TryGetValue(col.Name, out var val) && val == null)
					throw new InvalidOperationException($"Column '{col.Name}' is NOT NULL but got NULL value.");
			}

			_database.Schema.ValidateWriteConstraints(update.Table, row, kvp.Key);
			RecordUndo(update.Table, kvp.Key, kvp.Value); // UPDATE → undo = restore original
			table.Rows[kvp.Key] = new RowData(row, DateTimeOffset.UtcNow);
			affectedRows?.Add((new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase), "UPDATE"));
			count++;
		}

		return new DmlResult(count, BuildReturnedRows(update.Returning, affectedRows, parameters));
	}

	/// <summary>
	/// Executes a DELETE statement. Returns the number of rows deleted.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#delete_statement
	//   "Deletes rows from a table."
	public DmlResult ExecuteDelete(DeleteStatement delete, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(delete.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{delete.Table}' not found.");

		var evaluator = new ExpressionEvaluator(parameters, new QueryExecutor(_database));
		List<(Dictionary<string, object?> Row, string Action)>? affectedRows =
			delete.Returning != null ? new() : null;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#delete_statement
		//   Subqueries in WHERE are evaluated once against the initial table state.
		//   Collect matching keys first, then delete, to avoid re-evaluation against modified data.
		var keysToDelete = new List<(RowKey Key, Dictionary<string, object?> Row)>();
		foreach (var kvp in table.Rows.ToList())
		{
			// Ref: https://cloud.google.com/spanner/docs/ttl/working-with-ttl
			//   Expired rows are not visible to DML statements.
			if (table.IsRowExpired(kvp.Value))
				continue;

			var row = new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase);

			if (delete.Where != null && !evaluator.EvaluateAsBool(delete.Where, row))
				continue;

			keysToDelete.Add((kvp.Key, row));
		}

		int count = 0;
		foreach (var (key, row) in keysToDelete)
		{
			_database.Schema.HandleInterleavedDelete(delete.Table, key);
			_database.Schema.HandleForeignKeyDeletes(delete.Table, row);
			if (table.Rows.TryRemove(key, out var removedRow))
			{
				RecordUndo(delete.Table, key, removedRow); // DELETE → undo = restore
				affectedRows?.Add((row, "DELETE"));
				count++;
			}
		}

		return new DmlResult(count, BuildReturnedRows(delete.Returning, affectedRows, parameters));
	}

	/// <summary>
	/// Evaluates a THEN RETURN clause against the affected rows, producing the returned result rows.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#then_return
	//   "Returns data from rows that are modified by a DML statement."
	private List<Dictionary<string, object?>>? BuildReturnedRows(
		ReturningClause? returning,
		List<(Dictionary<string, object?> Row, string Action)>? affectedRows,
		IDictionary<string, object?>? parameters)
	{
		if (returning == null || affectedRows == null)
			return null;

		var evaluator = new ExpressionEvaluator(parameters, new QueryExecutor(_database));
		var result = new List<Dictionary<string, object?>>();

		foreach (var (row, action) in affectedRows)
		{
			var outputRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

			// Evaluate each column expression in the THEN RETURN clause
			foreach (var col in returning.Columns)
			{
				if (col.Expr is StarExpr)
				{
					foreach (var kvp in row)
						outputRow[kvp.Key] = kvp.Value;
				}
				else
				{
					var value = evaluator.Evaluate(col.Expr, row);
					var alias = col.Alias
						?? (col.Expr is ColumnRefExpr cr ? cr.Column : $"_col{outputRow.Count}");
					outputRow[alias] = value;
				}
			}

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#with_action
			//   "WITH ACTION adds an ACTION column that indicates the type of action performed."
			if (returning.WithAction)
			{
				var actionAlias = returning.ActionAlias ?? "ACTION";
				outputRow[actionAlias] = action;
			}

			result.Add(outputRow);
		}

		return result;
	}
}
