namespace InMemoryEmulator.Spanner;

/// <summary>
/// In-process Spanner database with schema + data.
/// Thread-safe for concurrent reads and writes.
/// </summary>
public class InMemorySpannerDatabase : IDisposable
{
	private readonly SchemaRegistry _schema = new();
	private readonly InMemorySpannerDatabaseOptions _options;
	private bool _disposed;

	public InMemorySpannerDatabase()
		: this(new InMemorySpannerDatabaseOptions())
	{
	}

	public InMemorySpannerDatabase(InMemorySpannerDatabaseOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	// ─── DDL ───

	/// <summary>Executes a single DDL statement (CREATE TABLE, DROP TABLE, ALTER TABLE, CREATE INDEX, etc.).</summary>
	public void ExecuteDdl(string ddlStatement)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		DdlParser.ExecuteDdl(ddlStatement, _schema);
	}

	/// <summary>Executes multiple DDL statements in order.</summary>
	public void ExecuteDdl(params string[] ddlStatements)
	{
		foreach (var stmt in ddlStatements)
		{
			ExecuteDdl(stmt);
		}
	}

	// ─── Mutations (direct, typed) ───

	/// <summary>Inserts a row. Fails if the primary key already exists.</summary>
	public void Insert(string table, IDictionary<string, object?> columns)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		MutateRow(table, columns, MutateMode.Insert);
	}

	/// <summary>Updates an existing row. Fails if the primary key does not exist.</summary>
	public void Update(string table, IDictionary<string, object?> columns)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		MutateRow(table, columns, MutateMode.Update);
	}

	/// <summary>Inserts or updates a row (upsert).</summary>
	public void InsertOrUpdate(string table, IDictionary<string, object?> columns)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		MutateRow(table, columns, MutateMode.InsertOrUpdate);
	}

	/// <summary>Replaces a row (delete + insert atomically).</summary>
	public void Replace(string table, IDictionary<string, object?> columns)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		MutateRow(table, columns, MutateMode.Replace);
	}

	/// <summary>Deletes a row by primary key values.</summary>
	public void Delete(string table, params object[] primaryKeyValues)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var tableDef = _schema.GetTableDefinition(table);
		var rowKey = new RowKey(primaryKeyValues);
		if (tableDef.Rows.TryGetValue(rowKey, out var rowData))
		{
			var rowValues = new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase);
			_schema.HandleInterleavedDelete(table, rowKey);
			_schema.HandleForeignKeyDeletes(table, rowValues);
		}
		else
		{
			_schema.HandleInterleavedDelete(table, rowKey);
		}
		tableDef.Rows.TryRemove(rowKey, out _);
	}

	/// <summary>Deletes all rows in a key range.</summary>
	public void DeleteRange(string table, object[] startKey, object[] endKey,
		bool startInclusive = true, bool endInclusive = false)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var tableDef = _schema.GetTableDefinition(table);
		var start = new RowKey(startKey);
		var end = new RowKey(endKey);
		var keysToRemove = tableDef.Rows.Keys
			.Where(k =>
			{
				var startCmp = k.CompareTo(start);
				var endCmp = k.CompareTo(end);
				var afterStart = startInclusive ? startCmp >= 0 : startCmp > 0;
				var beforeEnd = endInclusive ? endCmp <= 0 : endCmp < 0;
				return afterStart && beforeEnd;
			})
			.ToList();
		foreach (var key in keysToRemove)
		{
			if (tableDef.Rows.TryGetValue(key, out var rowData))
			{
				var rowValues = new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase);
				_schema.HandleInterleavedDelete(table, key);
				_schema.HandleForeignKeyDeletes(table, rowValues);
			}
			else
			{
				_schema.HandleInterleavedDelete(table, key);
			}
			tableDef.Rows.TryRemove(key, out _);
		}
	}

	// ─── Query ───

	/// <summary>Executes a SQL query and returns the result rows.</summary>
	public List<Dictionary<string, object?>> ExecuteQuery(
		string sql, IDictionary<string, object?>? parameters = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var engine = new SqlEngine(this);
		var resultSet = engine.ExecuteSql(sql, parameters);
		// Convert ResultSet rows back to dictionaries
		var result = new List<Dictionary<string, object?>>();
		if (resultSet.Metadata?.RowType != null)
		{
			foreach (var row in resultSet.Rows)
			{
				var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < resultSet.Metadata.RowType.Fields.Count && i < row.Values.Count; i++)
				{
					var field = resultSet.Metadata.RowType.Fields[i];
					dict[field.Name] = TypeConverter.FromProtobufValue(row.Values[i], field.Type.Code);
				}
				result.Add(dict);
			}
		}
		return result;
	}

	/// <summary>Executes a SQL query and returns a single scalar value.</summary>
	public T ExecuteScalar<T>(string sql, IDictionary<string, object?>? parameters = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var rows = ExecuteQuery(sql, parameters);
		if (rows.Count == 0) throw new InvalidOperationException("Query returned no rows.");
		var firstValue = rows[0].Values.FirstOrDefault();
		if (firstValue is T typed) return typed;
		return (T)Convert.ChangeType(firstValue!, typeof(T));
	}

	// ─── DML ───

	/// <summary>Executes a DML statement (INSERT, UPDATE, DELETE) and returns the number of affected rows.</summary>
	public int ExecuteDml(string dml, IDictionary<string, object?>? parameters = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var engine = new SqlEngine(this);
		var resultSet = engine.ExecuteSql(dml, parameters);
		return (int)(resultSet.Stats?.RowCountExact ?? 0);
	}

	/// <summary>Executes a batch of DML statements and returns per-statement row counts.</summary>
	public int[] ExecuteBatchDml(params (string sql, IDictionary<string, object?>? parameters)[] statements)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var engine = new SqlEngine(this);
		var counts = new int[statements.Length];
		for (int i = 0; i < statements.Length; i++)
		{
			var resultSet = engine.ExecuteSql(statements[i].sql, statements[i].parameters);
			counts[i] = (int)(resultSet.Stats?.RowCountExact ?? 0);
		}
		return counts;
	}

	// ─── Schema Introspection ───

	/// <summary>Returns the names of all tables in the database.</summary>
	public IReadOnlyList<string> GetTableNames() => _schema.GetTableNames();

	/// <summary>Returns the schema definition for a table.</summary>
	public TableDefinition GetTableDefinition(string tableName) => _schema.GetTableDefinition(tableName);

	// ─── State Management ───

	/// <summary>Exports the entire database state (schema + data) as JSON.</summary>
	public string ExportState()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return StatePersistence.Export(_schema);
	}

	/// <summary>Imports database state (schema + data) from JSON. Replaces all existing state.</summary>
	public void ImportState(string json)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		StatePersistence.Import(json, _schema);
	}

	/// <summary>Exports the database state to a file.</summary>
	public void ExportStateToFile(string filePath)
	{
		File.WriteAllText(filePath, ExportState());
	}

	/// <summary>Imports database state from a file.</summary>
	public void ImportStateFromFile(string filePath)
	{
		ImportState(File.ReadAllText(filePath));
	}

	/// <summary>Removes all data but keeps the schema.</summary>
	public void ClearAllData()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_schema.ClearAllData();
	}

	/// <summary>Removes all data and schema.</summary>
	public void ClearAll()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_schema.ClearAll();
	}

	// ─── Internal access for gRPC service layer ───

	internal SchemaRegistry Schema => _schema;

	// ─── Private helpers ───

	private void MutateRow(string table, IDictionary<string, object?> columns, MutateMode mode)
	{
		var tableDef = _schema.GetTableDefinition(table);
		var rowValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in columns)
		{
			// Convert DBNull.Value to null for consistent storage
			rowValues[kvp.Key] = kvp.Value is DBNull ? null : kvp.Value;
		}

		// Build the row key from PK columns
		var pkValues = tableDef.PrimaryKeyColumns
			.Select(pk =>
			{
				if (!rowValues.TryGetValue(pk, out var val))
					throw new InvalidOperationException($"Primary key column '{pk}' not provided for table '{table}'.");
				return val;
			})
			.ToArray();
		var rowKey = new RowKey(pkValues);
		var commitTimestamp = DateTimeOffset.UtcNow;

		switch (mode)
		{
			case MutateMode.Insert:
				if (tableDef.Rows.ContainsKey(rowKey))
					throw new InvalidOperationException(
						$"Row with key [{string.Join(", ", pkValues)}] already exists in table '{table}'.");
				FillMissingNullableColumns(tableDef, rowValues);
				ValidateNotNull(tableDef, rowValues);
				_schema.ValidateWriteConstraints(table, rowValues);
				tableDef.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
				break;

			case MutateMode.Update:
				if (!tableDef.Rows.TryGetValue(rowKey, out var existing))
					throw new InvalidOperationException(
						$"Row with key [{string.Join(", ", pkValues)}] does not exist in table '{table}'.");
				var updated = new Dictionary<string, object?>(existing.Columns, StringComparer.OrdinalIgnoreCase);
				foreach (var kvp in rowValues)
					updated[kvp.Key] = kvp.Value;
				ValidateNotNull(tableDef, updated);
				_schema.ValidateWriteConstraints(table, updated, rowKey);
				tableDef.Rows[rowKey] = new RowData(updated, commitTimestamp);
				break;

			case MutateMode.InsertOrUpdate:
				if (tableDef.Rows.TryGetValue(rowKey, out var existingForUpsert))
				{
					var upserted = new Dictionary<string, object?>(existingForUpsert.Columns, StringComparer.OrdinalIgnoreCase);
					foreach (var kvp in rowValues)
						upserted[kvp.Key] = kvp.Value;
					ValidateNotNull(tableDef, upserted);
					_schema.ValidateWriteConstraints(table, upserted, rowKey);
					tableDef.Rows[rowKey] = new RowData(upserted, commitTimestamp);
				}
				else
				{
					FillMissingNullableColumns(tableDef, rowValues);
					ValidateNotNull(tableDef, rowValues);
					_schema.ValidateWriteConstraints(table, rowValues);
					tableDef.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
				}
				break;

			case MutateMode.Replace:
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
				//   "Like insert, except that if the row already exists, it is deleted, and the column
				//    values provided are inserted instead."
				tableDef.Rows.TryRemove(rowKey, out _);
				FillMissingNullableColumns(tableDef, rowValues);
				ValidateNotNull(tableDef, rowValues);
				_schema.ValidateWriteConstraints(table, rowValues);
				tableDef.Rows[rowKey] = new RowData(rowValues, commitTimestamp);
				break;
		}
	}

	/// <summary>
	/// For Insert and Replace, nullable columns not provided in the mutation should default to null.
	/// </summary>
	private static void FillMissingNullableColumns(TableDefinition table, Dictionary<string, object?> values)
	{
		foreach (var col in table.Columns)
		{
			if (!values.ContainsKey(col.Name) && col.IsNullable)
			{
				values[col.Name] = null;
			}
		}
	}

	private static void ValidateNotNull(TableDefinition table, Dictionary<string, object?> values)
	{
		foreach (var col in table.Columns)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1
			//   NOT NULL columns must be provided and cannot have NULL values.
			if (!col.IsNullable)
			{
				if (!values.ContainsKey(col.Name))
					throw new InvalidOperationException(
						$"Column '{col.Name}' in table '{table.Name}' is NOT NULL but was not provided.");
				if (values[col.Name] == null)
					throw new InvalidOperationException(
						$"Column '{col.Name}' in table '{table.Name}' is NOT NULL but got NULL value.");
			}
		}
	}

	private enum MutateMode
	{
		Insert,
		Update,
		InsertOrUpdate,
		Replace
	}

	// ─── Disposal ───

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			GC.SuppressFinalize(this);
		}
	}
}
