using Spanner.InMemoryEmulator.Parsing;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Executes parsed DML statements (INSERT, UPDATE, DELETE) against the in-memory database.
/// </summary>
internal class DmlExecutor
{
	private readonly InMemorySpannerDatabase _database;

	public DmlExecutor(InMemorySpannerDatabase database)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
	}

	/// <summary>
	/// Executes an INSERT statement. Returns the number of rows inserted.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Inserts one or more rows into a table."
	public int ExecuteInsert(InsertStatement insert, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(insert.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{insert.Table}' not found.");

		var evaluator = new ExpressionEvaluator(parameters);
		int count = 0;

		// Build value rows from VALUES clause or SELECT source
		var allValueExprs = insert.ValueRows;
		List<Dictionary<string, object?>>? selectRows = null;

		if (allValueExprs == null && insert.SelectSource != null)
		{
			// INSERT INTO ... SELECT ...
			var queryExecutor = new QueryExecutor(_database);
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
				for (int i = 0; i < insert.Columns.Count && i < selectValues.Count; i++)
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
					// Update existing row
					var existing = table.Rows[rowKey].Columns;
					foreach (var kvp in rowValues)
						existing[kvp.Key] = kvp.Value;
					_database.Schema.ValidateWriteConstraints(insert.Table, existing, rowKey);
					table.Rows[rowKey] = new RowData(existing, DateTimeOffset.UtcNow);
					count++;
					continue;
				}
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_or_ignore
				else if (insert.Mode == InsertMode.InsertOrIgnore)
				{
					// Silently skip
					continue;
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
			table.Rows[rowKey] = new RowData(rowValues, DateTimeOffset.UtcNow);
			count++;
		}

		return count;
	}

	/// <summary>
	/// Executes an UPDATE statement. Returns the number of rows updated.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
	//   "Updates existing rows in a table."
	public int ExecuteUpdate(UpdateStatement update, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(update.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{update.Table}' not found.");

		var evaluator = new ExpressionEvaluator(parameters);
		int count = 0;

		foreach (var kvp in table.Rows.ToList())
		{
			var row = new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase);

			if (update.Where != null && !evaluator.EvaluateAsBool(update.Where, row))
				continue;

			foreach (var set in update.Sets)
			{
				var colDef = table.Columns.FirstOrDefault(
					c => string.Equals(c.Name, set.Column, StringComparison.OrdinalIgnoreCase));
				if (colDef == null)
					throw new InvalidOperationException($"Column '{set.Column}' not found in table '{update.Table}'.");

				row[colDef.Name] = evaluator.Evaluate(set.Value, row);
			}

			// Validate NOT NULL
			foreach (var col in table.Columns)
			{
				if (!col.IsNullable && row.TryGetValue(col.Name, out var val) && val == null)
					throw new InvalidOperationException($"Column '{col.Name}' is NOT NULL but got NULL value.");
			}

			_database.Schema.ValidateWriteConstraints(update.Table, row, kvp.Key);
			table.Rows[kvp.Key] = new RowData(row, DateTimeOffset.UtcNow);
			count++;
		}

		return count;
	}

	/// <summary>
	/// Executes a DELETE statement. Returns the number of rows deleted.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#delete_statement
	//   "Deletes rows from a table."
	public int ExecuteDelete(DeleteStatement delete, IDictionary<string, object?>? parameters)
	{
		if (!_database.Schema.TryGetTable(delete.Table, out var table) || table == null)
			throw new InvalidOperationException($"Table '{delete.Table}' not found.");

		var evaluator = new ExpressionEvaluator(parameters);
		int count = 0;

		foreach (var kvp in table.Rows.ToList())
		{
			var row = new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase);

			if (delete.Where != null && !evaluator.EvaluateAsBool(delete.Where, row))
				continue;

			_database.Schema.HandleInterleavedDelete(delete.Table, kvp.Key);
			if (table.Rows.TryRemove(kvp.Key, out _))
				count++;
		}

		return count;
	}
}
