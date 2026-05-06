using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Parsing;
using Superpower;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Parses and applies DDL statements to a <see cref="SchemaRegistry"/>.
/// </summary>
internal static class DdlParser
{
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
	//   "DDL statements define, modify, or remove Spanner resources."

	/// <summary>
	/// Parses a DDL statement string and applies it to the schema.
	/// </summary>
	public static void ExecuteDdl(string ddl, SchemaRegistry schema)
	{
		var trimmed = ddl.Trim().TrimEnd(';').Trim();
		if (string.IsNullOrWhiteSpace(trimmed))
			throw new InvalidOperationException("Empty DDL statement.");

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_database
		//   "ALTER DATABASE db SET OPTIONS (...)" — accepted as a no-op since database-level
		//   options (optimizer_version, version_retention_period, etc.) have no effect in-memory.
		if (trimmed.StartsWith("ALTER DATABASE", StringComparison.OrdinalIgnoreCase))
			return;

		// Clear proto FQN placeholder mapping from previous DDL calls
		ProtoFqnPlaceholders.Clear();

		// Handle CREATE VIEW / CREATE OR REPLACE VIEW with string parsing
		// (view body is arbitrary SQL that's hard to parse with the DDL tokenizer)
		if (TryParseView(trimmed, schema)) return;

		// Handle CREATE SEQUENCE / DROP SEQUENCE with string parsing
		if (TryParseSequence(trimmed, schema)) return;

		// Handle CREATE SEARCH INDEX / DROP SEARCH INDEX with string matching.
		// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
		//   Search indexes have many optional trailing clauses (PARTITION BY, ORDER BY,
		//   STORING, INTERLEAVE, OPTIONS, WHERE) that are complex to parse.
		//   The in-memory emulator accepts them as no-ops.
		if (TryParseSearchIndex(trimmed)) return;

		// Handle CREATE/ALTER/DROP CHANGE STREAM with string parsing.
		// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
		if (TryParseChangeStream(trimmed, schema)) return;

		// Handle CREATE/DROP PROPERTY GRAPH as no-ops.
		// Ref: https://cloud.google.com/spanner/docs/graph/schema-overview
		if (TryParsePropertyGraph(trimmed)) return;

		// Handle CREATE/ALTER/DROP PROTO BUNDLE with string parsing.
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#proto_bundle_statements
		if (TryParseProtoBundle(trimmed, schema)) return;

		// Handle proto/enum column types — DDL with dotted FQN type names
		// must be pre-processed before tokenization since the tokenizer splits dots.
		var preprocessed = PreprocessProtoBundleColumnTypes(trimmed, schema);

		var tokens = GoogleSqlTokenizer.Tokenize(preprocessed);
		var result = DdlParsers.DdlStatement.AtEnd().TryParse(tokens);

		if (!result.HasValue)
		{
			throw new InvalidOperationException($"Failed to parse DDL: {result.ErrorMessage} at position {result.ErrorPosition}. SQL: {ddl}");
		}

		switch (result.Value)
		{
			case CreateTableStatement create:
				ApplyCreateTable(create, schema);
				break;
			case DropTableStatement drop:
				ApplyDropTable(drop, schema);
				break;
			case AlterTableStatement alter:
				ApplyAlterTable(alter, schema);
				break;
			case CreateIndexStatement createIndex:
				ApplyCreateIndex(createIndex, schema);
				break;
			case DropIndexStatement dropIndex:
				ApplyDropIndex(dropIndex, schema);
				break;
			case CreateViewStatement createView:
				schema.AddView(new ViewDefinition(createView.Name, createView.SqlBody));
				break;
			case DropViewStatement dropView:
				schema.RemoveView(dropView.Name);
				break;
			case CreateSequenceStatement createSeq:
				schema.AddSequence(new SequenceDefinition(createSeq.Name, createSeq.SequenceKind, createSeq.StartWithCounter ?? 1));
				break;
			case DropSequenceStatement dropSeq:
				schema.RemoveSequence(dropSeq.Name);
				break;
			default:
				throw new InvalidOperationException($"Unknown DDL statement type: {result.Value.GetType().Name}");
		}
	}

	private static void ApplyCreateTable(CreateTableStatement stmt, SchemaRegistry schema)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
		//   "IF NOT EXISTS" — silently succeed if the table already exists
		if (stmt.IfNotExists && schema.TryGetTable(stmt.Name, out _))
			return;

		var columns = stmt.Columns.Select(c => new ColumnDef(
			c.Name,
			c.SpannerType,
			c.IsNullable,
			c.MaxLength,
			c.AllowCommitTimestamp,
			c.ArrayElementType,
			c.GeneratedExpression,
			c.IsStored,
			c.DefaultExpression,
			c.IsHidden,
			c.ProtoTypeFqn
		)).ToList();

		var pkColumnNames = stmt.PrimaryKey.Select(pk => pk.ColumnName).ToList();

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
		//   "All primary key columns must be defined in the column definition list."
		foreach (var pk in pkColumnNames)
		{
			if (!columns.Any(c => string.Equals(c.Name, pk, StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidOperationException($"Primary key column '{pk}' is not defined in the table '{stmt.Name}'.");
			}
		}

		var table = new TableDefinition(
			stmt.Name,
			columns.AsReadOnly(),
			pkColumnNames.AsReadOnly(),
			stmt.ParentTable,
			stmt.OnDelete ?? OnDeleteAction.NoAction);

		// Store row deletion policy if specified
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
		if (stmt.RowDeletionPolicy != null)
		{
			table.RowDeletionPolicy = new RowDeletionPolicy(
				stmt.RowDeletionPolicy.Column, stmt.RowDeletionPolicy.IntervalDays);
		}

		// Add CHECK constraints
		if (stmt.CheckConstraints != null)
		{
			foreach (var check in stmt.CheckConstraints)
			{
				table.CheckConstraints.Add(new CheckConstraint(check.Name, check.Expression));
			}
		}

		// Add FOREIGN KEY constraints
		if (stmt.ForeignKeys != null)
		{
			foreach (var fk in stmt.ForeignKeys)
			{
				table.ForeignKeys.Add(new ForeignKeyConstraint(
					fk.Name, fk.Columns, fk.ReferencedTable, fk.ReferencedColumns, fk.IsEnforced, fk.OnDelete));
			}
		}

		schema.AddTable(table);
	}

	private static void ApplyDropTable(DropTableStatement stmt, SchemaRegistry schema)
	{
		if (!schema.TryGetTable(stmt.Name, out _))
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#drop_table
			//   "IF EXISTS" — silently succeed if the table does not exist
			if (stmt.IfExists)
				return;
			throw new InvalidOperationException($"Table '{stmt.Name}' does not exist.");
		}
		schema.RemoveTable(stmt.Name);
	}

	private static void ApplyAlterTable(AlterTableStatement stmt, SchemaRegistry schema)
	{
		if (!schema.TryGetTable(stmt.Name, out var table) || table == null)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
			//   "ALTER TABLE IF EXISTS — no error if the table does not exist."
			if (stmt.IfExists) return;
			throw new InvalidOperationException($"Table '{stmt.Name}' does not exist.");
		}

		switch (stmt.Action)
		{
			case AddColumnAction add:
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
				//   "You cannot add NOT NULL columns to existing tables."
				if (table.Columns.Any(c => string.Equals(c.Name, add.Column.Name, StringComparison.OrdinalIgnoreCase)))
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
					//   "ADD COLUMN IF NOT EXISTS — no error if the column already exists."
					if (add.IfNotExists) return;
					throw new InvalidOperationException($"Column '{add.Column.Name}' already exists in table '{stmt.Name}'.");
				}

				var newCol = new ColumnDef(
					add.Column.Name,
					add.Column.SpannerType,
					add.Column.IsNullable,
					add.Column.MaxLength,
					add.Column.AllowCommitTimestamp,
					add.Column.ArrayElementType,
					add.Column.GeneratedExpression,
					add.Column.IsStored,
					add.Column.DefaultExpression,
					add.Column.IsHidden,
					add.Column.ProtoTypeFqn);

				var newColumns = table.Columns.ToList();
				newColumns.Add(newCol);

				// Replace the table definition with updated columns
				schema.RemoveTable(stmt.Name);
				var newTable = new TableDefinition(
					table.Name,
					newColumns.AsReadOnly(),
					table.PrimaryKeyColumns,
					table.ParentTable,
					table.OnDeleteAction);

				// Copy existing rows
				foreach (var kvp in table.Rows)
				{
					newTable.Rows[kvp.Key] = kvp.Value;
				}

				schema.AddTable(newTable);
				break;
			}

			case DropColumnAction drop:
			{
				if (!table.Columns.Any(c => string.Equals(c.Name, drop.ColumnName, StringComparison.OrdinalIgnoreCase)))
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
					//   "DROP COLUMN IF EXISTS — no error if the column does not exist."
					if (drop.IfExists) return;
					throw new InvalidOperationException($"Column '{drop.ColumnName}' does not exist in table '{stmt.Name}'.");
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
				//   "You cannot drop columns used in primary key."
				if (table.PrimaryKeyColumns.Any(pk => string.Equals(pk, drop.ColumnName, StringComparison.OrdinalIgnoreCase)))
				{
					throw new InvalidOperationException($"Cannot drop primary key column '{drop.ColumnName}' from table '{stmt.Name}'.");
				}

				var newColumns = table.Columns
					.Where(c => !string.Equals(c.Name, drop.ColumnName, StringComparison.OrdinalIgnoreCase))
					.ToList();

				schema.RemoveTable(stmt.Name);
				var newTable = new TableDefinition(
					table.Name,
					newColumns.AsReadOnly(),
					table.PrimaryKeyColumns,
					table.ParentTable,
					table.OnDeleteAction);

				foreach (var kvp in table.Rows)
				{
					// Remove dropped column from row data
					kvp.Value.Columns.Remove(drop.ColumnName);
					newTable.Rows[kvp.Key] = kvp.Value;
				}

				schema.AddTable(newTable);
				break;
			}

			case AlterColumnSetOptionsAction setOptions:
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
				//   ALTER TABLE t ALTER COLUMN c SET OPTIONS (allow_commit_timestamp = true|false)
				var optCol = table.Columns.FirstOrDefault(
					c => string.Equals(c.Name, setOptions.ColumnName, StringComparison.OrdinalIgnoreCase));
				if (optCol == null)
					throw new InvalidOperationException($"Column '{setOptions.ColumnName}' does not exist in table '{stmt.Name}'.");

				var updatedCol = new ColumnDef(
					optCol.Name,
					optCol.SpannerType,
					optCol.IsNullable,
					optCol.MaxLength,
					setOptions.AllowCommitTimestamp,
					optCol.ArrayElementType,
					optCol.GeneratedExpression,
					optCol.IsStored,
					optCol.DefaultExpression,
					optCol.IsHidden,
					optCol.ProtoTypeFqn);

				var newColumns = table.Columns.Select(c =>
					string.Equals(c.Name, setOptions.ColumnName, StringComparison.OrdinalIgnoreCase) ? updatedCol : c)
					.ToList();

				RebuildTable(table, newColumns, schema);
				break;
			}

			case AlterColumnAction alter:
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
				var existingCol = table.Columns.FirstOrDefault(
					c => string.Equals(c.Name, alter.ColumnName, StringComparison.OrdinalIgnoreCase));
				if (existingCol == null)
					throw new InvalidOperationException($"Column '{alter.ColumnName}' does not exist in table '{stmt.Name}'.");

				var newCol = new ColumnDef(
					existingCol.Name,
					alter.NewDefinition.SpannerType,
					alter.NewDefinition.IsNullable,
					alter.NewDefinition.MaxLength,
					alter.NewDefinition.AllowCommitTimestamp,
					alter.NewDefinition.ArrayElementType,
					alter.NewDefinition.GeneratedExpression ?? existingCol.GeneratedExpression,
					alter.NewDefinition.IsStored || existingCol.IsStored,
					alter.NewDefinition.DefaultExpression ?? existingCol.DefaultExpression,
					alter.NewDefinition.IsHidden || existingCol.IsHidden,
					alter.NewDefinition.ProtoTypeFqn ?? existingCol.ProtoTypeFqn);

				var newColumns = table.Columns.Select(c =>
					string.Equals(c.Name, alter.ColumnName, StringComparison.OrdinalIgnoreCase) ? newCol : c)
					.ToList();

				RebuildTable(table, newColumns, schema);
				break;
			}

			case AddConstraintAction addConstraint:
			{
				if (addConstraint.Check != null)
				{
					table.CheckConstraints.Add(
						new CheckConstraint(addConstraint.Check.Name, addConstraint.Check.Expression));
				}
				if (addConstraint.ForeignKey != null)
				{
					var fk = addConstraint.ForeignKey;
					table.ForeignKeys.Add(
						new ForeignKeyConstraint(fk.Name, fk.Columns, fk.ReferencedTable, fk.ReferencedColumns, fk.IsEnforced, fk.OnDelete));
				}
				break;
			}

			case DropConstraintAction dropConstraint:
			{
				var removedCheck = table.CheckConstraints.RemoveAll(
					c => string.Equals(c.Name, dropConstraint.ConstraintName, StringComparison.OrdinalIgnoreCase));
				var removedFk = table.ForeignKeys.RemoveAll(
					f => string.Equals(f.Name, dropConstraint.ConstraintName, StringComparison.OrdinalIgnoreCase));
				if (removedCheck == 0 && removedFk == 0)
					throw new InvalidOperationException(
						$"Constraint '{dropConstraint.ConstraintName}' does not exist in table '{stmt.Name}'.");
				break;
			}

			case SetOnDeleteAction setOnDelete:
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
				//   "SET ON DELETE CASCADE | NO ACTION"
				RebuildTable(table, table.Columns.ToList(), schema, setOnDelete.OnDelete);
				break;
			}

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
			//   Row deletion policies — stored on the table and enforced at runtime.
			case AddRowDeletionPolicyAction addPolicy:
				table.RowDeletionPolicy = new RowDeletionPolicy(addPolicy.Policy.Column, addPolicy.Policy.IntervalDays);
				break;
			case ReplaceRowDeletionPolicyAction replacePolicy:
				table.RowDeletionPolicy = new RowDeletionPolicy(replacePolicy.Policy.Column, replacePolicy.Policy.IntervalDays);
				break;
			case DropRowDeletionPolicyAction:
				table.RowDeletionPolicy = null;
				break;

			default:
				throw new InvalidOperationException($"Unknown alter action: {stmt.Action.GetType().Name}");
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
	private static TableDefinition RebuildTable(
		TableDefinition table, IReadOnlyList<ColumnDef> newColumns, SchemaRegistry schema,
		OnDeleteAction? onDelete = null)
	{
		schema.RemoveTable(table.Name);
		var newTable = new TableDefinition(
			table.Name,
			newColumns.ToList().AsReadOnly(),
			table.PrimaryKeyColumns,
			table.ParentTable,
			onDelete ?? table.OnDeleteAction);

		foreach (var kvp in table.Rows)
			newTable.Rows[kvp.Key] = kvp.Value;

		// Preserve CHECK and FK constraints
		foreach (var check in table.CheckConstraints)
			newTable.CheckConstraints.Add(check);
		foreach (var fk in table.ForeignKeys)
			newTable.ForeignKeys.Add(fk);

		// Preserve row deletion policy
		newTable.RowDeletionPolicy = table.RowDeletionPolicy;

		schema.AddTable(newTable);
		return newTable;
	}

	private static void ApplyCreateIndex(CreateIndexStatement stmt, SchemaRegistry schema)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
		//   "IF NOT EXISTS" — silently succeed if the index already exists
		if (stmt.IfNotExists && schema.TryGetIndex(stmt.Name, out _))
			return;

		if (!schema.TryGetTable(stmt.TableName, out var table) || table == null)
		{
			throw new InvalidOperationException($"Table '{stmt.TableName}' does not exist.");
		}

		// Validate index columns exist in the table
		foreach (var col in stmt.Columns)
		{
			if (!table.Columns.Any(c => string.Equals(c.Name, col.ColumnName, StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidOperationException($"Column '{col.ColumnName}' does not exist in table '{stmt.TableName}'.");
			}
		}

		var indexColumns = stmt.Columns
			.Select(c => new IndexColumn(c.ColumnName, c.Order))
			.ToList();

		var index = new IndexDefinition(
			stmt.Name,
			stmt.TableName,
			indexColumns.AsReadOnly(),
			stmt.StoringColumns?.AsReadOnly(),
			stmt.IsUnique,
			stmt.IsNullFiltered);

		schema.AddIndex(index);
	}

	private static void ApplyDropIndex(DropIndexStatement stmt, SchemaRegistry schema)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#drop_index
		//   "IF EXISTS" — silently succeed if the index does not exist
		if (stmt.IfExists && !schema.TryGetIndex(stmt.Name, out _))
			return;
		schema.RemoveIndex(stmt.Name);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_view
	private static bool TryParseView(string ddl, SchemaRegistry schema)
	{
		var upper = ddl.ToUpperInvariant();

		if (upper.StartsWith("DROP VIEW"))
		{
			var name = ddl["DROP VIEW".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
			schema.RemoveView(name);
			return true;
		}

		bool orReplace = false;
		string rest = ddl;

		if (upper.StartsWith("CREATE OR REPLACE VIEW"))
		{
			orReplace = true;
			rest = ddl["CREATE OR REPLACE VIEW".Length..].Trim();
		}
		else if (upper.StartsWith("CREATE VIEW"))
		{
			rest = ddl["CREATE VIEW".Length..].Trim();
		}
		else
		{
			return false;
		}

		// Name is the first token
		var spaceIdx = rest.IndexOf(' ');
		if (spaceIdx < 0) throw new InvalidOperationException("Invalid CREATE VIEW: missing SQL body.");
		var viewName = rest[..spaceIdx];
		rest = rest[(spaceIdx + 1)..].Trim();

		// Optional: SQL SECURITY INVOKER
		var restUpper = rest.ToUpperInvariant();
		if (restUpper.StartsWith("SQL SECURITY INVOKER"))
			rest = rest["SQL SECURITY INVOKER".Length..].Trim();

		// Must have AS
		restUpper = rest.ToUpperInvariant();
		if (!restUpper.StartsWith("AS "))
			throw new InvalidOperationException("Invalid CREATE VIEW: missing AS keyword.");
		var sqlBody = rest[3..].Trim();

		if (!orReplace && schema.TryGetView(viewName, out _))
			throw new InvalidOperationException($"View '{viewName}' already exists.");

		schema.AddView(new ViewDefinition(viewName, sqlBody));
		return true;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-sequence
	private static bool TryParseSequence(string ddl, SchemaRegistry schema)
	{
		var upper = ddl.ToUpperInvariant();

		if (upper.StartsWith("DROP SEQUENCE"))
		{
			var name = ddl["DROP SEQUENCE".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
			schema.RemoveSequence(name);
			return true;
		}

		if (!upper.StartsWith("CREATE SEQUENCE")) return false;

		var rest = ddl["CREATE SEQUENCE".Length..].Trim();
		var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var seqName = parts[0];

		// Parse OPTIONS (sequence_kind='bit_reversed_positive', start_with_counter=N)
		var optionsMatch = System.Text.RegularExpressions.Regex.Match(rest,
			@"OPTIONS\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		string sequenceKind = "bit_reversed_positive";
		long? startWithCounter = null;

		if (optionsMatch.Success)
		{
			var optionsStr = optionsMatch.Groups[1].Value;
			var kindMatch = System.Text.RegularExpressions.Regex.Match(optionsStr,
				@"sequence_kind\s*=\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (kindMatch.Success) sequenceKind = kindMatch.Groups[1].Value;

			var startMatch = System.Text.RegularExpressions.Regex.Match(optionsStr,
				@"start_with_counter\s*=\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (startMatch.Success) startWithCounter = long.Parse(startMatch.Groups[1].Value);
		}

		schema.AddSequence(new SequenceDefinition(seqName, sequenceKind, startWithCounter ?? 1));
		return true;
	}

	// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
	//   CREATE SEARCH INDEX / DROP SEARCH INDEX — accepted as no-ops.
	//   The in-memory emulator does not maintain inverted indexes.
	private static bool TryParseSearchIndex(string ddl)
	{
		var upper = ddl.ToUpperInvariant();
		if (upper.StartsWith("CREATE SEARCH INDEX") || upper.StartsWith("DROP SEARCH INDEX"))
			return true;
		return false;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#proto_bundle_statements
	//   CREATE PROTO BUNDLE (type1, type2, ...)
	//   ALTER PROTO BUNDLE [INSERT (...)] [UPDATE (...)] [DELETE (...)]
	//   DROP PROTO BUNDLE
	private static bool TryParseProtoBundle(string ddl, SchemaRegistry schema)
	{
		var upper = ddl.ToUpperInvariant();

		if (upper.StartsWith("DROP PROTO BUNDLE"))
		{
			schema.DropProtoBundle();
			return true;
		}

		if (upper.StartsWith("CREATE PROTO BUNDLE"))
		{
			var rest = ddl["CREATE PROTO BUNDLE".Length..].Trim();
			var types = ParseParenthesizedTypeList(rest);
			schema.SetProtoBundleTypes(types);
			return true;
		}

		if (upper.StartsWith("ALTER PROTO BUNDLE"))
		{
			var rest = ddl["ALTER PROTO BUNDLE".Length..].Trim();
			ParseAlterProtoBundle(rest, schema);
			return true;
		}

		return false;
	}

	/// <summary>Parses a parenthesized, comma-separated list of dotted type names.</summary>
	private static List<string> ParseParenthesizedTypeList(string input)
	{
		input = input.Trim();
		if (input.StartsWith('(') && input.EndsWith(')'))
			input = input[1..^1];
		return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(t => t.Trim('`'))
			.ToList();
	}

	/// <summary>
	/// Parses ALTER PROTO BUNDLE clauses: INSERT (...), UPDATE (...), DELETE (...).
	/// All three are optional and execute atomically.
	/// </summary>
	private static void ParseAlterProtoBundle(string rest, SchemaRegistry schema)
	{
		var upper = rest.ToUpperInvariant();
		int pos = 0;

		while (pos < upper.Length)
		{
			// Skip whitespace
			while (pos < upper.Length && char.IsWhiteSpace(upper[pos])) pos++;
			if (pos >= upper.Length) break;

			string? action = null;
			foreach (var keyword in new[] { "INSERT", "UPDATE", "DELETE" })
			{
				if (upper[pos..].StartsWith(keyword))
				{
					action = keyword;
					pos += keyword.Length;
					break;
				}
			}

			if (action == null) break;

			// Skip whitespace to opening paren
			while (pos < upper.Length && char.IsWhiteSpace(upper[pos])) pos++;
			if (pos >= upper.Length || rest[pos] != '(') break;

			// Find matching closing paren (using original case for type names)
			var parenStart = pos;
			int depth = 0;
			int parenEnd = pos;
			for (int i = pos; i < rest.Length; i++)
			{
				if (rest[i] == '(') depth++;
				else if (rest[i] == ')')
				{
					depth--;
					if (depth == 0) { parenEnd = i; break; }
				}
			}

			var typeListStr = rest[(parenStart)..(parenEnd + 1)];
			var types = ParseParenthesizedTypeList(typeListStr);
			pos = parenEnd + 1;

			switch (action)
			{
				case "INSERT":
					schema.InsertProtoBundleTypes(types);
					break;
				case "UPDATE":
					// UPDATE refreshes proto type info — for the emulator this is a no-op
					// since we don't hold actual proto file descriptors.
					break;
				case "DELETE":
					schema.DeleteProtoBundleTypes(types);
					break;
			}
		}
	}

	/// <summary>
	/// Pre-processes DDL to replace dotted proto/enum type names with a placeholder
	/// token that the DDL tokenizer can handle. Dotted names like "examples.music.SingerInfo"
	/// become "__PROTO_FQN_0__" (sequentially numbered) before tokenization.
	/// The mapping from placeholder to original FQN is stored for later decoding.
	/// </summary>
	private static string PreprocessProtoBundleColumnTypes(string ddl, SchemaRegistry schema)
	{
		if (!schema.HasProtoBundle) return ddl;

		var bundleTypes = schema.GetProtoBundleTypes();
		var result = ddl;
		// Replace longer FQNs first to avoid partial matches
		int index = 0;
		foreach (var fqn in bundleTypes.OrderByDescending(t => t.Length))
		{
			if (result.Contains(fqn))
			{
				var placeholder = $"__PROTO_FQN_{index}__";
				ProtoFqnPlaceholders[placeholder] = fqn;
				result = result.Replace(fqn, placeholder);
				index++;
			}
		}
		return result;
	}

	// Thread-safe mapping from placeholder identifiers back to original proto FQNs.
	// Populated by PreprocessProtoBundleColumnTypes, consumed by DecodeProtoFqnPlaceholder.
	[ThreadStatic] private static Dictionary<string, string>? _protoFqnPlaceholders;
	private static Dictionary<string, string> ProtoFqnPlaceholders =>
		_protoFqnPlaceholders ??= new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Reverses the FQN placeholder encoding back to the original dotted name.</summary>
	internal static string? DecodeProtoFqnPlaceholder(string? identifier)
	{
		if (identifier == null || !identifier.StartsWith("__PROTO_FQN_") || !identifier.EndsWith("__"))
			return null;
		return ProtoFqnPlaceholders.TryGetValue(identifier, out var fqn) ? fqn : null;
	}

	// Ref: https://cloud.google.com/spanner/docs/graph/schema-overview
	//   CREATE [OR REPLACE] PROPERTY GRAPH / DROP PROPERTY GRAPH — accepted as no-ops.
	//   The in-memory emulator does not implement graph query semantics.
	private static bool TryParsePropertyGraph(string ddl)
	{
		var upper = ddl.ToUpperInvariant();
		if (upper.StartsWith("CREATE PROPERTY GRAPH") ||
			upper.StartsWith("CREATE OR REPLACE PROPERTY GRAPH") ||
			upper.StartsWith("DROP PROPERTY GRAPH"))
			return true;
		return false;
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
	//   CREATE CHANGE STREAM name [FOR ...] [OPTIONS (...)];
	//   ALTER CHANGE STREAM name SET/DROP FOR ...;
	//   DROP CHANGE STREAM name;
	private static bool TryParseChangeStream(string ddl, SchemaRegistry schema)
	{
		var upper = ddl.ToUpperInvariant();

		if (upper.StartsWith("DROP CHANGE STREAM"))
		{
			var name = ddl["DROP CHANGE STREAM".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
			schema.RemoveChangeStream(name);
			return true;
		}

		if (upper.StartsWith("ALTER CHANGE STREAM"))
		{
			// ALTER CHANGE STREAM name SET FOR ...|DROP FOR ALL|SET OPTIONS (...)
			var rest = ddl["ALTER CHANGE STREAM".Length..].Trim();
			var nameParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
			if (nameParts.Length == 0) return false;
			var name = nameParts[0];

			if (!schema.TryGetChangeStream(name, out var existing) || existing == null)
				existing = new ChangeStreamDefinition(name);

			if (nameParts.Length > 1)
			{
				var clause = nameParts[1].Trim();
				var clauseUpper = clause.ToUpperInvariant();

				if (clauseUpper.StartsWith("DROP FOR ALL"))
				{
					// Suspend: watches nothing
					existing.WatchesAll = false;
					existing.WatchedTables.Clear();
				}
				else if (clauseUpper.StartsWith("SET FOR"))
				{
					var forClause = clause["SET FOR".Length..].Trim();
					ParseForClause(forClause, existing);
				}
				else if (clauseUpper.StartsWith("SET OPTIONS"))
				{
					ParseOptions(clause, existing);
				}
			}

			schema.AddChangeStream(existing);
			return true;
		}

		if (!upper.StartsWith("CREATE CHANGE STREAM")) return false;

		var createRest = ddl["CREATE CHANGE STREAM".Length..].Trim();
		var createParts = createRest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		if (createParts.Length == 0) return false;

		var csName = createParts[0];
		var csDef = new ChangeStreamDefinition(csName);

		if (createParts.Length > 1)
		{
			var body = createParts[1].Trim();
			var bodyUpper = body.ToUpperInvariant();

			// Split FOR clause from OPTIONS
			var optionsIdx = bodyUpper.IndexOf("OPTIONS", StringComparison.Ordinal);
			string? forPart = null;
			string? optionsPart = null;

			if (bodyUpper.StartsWith("FOR"))
			{
				if (optionsIdx >= 0)
				{
					forPart = body[..optionsIdx].Trim();
					optionsPart = body[optionsIdx..];
				}
				else
				{
					forPart = body;
				}
			}
			else if (bodyUpper.StartsWith("OPTIONS"))
			{
				optionsPart = body;
			}

			if (forPart != null)
				ParseForClause(forPart["FOR".Length..].Trim(), csDef);

			if (optionsPart != null)
				ParseOptions(optionsPart, csDef);
		}

		schema.AddChangeStream(csDef);
		return true;
	}

	private static void ParseForClause(string forBody, ChangeStreamDefinition def)
	{
		var upper = forBody.Trim().ToUpperInvariant();
		if (upper == "ALL" || upper.StartsWith("ALL "))
		{
			def.WatchesAll = true;
			def.WatchedTables.Clear();
			return;
		}

		def.WatchesAll = false;
		def.WatchedTables.Clear();

		// Parse comma-separated table specs: Table1, Table2(Col1, Col2), Table3
		// Use a simple state machine to handle parentheses
		var specs = SplitTopLevel(forBody, ',');
		foreach (var spec in specs)
		{
			var trimSpec = spec.Trim();
			var parenIdx = trimSpec.IndexOf('(');
			if (parenIdx >= 0)
			{
				var table = trimSpec[..parenIdx].Trim();
				var colList = trimSpec[(parenIdx + 1)..trimSpec.LastIndexOf(')')].Trim();
				var cols = colList.Split(',').Select(c => c.Trim()).ToList();
				def.WatchedTables.Add((table, cols));
			}
			else
			{
				def.WatchedTables.Add((trimSpec, null));
			}
		}
	}

	private static void ParseOptions(string optionsClause, ChangeStreamDefinition def)
	{
		var match = System.Text.RegularExpressions.Regex.Match(optionsClause,
			@"OPTIONS\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (!match.Success) return;

		var optionsStr = match.Groups[1].Value;
		// Parse key = 'value' or key = true/false pairs
		var pairs = System.Text.RegularExpressions.Regex.Matches(optionsStr,
			@"(\w+)\s*=\s*(?:'([^']*)'|(true|false|\d+))",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		foreach (System.Text.RegularExpressions.Match pair in pairs)
		{
			var key = pair.Groups[1].Value;
			var val = pair.Groups[2].Success ? pair.Groups[2].Value : pair.Groups[3].Value;
			def.Options[key] = val;
		}
	}

	/// <summary>
	/// Splits a string by a delimiter, but only at the top level (not inside parentheses).
	/// </summary>
	private static List<string> SplitTopLevel(string input, char delimiter)
	{
		var result = new List<string>();
		var depth = 0;
		var start = 0;
		for (var i = 0; i < input.Length; i++)
		{
			if (input[i] == '(') depth++;
			else if (input[i] == ')') depth--;
			else if (input[i] == delimiter && depth == 0)
			{
				result.Add(input[start..i]);
				start = i + 1;
			}
		}
		result.Add(input[start..]);
		return result;
	}
}

