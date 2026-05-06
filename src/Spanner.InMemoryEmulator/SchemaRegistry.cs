using System.Collections.Concurrent;
using Spanner.InMemoryEmulator.Parsing;
using Superpower;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Stores table, index, and view definitions for an in-memory Spanner database.
/// Thread-safe for concurrent access.
/// </summary>
internal class SchemaRegistry
{
	private readonly ConcurrentDictionary<string, TableDefinition> _tables = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, IndexDefinition> _indexes = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ViewDefinition> _views = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, SequenceDefinition> _sequences = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ChangeStreamDefinition> _changeStreams = new(StringComparer.OrdinalIgnoreCase);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#proto_bundle_statements
	//   "PROTO BUNDLE makes proto types available for use by tables and queries."
	private readonly HashSet<string> _protoBundleTypes = new(StringComparer.Ordinal);
	private readonly object _protoBundleLock = new();

	/// <summary>Gets whether a proto bundle exists.</summary>
	public bool HasProtoBundle
	{
		get { lock (_protoBundleLock) return _protoBundleTypes.Count > 0; }
	}

	/// <summary>Gets the current set of proto bundle type names.</summary>
	public IReadOnlySet<string> GetProtoBundleTypes()
	{
		lock (_protoBundleLock)
			return new HashSet<string>(_protoBundleTypes, StringComparer.Ordinal);
	}

	/// <summary>Creates or replaces the proto bundle with the given type names.</summary>
	public void SetProtoBundleTypes(IEnumerable<string> typeNames)
	{
		lock (_protoBundleLock)
		{
			_protoBundleTypes.Clear();
			foreach (var t in typeNames) _protoBundleTypes.Add(t);
		}
	}

	/// <summary>Inserts additional types into an existing proto bundle.</summary>
	public void InsertProtoBundleTypes(IEnumerable<string> typeNames)
	{
		lock (_protoBundleLock)
		{
			foreach (var t in typeNames) _protoBundleTypes.Add(t);
		}
	}

	/// <summary>Removes types from the proto bundle.</summary>
	public void DeleteProtoBundleTypes(IEnumerable<string> typeNames)
	{
		lock (_protoBundleLock)
		{
			foreach (var t in typeNames) _protoBundleTypes.Remove(t);
		}
	}

	/// <summary>Drops the entire proto bundle.</summary>
	public void DropProtoBundle()
	{
		lock (_protoBundleLock) _protoBundleTypes.Clear();
	}

	public void AddTable(TableDefinition table)
	{
		if (!_tables.TryAdd(table.Name, table))
		{
			throw new InvalidOperationException($"Table '{table.Name}' already exists.");
		}
	}

	public bool TryGetTable(string name, out TableDefinition? table)
	{
		return _tables.TryGetValue(name, out table);
	}

	public TableDefinition GetTableDefinition(string tableName)
	{
		if (!_tables.TryGetValue(tableName, out var table))
		{
			throw new InvalidOperationException($"Table '{tableName}' does not exist.");
		}
		return table;
	}

	public void RemoveTable(string name)
	{
		_tables.TryRemove(name, out _);
	}

	public IReadOnlyList<string> GetTableNames()
	{
		return _tables.Keys.ToList().AsReadOnly();
	}

	public void AddIndex(IndexDefinition index)
	{
		if (!_indexes.TryAdd(index.Name, index))
		{
			throw new InvalidOperationException($"Index '{index.Name}' already exists.");
		}
	}

	public void RemoveIndex(string name)
	{
		_indexes.TryRemove(name, out _);
	}

	public bool TryGetIndex(string name, out IndexDefinition? index)
	{
		return _indexes.TryGetValue(name, out index);
	}

	/// <summary>Gets all indexes defined on the given table.</summary>
	public IReadOnlyList<IndexDefinition> GetIndexesForTable(string tableName)
	{
		return _indexes.Values
			.Where(i => string.Equals(i.TableName, tableName, StringComparison.OrdinalIgnoreCase))
			.ToList()
			.AsReadOnly();
	}

	/// <summary>Gets all child tables interleaved in the given parent table.</summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#interleave_in
	public IReadOnlyList<TableDefinition> GetChildTables(string parentTableName)
	{
		return _tables.Values
			.Where(t => string.Equals(t.ParentTable, parentTableName, StringComparison.OrdinalIgnoreCase))
			.ToList()
			.AsReadOnly();
	}

	/// <summary>
	/// Handles interleaved table semantics when deleting a row from a parent table.
	/// CASCADE: deletes all child rows whose PK prefixes match.
	/// NO ACTION: throws if any child rows exist.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#interleave_in
	//   "ON DELETE CASCADE: When a parent row is deleted, also delete the child rows."
	//   "ON DELETE NO ACTION: If any child rows exist, the parent delete fails."
	public void HandleInterleavedDelete(string tableName, RowKey parentKey)
	{
		foreach (var child in GetChildTables(tableName))
		{
			// Child PK prefixes parent PK in interleaved tables
			var parentPkCount = GetTableDefinition(tableName).PrimaryKeyColumns.Count;
			var matchingChildKeys = child.Rows.Keys
				.Where(ck => ck.Values.Length >= parentPkCount &&
					parentKey.Values.SequenceEqual(ck.Values.Take(parentPkCount),
						EqualityComparer<object?>.Default))
				.ToList();

			if (matchingChildKeys.Count == 0) continue;

			if (child.OnDeleteAction == OnDeleteAction.Cascade)
			{
				foreach (var ck in matchingChildKeys)
				{
					// Recursively handle grandchildren
					HandleInterleavedDelete(child.Name, ck);
					child.Rows.TryRemove(ck, out _);
				}
			}
			else // NoAction
			{
				throw new InvalidOperationException(
					$"Cannot delete row from '{tableName}': child rows exist in interleaved table '{child.Name}' (ON DELETE NO ACTION).");
			}
		}
	}

	/// <summary>
	/// Validates all write constraints: unique indexes, CHECK, and FOREIGN KEY.
	/// </summary>
	public void ValidateWriteConstraints(string tableName, Dictionary<string, object?> rowValues, RowKey? currentKey = null)
	{
		ValidateUniqueIndexes(tableName, rowValues, currentKey);
		ValidateCheckConstraints(tableName, rowValues);
		ValidateForeignKeys(tableName, rowValues);
		ValidateColumnLengths(tableName, rowValues);
	}

	/// <summary>
	/// Validates STRING and BYTES column length constraints.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   "STRING(length) or BYTES(length): The maximum number of Unicode characters (STRING) or bytes (BYTES)."
	public void ValidateColumnLengths(string tableName, Dictionary<string, object?> rowValues)
	{
		if (!TryGetTable(tableName, out var table) || table == null) return;

		foreach (var col in table.Columns)
		{
			if (col.MaxLength == null || !rowValues.TryGetValue(col.Name, out var val) || val == null)
				continue;

			if (col.SpannerType == Google.Cloud.Spanner.V1.TypeCode.String && val is string s)
			{
				if (s.Length > col.MaxLength.Value)
					throw new InvalidOperationException(
						$"Value for column '{col.Name}' exceeds maximum length {col.MaxLength}. Got {s.Length} characters.");
			}
			else if (col.SpannerType == Google.Cloud.Spanner.V1.TypeCode.Bytes && val is byte[] bytes)
			{
				if (bytes.Length > col.MaxLength.Value)
					throw new InvalidOperationException(
						$"Value for column '{col.Name}' exceeds maximum length {col.MaxLength}. Got {bytes.Length} bytes.");
			}
		}
	}

	/// <summary>
	/// Validates unique index constraints for a row being written to the given table.
	/// Throws if any unique index is violated.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
	//   "UNIQUE indexes ensure that no two rows in a table contain the same values
	//    for the index key columns."
	public void ValidateUniqueIndexes(string tableName, Dictionary<string, object?> rowValues, RowKey? currentKey = null)
	{
		if (!TryGetTable(tableName, out var table) || table == null) return;

		foreach (var index in GetIndexesForTable(tableName))
		{
			if (!index.IsUnique) continue;

			// Extract index key values from the row
			var indexKeyValues = index.Columns
				.Select(c => rowValues.TryGetValue(c.Name, out var v) ? v : null)
				.ToArray();

			// NULL_FILTERED: skip rows where any index key is null
			if (index.IsNullFiltered && indexKeyValues.Any(v => v == null))
				continue;

			// Check all existing rows for conflicts
			foreach (var (existingKey, existingRow) in table.Rows)
			{
				// Skip the row being updated (same primary key)
				if (currentKey != null && existingKey.Equals(currentKey))
					continue;

				var existingIndexValues = index.Columns
					.Select(c => existingRow.Columns.TryGetValue(c.Name, out var v) ? v : null)
					.ToArray();

				// NULL in unique index: UNIQUE constraint allows multiple NULLs
				if (indexKeyValues.Any(v => v == null))
					continue;

				bool match = true;
				for (int i = 0; i < indexKeyValues.Length; i++)
				{
					if (!Equals(indexKeyValues[i], existingIndexValues[i]))
					{
						match = false;
						break;
					}
				}

				if (match)
				{
					throw new InvalidOperationException(
						$"UNIQUE index '{index.Name}' violation: duplicate key [{string.Join(", ", indexKeyValues)}] in table '{tableName}'.");
				}
			}
		}
	}

	/// <summary>
	/// Validates CHECK constraints for a row being written.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
	//   "The CHECK constraint expression must evaluate to TRUE or NULL for any row."
	public void ValidateCheckConstraints(string tableName, Dictionary<string, object?> rowValues)
	{
		if (!TryGetTable(tableName, out var table) || table == null) return;

		var evaluator = new ExpressionEvaluator(null);
		foreach (var check in table.CheckConstraints)
		{
			try
			{
				var tokens = Parsing.GoogleSqlTokenizer.Tokenize(check.Expression);
				var expr = Parsing.SqlParsers.Expression.AtEnd().Parse(tokens);
				var result = evaluator.Evaluate(expr, rowValues);

				// CHECK passes if result is TRUE or NULL
				if (result is bool b && !b)
				{
					throw new InvalidOperationException(
						$"CHECK constraint '{check.Name ?? "(unnamed)"}' violated for table '{tableName}'. Expression: {check.Expression}");
				}
			}
			catch (InvalidOperationException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Error evaluating CHECK constraint '{check.Name ?? "(unnamed)"}' for table '{tableName}': {ex.Message}", ex);
			}
		}
	}

	/// <summary>
	/// Validates FOREIGN KEY constraints for a row being written.
	/// Ensures referenced row exists in the referenced table.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	//   "ENFORCED Foreign key constraints are validated at transaction commit time."
	public void ValidateForeignKeys(string tableName, Dictionary<string, object?> rowValues)
	{
		if (!TryGetTable(tableName, out var table) || table == null) return;

		foreach (var fk in table.ForeignKeys)
		{
			if (!fk.IsEnforced) continue;

			// Get the FK column values from the row
			var fkValues = fk.Columns
				.Select(c => rowValues.TryGetValue(c, out var v) ? v : null)
				.ToArray();

			// If any FK column is NULL, the constraint is satisfied
			if (fkValues.Any(v => v == null)) continue;

			if (!TryGetTable(fk.ReferencedTable, out var refTable) || refTable == null)
				throw new InvalidOperationException($"Referenced table '{fk.ReferencedTable}' not found for foreign key.");

			// Check if a matching row exists in the referenced table
			bool found = refTable.Rows.Values.Any(row =>
			{
				for (int i = 0; i < fk.ReferencedColumns.Count; i++)
				{
					if (!row.Columns.TryGetValue(fk.ReferencedColumns[i], out var refVal))
						return false;
					if (!Equals(fkValues[i], refVal))
						return false;
				}
				return true;
			});

			if (!found)
			{
				throw new InvalidOperationException(
					$"Foreign key constraint '{fk.Name ?? "(unnamed)"}' violated: no matching row in '{fk.ReferencedTable}' for values [{string.Join(", ", fkValues)}].");
			}
		}
	}

	/// <summary>
	/// Handles FK ON DELETE CASCADE/NO ACTION for all tables that reference the given table.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	//   "ON DELETE CASCADE: Deletes referencing rows when the referenced row is deleted."
	//   "ON DELETE NO ACTION: If referencing rows exist, the delete is rejected."
	public void HandleForeignKeyDeletes(string tableName, Dictionary<string, object?> deletedRowValues)
	{
		// Find all tables with FKs referencing this table
		foreach (var (_, table) in _tables)
		{
			foreach (var fk in table.ForeignKeys)
			{
				if (!string.Equals(fk.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase))
					continue;
				if (!fk.IsEnforced) continue;

				// Build the referenced key values from the deleted row
				var referencedValues = new object?[fk.ReferencedColumns.Count];
				bool anyNull = false;
				for (int i = 0; i < fk.ReferencedColumns.Count; i++)
				{
					if (deletedRowValues.TryGetValue(fk.ReferencedColumns[i], out var val))
						referencedValues[i] = val;
					else
						referencedValues[i] = null;
					if (referencedValues[i] == null) anyNull = true;
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
				//   "NULL values in FK columns are not checked."
				if (anyNull) continue;

				// Find referencing rows in the child table
				var referencingKeys = table.Rows
					.Where(kvp =>
					{
						for (int i = 0; i < fk.Columns.Count; i++)
						{
							var colName = fk.Columns[i];
							if (!kvp.Value.Columns.TryGetValue(colName, out var childVal))
								return false;
							if (childVal == null) return false;
							if (!ValuesEqual(childVal, referencedValues[i])) return false;
						}
						return true;
					})
					.Select(kvp => kvp.Key)
					.ToList();

				if (referencingKeys.Count == 0) continue;

				if (fk.OnDelete == ForeignKeyDeleteAction.Cascade)
				{
					// Delete referencing rows (recursively handles their FKs and interleaved children)
					foreach (var key in referencingKeys)
					{
						// Get the row values before deletion for cascading further
						if (table.Rows.TryGetValue(key, out var childRow))
						{
							var childValues = new Dictionary<string, object?>(childRow.Columns, StringComparer.OrdinalIgnoreCase);
							HandleInterleavedDelete(table.Name, key);
							HandleForeignKeyDeletes(table.Name, childValues);
							table.Rows.TryRemove(key, out _);
						}
					}
				}
				else // NoAction
				{
					throw new InvalidOperationException(
						$"Foreign key constraint '{fk.Name ?? "(unnamed)"}' violation: cannot delete row from '{tableName}' because referencing rows exist in '{table.Name}'.");
				}
			}
		}
	}

	private static bool ValuesEqual(object? a, object? b)
	{
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;
		if (a is long la && b is long lb) return la == lb;
		if (a is double da && b is double db) return Math.Abs(da - db) < double.Epsilon;
		return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
	}

	public void ClearAllData()
	{
		foreach (var table in _tables.Values)
		{
			table.ClearData();
		}
	}

	public void ClearAll()
	{
		_tables.Clear();
		_indexes.Clear();
		_views.Clear();
		_sequences.Clear();
		_changeStreams.Clear();
		DropProtoBundle();
	}

	// ─── VIEWS ───

	public void AddView(ViewDefinition view)
	{
		_views[view.Name] = view; // CREATE OR REPLACE semantics: always overwrite
	}

	public bool TryGetView(string name, out ViewDefinition? view) => _views.TryGetValue(name, out view);

	public void RemoveView(string name) => _views.TryRemove(name, out _);

	public IReadOnlyList<string> GetViewNames() => _views.Keys.ToList();

	public IReadOnlyDictionary<string, ViewDefinition> GetViews() => _views;

	// ─── SEQUENCES ───

	public void AddSequence(SequenceDefinition seq)
	{
		if (!_sequences.TryAdd(seq.Name, seq))
			throw new InvalidOperationException($"Sequence '{seq.Name}' already exists.");
	}

	public bool TryGetSequence(string name, out SequenceDefinition? seq) => _sequences.TryGetValue(name, out seq);

	public void RemoveSequence(string name) => _sequences.TryRemove(name, out _);

	public IReadOnlyDictionary<string, SequenceDefinition> GetSequences() => _sequences;

	// ─── CHANGE STREAMS ───

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
	//   Change streams are schema objects that watch data changes.
	public void AddChangeStream(ChangeStreamDefinition cs)
	{
		_changeStreams[cs.Name] = cs; // CREATE or ALTER both overwrite
	}

	public bool TryGetChangeStream(string name, out ChangeStreamDefinition? cs) => _changeStreams.TryGetValue(name, out cs);

	public void RemoveChangeStream(string name) => _changeStreams.TryRemove(name, out _);

	public IReadOnlyDictionary<string, ChangeStreamDefinition> GetChangeStreams() => _changeStreams;
}
