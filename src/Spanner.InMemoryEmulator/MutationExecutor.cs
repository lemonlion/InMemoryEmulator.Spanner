using Google.Cloud.Spanner.V1;
using Google.Protobuf.WellKnownTypes;

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
					if (!table.Rows.TryGetValue(rowKey, out var existingRow))
					{
						throw new InvalidOperationException(
							$"Row with key [{string.Join(", ", pkValues)}] does not exist in table '{tableName}'.");
					}
					var updatedValues = new Dictionary<string, object?>(existingRow.Columns, StringComparer.OrdinalIgnoreCase);
					foreach (var kvp in rowValues)
					{
						updatedValues[kvp.Key] = kvp.Value;
					}
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
						foreach (var kvp in rowValues)
						{
							upsertValues[kvp.Key] = kvp.Value;
						}
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
					table.Rows.TryRemove(rowKey, out _);
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
				_database.Schema.HandleInterleavedDelete(tableName, key);
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
			_database.Schema.HandleInterleavedDelete(tableName, deleteKey);
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
			_database.Schema.HandleInterleavedDelete(table.Name, key);
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

	private enum MutationMode
	{
		Insert,
		Update,
		InsertOrUpdate,
		Replace
	}
}
