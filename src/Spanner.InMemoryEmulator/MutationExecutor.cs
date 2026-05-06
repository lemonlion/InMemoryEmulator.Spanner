using Google.Cloud.Spanner.V1;
using Google.Protobuf.WellKnownTypes;
using Superpower;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Applies protobuf <c>Mutation</c> operations to the in-memory database.
/// </summary>
internal class MutationExecutor
{
	private readonly InMemorySpannerDatabase _database;

	public MutationExecutor(InMemorySpannerDatabase database)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
	//   "A single mutation to be applied. Mutations can be applied individually or atomically via Commit."

	/// <summary>
	/// Applies a list of mutations atomically.
	/// </summary>
	public void ApplyMutations(IReadOnlyList<Mutation> mutations, DateTimeOffset commitTimestamp)
	{
		// Ref: https://cloud.google.com/spanner/quotas#limits_for_creating_reading_updating_and_deleting_data
		//   "Maximum number of mutations per commit: 80,000"
		long totalMutations = 0;
		foreach (var m in mutations)
		{
			totalMutations += m.OperationCase switch
			{
				Mutation.OperationOneofCase.Insert => m.Insert.Values.Count * m.Insert.Columns.Count,
				Mutation.OperationOneofCase.Update => m.Update.Values.Count * m.Update.Columns.Count,
				Mutation.OperationOneofCase.InsertOrUpdate => m.InsertOrUpdate.Values.Count * m.InsertOrUpdate.Columns.Count,
				Mutation.OperationOneofCase.Replace => m.Replace.Values.Count * m.Replace.Columns.Count,
				Mutation.OperationOneofCase.Delete => m.Delete.KeySet.All ? 1 : m.Delete.KeySet.Keys.Count,
				_ => 0
			};
		}
		if (totalMutations > 80_000)
			throw new InvalidOperationException(
				$"The transaction exceeds the mutation limit. Number of mutations: {totalMutations}. Limit: 80000.");

		foreach (var mutation in mutations)
		{
			switch (mutation.OperationCase)
			{
				case Mutation.OperationOneofCase.Insert:
					ApplyWrite(mutation.Insert, MutationMode.Insert, commitTimestamp);
					break;
				case Mutation.OperationOneofCase.Update:
					ApplyWrite(mutation.Update, MutationMode.Update, commitTimestamp);
					break;
				case Mutation.OperationOneofCase.InsertOrUpdate:
					ApplyWrite(mutation.InsertOrUpdate, MutationMode.InsertOrUpdate, commitTimestamp);
					break;
				case Mutation.OperationOneofCase.Replace:
					ApplyWrite(mutation.Replace, MutationMode.Replace, commitTimestamp);
					break;
				case Mutation.OperationOneofCase.Delete:
					ApplyDelete(mutation.Delete);
					break;
				default:
					throw new InvalidOperationException($"Unknown mutation operation: {mutation.OperationCase}");
			}
		}
	}

	private void ApplyWrite(Mutation.Types.Write write, MutationMode mode, DateTimeOffset commitTimestamp)
	{
		var tableName = write.Table;
		if (!_database.Schema.TryGetTable(tableName, out var table) || table == null)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
			//   "Table must exist."
			throw new InvalidOperationException($"Table '{tableName}' not found.");
		}

		var columnNames = write.Columns.ToList();

		foreach (var valueList in write.Values)
		{
			if (valueList.Values.Count != columnNames.Count)
			{
				throw new InvalidOperationException(
					$"Column count ({columnNames.Count}) does not match value count ({valueList.Values.Count}) for table '{tableName}'.");
			}

			// Convert protobuf values to .NET types
			var rowValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < columnNames.Count; i++)
			{
				var colName = columnNames[i];
				var colDef = table.Columns.FirstOrDefault(
					c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
				if (colDef == null)
				{
					throw new InvalidOperationException($"Column '{colName}' not found in table '{tableName}'.");
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
				//   "For commit timestamp columns, the string 'spanner.commit_timestamp()' can be set."
				var rawValue = valueList.Values[i];
				if (rawValue.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue
					&& rawValue.StringValue == "spanner.commit_timestamp()")
				{
					if (!colDef.AllowCommitTimestamp)
						throw new InvalidOperationException(
							$"Column '{colName}' does not allow commit timestamps.");
					rowValues[colDef.Name] = commitTimestamp.UtcDateTime;
					continue;
				}

				var value = TypeConverter.FromProtobufValue(rawValue, colDef.SpannerType);
				rowValues[colDef.Name] = value;
			}

			// Populate omitted columns with null so they appear in queries
			foreach (var colDef in table.Columns)
			{
				if (!rowValues.ContainsKey(colDef.Name))
					rowValues[colDef.Name] = null;
			}

			// Apply DEFAULT and GENERATED column expressions
			var explicitCols = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
			ApplyDefaultsAndGenerated(table, rowValues, explicitCols);

			// Build the row key from PK columns
			var pkValues = table.PrimaryKeyColumns
				.Select(pk =>
				{
					if (!rowValues.TryGetValue(pk, out var val))
					{
						throw new InvalidOperationException($"Primary key column '{pk}' not provided for table '{tableName}'.");
					}
					return val;
				})
				.ToArray();
			var rowKey = new RowKey(pkValues);

			switch (mode)
			{
				case MutationMode.Insert:
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
					//   "Insert: Inserts new rows. Fails if any of the specified rows already exist."
					if (table.Rows.ContainsKey(rowKey))
					{
						throw new InvalidOperationException(
							$"Row with key [{string.Join(", ", pkValues)}] already exists in table '{tableName}'.");
					}
					ValidateNotNull(table, rowValues);
					_database.Schema.ValidateWriteConstraints(tableName, rowValues);
					table.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
					break;

				case MutationMode.Update:
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
					//   "Update: Updates existing rows. Fails if any specified row does not exist."
					//   "Only values for the listed columns will be updated."
					if (!table.Rows.TryGetValue(rowKey, out var existingRow))
					{
						throw new InvalidOperationException(
							$"Row with key [{string.Join(", ", pkValues)}] does not exist in table '{tableName}'.");
					}
					var updatedValues = new Dictionary<string, object?>(existingRow.Columns, StringComparer.OrdinalIgnoreCase);
					foreach (var kvp in rowValues)
					{
						if (explicitCols.Contains(kvp.Key))
							updatedValues[kvp.Key] = kvp.Value;
					}
					// Re-evaluate generated columns after merging updated values
					ApplyDefaultsAndGenerated(table, updatedValues, explicitCols);
					ValidateNotNull(table, updatedValues);
					_database.Schema.ValidateWriteConstraints(tableName, updatedValues, rowKey);
					table.Rows[rowKey] = new RowData(updatedValues, commitTimestamp);
					break;

				case MutationMode.InsertOrUpdate:
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
					//   "InsertOrUpdate: Like insert, except that if the row already exists, then its column values
					//    are overwritten with the ones provided."
					if (table.Rows.TryGetValue(rowKey, out var existingForUpsert))
					{
						var upsertValues = new Dictionary<string, object?>(existingForUpsert.Columns, StringComparer.OrdinalIgnoreCase);
						// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
						//   "InsertOrUpdate ... its column values are overwritten with the ones provided."
						foreach (var kvp in rowValues)
						{
							if (explicitCols.Contains(kvp.Key))
								upsertValues[kvp.Key] = kvp.Value;
						}
						ApplyDefaultsAndGenerated(table, upsertValues, explicitCols);
						ValidateNotNull(table, upsertValues);
						_database.Schema.ValidateWriteConstraints(tableName, upsertValues, rowKey);
						table.Rows[rowKey] = new RowData(upsertValues, commitTimestamp);
					}
					else
					{
						ValidateNotNull(table, rowValues);
						_database.Schema.ValidateWriteConstraints(tableName, rowValues);
						table.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
					}
					break;

				case MutationMode.Replace:
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
					//   "Replace: Like insert, except that if the row already exists, it is deleted,
					//    and the column values provided are inserted instead."
					//   "In an interleaved table, if you create the child table with the ON DELETE CASCADE
					//    annotation, then replacing a parent row also deletes the child rows."
					if (table.Rows.ContainsKey(rowKey))
					{
						_database.Schema.HandleInterleavedDelete(tableName, rowKey);
						table.Rows.TryRemove(rowKey, out _);
					}
					ValidateNotNull(table, rowValues);
					_database.Schema.ValidateWriteConstraints(tableName, rowValues);
					table.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
					break;
			}
		}
	}

	private void ApplyDelete(Mutation.Types.Delete delete)
	{
		var tableName = delete.Table;
		if (!_database.Schema.TryGetTable(tableName, out var table) || table == null)
		{
			throw new InvalidOperationException($"Table '{tableName}' not found.");
		}

		var keySet = delete.KeySet;

		if (keySet.All)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.KeySet
			//   "For convenience all can be set to true to indicate all rows of a table."
			foreach (var key in table.Rows.Keys.ToList())
			{
				if (table.Rows.TryGetValue(key, out var rowData))
				{
					var rowValues = new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase);
					_database.Schema.HandleInterleavedDelete(tableName, key);
					_database.Schema.HandleForeignKeyDeletes(tableName, rowValues);
				}
				else
				{
					_database.Schema.HandleInterleavedDelete(tableName, key);
				}
			}
			table.Rows.Clear();
			return;
		}

		// Delete by specific keys
		foreach (var keyList in keySet.Keys)
		{
			var pkValues = new object?[keyList.Values.Count];
			for (int i = 0; i < keyList.Values.Count; i++)
			{
				var pkCol = table.PrimaryKeyColumns[i];
				var colDef = table.Columns.First(
					c => string.Equals(c.Name, pkCol, StringComparison.OrdinalIgnoreCase));
				pkValues[i] = TypeConverter.FromProtobufValue(keyList.Values[i], colDef.SpannerType);
			}
			var deleteKey = new RowKey(pkValues);
			if (table.Rows.TryGetValue(deleteKey, out var rowData))
			{
				var rowValues = new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase);
				_database.Schema.HandleInterleavedDelete(tableName, deleteKey);
				_database.Schema.HandleForeignKeyDeletes(tableName, rowValues);
			}
			else
			{
				_database.Schema.HandleInterleavedDelete(tableName, deleteKey);
			}
			table.Rows.TryRemove(deleteKey, out _);
		}

		// Delete by key ranges
		foreach (var range in keySet.Ranges)
		{
			DeleteByKeyRange(table, range);
		}
	}

	private void DeleteByKeyRange(TableDefinition table, KeyRange range)
	{
		// Build start/end keys
		var startKey = BuildKeyFromListValue(table, range.StartClosed ?? range.StartOpen);
		var endKey = BuildKeyFromListValue(table, range.EndClosed ?? range.EndOpen);
		var startInclusive = range.StartClosed != null;
		var endInclusive = range.EndClosed != null;

		var keysToRemove = table.Rows.Keys
			.Where(k =>
			{
				var startCmp = k.CompareTo(startKey);
				var endCmp = k.CompareTo(endKey);
				var afterStart = startInclusive ? startCmp >= 0 : startCmp > 0;
				var beforeEnd = endInclusive ? endCmp <= 0 : endCmp < 0;
				return afterStart && beforeEnd;
			})
			.ToList();

		foreach (var key in keysToRemove)
		{
			if (table.Rows.TryGetValue(key, out var rowData))
			{
				var rowValues = new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase);
				_database.Schema.HandleInterleavedDelete(table.Name, key);
				_database.Schema.HandleForeignKeyDeletes(table.Name, rowValues);
			}
			else
			{
				_database.Schema.HandleInterleavedDelete(table.Name, key);
			}
			table.Rows.TryRemove(key, out _);
		}
	}

	private RowKey BuildKeyFromListValue(TableDefinition table, ListValue? listValue)
	{
		if (listValue == null || listValue.Values.Count == 0)
			return new RowKey(Array.Empty<object?>());

		var values = new object?[listValue.Values.Count];
		for (int i = 0; i < listValue.Values.Count; i++)
		{
			var pkCol = table.PrimaryKeyColumns[i];
			var colDef = table.Columns.First(
				c => string.Equals(c.Name, pkCol, StringComparison.OrdinalIgnoreCase));
			values[i] = TypeConverter.FromProtobufValue(listValue.Values[i], colDef.SpannerType);
		}
		return new RowKey(values);
	}

	private static void ValidateNotNull(TableDefinition table, Dictionary<string, object?> values)
	{
		foreach (var col in table.Columns)
		{
			if (!col.IsNullable && values.TryGetValue(col.Name, out var val) && val == null)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1
				//   NOT NULL columns cannot have NULL values.
				throw new InvalidOperationException(
					$"Column '{col.Name}' in table '{table.Name}' is NOT NULL but got NULL value.");
			}
		}
	}

	/// <summary>
	/// Evaluates DEFAULT and GENERATED column expressions for a row.
	/// Applies default values for omitted columns and computes generated columns.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   "DEFAULT (expression)" provides a default value when the column is omitted.
	//   "AS (expression) STORED" defines a stored generated column.
	internal static void ApplyDefaultsAndGenerated(
		TableDefinition table,
		Dictionary<string, object?> rowValues,
		ISet<string>? explicitColumns = null)
	{
		var evaluator = new ExpressionEvaluator(null);

		// Apply DEFAULT expressions for omitted columns
		foreach (var col in table.Columns)
		{
			if (col.DefaultExpression != null
				&& (explicitColumns == null || !explicitColumns.Contains(col.Name))
				&& rowValues.TryGetValue(col.Name, out var val)
				&& val == null)
			{
				try
				{
					var tokens = Parsing.GoogleSqlTokenizer.Tokenize(col.DefaultExpression);
					var expr = Parsing.SqlParsers.Expression.AtEnd().Parse(tokens);
					rowValues[col.Name] = evaluator.Evaluate(expr, rowValues);
				}
				catch
				{
					// If default expression evaluation fails, leave as null
				}
			}
		}

		// Apply GENERATED ALWAYS AS expressions (stored generated columns)
		foreach (var col in table.Columns)
		{
			if (col.GeneratedExpression != null)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#generated_column
				//   "You cannot write directly to a generated column."
				if (explicitColumns != null && explicitColumns.Contains(col.Name))
				{
					throw new InvalidOperationException(
						$"Cannot write to generated column '{col.Name}' in table '{table.Name}'.");
				}

				try
				{
					var tokens = Parsing.GoogleSqlTokenizer.Tokenize(col.GeneratedExpression);
					var expr = Parsing.SqlParsers.Expression.AtEnd().Parse(tokens);
					rowValues[col.Name] = evaluator.Evaluate(expr, rowValues);
				}
				catch
				{
					// If generated expression evaluation fails, leave as null
				}
			}
		}
	}

	private enum MutationMode
	{
		Insert,
		Update,
		InsertOrUpdate,
		Replace
	}
}
