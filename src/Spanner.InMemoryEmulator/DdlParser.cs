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

		// Handle CREATE VIEW / CREATE OR REPLACE VIEW with string parsing
		// (view body is arbitrary SQL that's hard to parse with the DDL tokenizer)
		if (TryParseView(trimmed, schema)) return;

		// Handle CREATE SEQUENCE / DROP SEQUENCE with string parsing
		if (TryParseSequence(trimmed, schema)) return;

		var tokens = GoogleSqlTokenizer.Tokenize(trimmed);
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
		var columns = stmt.Columns.Select(c => new ColumnDef(
			c.Name,
			c.SpannerType,
			c.IsNullable,
			c.MaxLength,
			c.AllowCommitTimestamp,
			c.ArrayElementType,
			c.GeneratedExpression,
			c.IsStored,
			c.DefaultExpression
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
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1
			//   Drop of non-existent table returns NOT_FOUND.
			throw new InvalidOperationException($"Table '{stmt.Name}' does not exist.");
		}
		schema.RemoveTable(stmt.Name);
	}

	private static void ApplyAlterTable(AlterTableStatement stmt, SchemaRegistry schema)
	{
		if (!schema.TryGetTable(stmt.Name, out var table) || table == null)
		{
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
					add.Column.DefaultExpression);

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

			default:
				throw new InvalidOperationException($"Unknown alter action: {stmt.Action.GetType().Name}");
		}
	}

	private static void ApplyCreateIndex(CreateIndexStatement stmt, SchemaRegistry schema)
	{
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
}

