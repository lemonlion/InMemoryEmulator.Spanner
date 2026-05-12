using Google.Cloud.Spanner.V1;
using Superpower;
using Superpower.Parsers;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace InMemoryEmulator.Spanner.Parsing;

/// <summary>
/// Parses GoogleSQL DDL statements (CREATE TABLE, DROP TABLE, ALTER TABLE, CREATE INDEX, DROP INDEX).
/// </summary>
internal static class DdlParsers
{
	// Helper record to avoid AsNullable on value tuples
	private record InterleaveInfo(string Parent, OnDeleteAction OnDelete);

	// IF NOT EXISTS / IF EXISTS helpers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
	//   "IF NOT EXISTS" — skip CREATE if object already exists
	//   "IF EXISTS" — skip DROP if object does not exist
	private static TokenListParser<GoogleSqlToken, bool> IfNotExists { get; } =
		(from _if in Token.EqualTo(GoogleSqlToken.If)
		 from _not in Token.EqualTo(GoogleSqlToken.Not)
		 from _ex in Token.EqualTo(GoogleSqlToken.Exists)
		 select true)
		.OptionalOrDefault(false);

	private static TokenListParser<GoogleSqlToken, bool> IfExists { get; } =
		(from _if in Token.EqualTo(GoogleSqlToken.If)
		 from _ex in Token.EqualTo(GoogleSqlToken.Exists)
		 select true)
		.OptionalOrDefault(false);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
	//   Defines the DDL syntax for Spanner.

	// ──────────────────────────────────────────
	// Utilities
	// ──────────────────────────────────────────

	private static TokenListParser<GoogleSqlToken, string> IdentifierOrKeywordAsName { get; } =
		Token.EqualTo(GoogleSqlToken.Identifier).Select(t => t.ToStringValue())
		.Or(Token.EqualTo(GoogleSqlToken.QuotedIdentifier).Select(t => t.ToStringValue().Trim('`')))
		// Allow certain keywords to be used as identifiers (common in Spanner schemas)
		.Or(Token.EqualTo(GoogleSqlToken.Key).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Values).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Set).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Action).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Options).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Default).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.View).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Index).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Column).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.DateType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.TimestampType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.JsonType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Parent).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Replace).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Left).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Right).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Avg).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Sum).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Min).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Max).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Row).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Day).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Percent).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Hidden).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Search).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Tokenlist).Select(t => t.ToStringValue()))
		.Named("identifier");

	// ──────────────────────────────────────────
	// Type Parsing
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types
	//   Type syntax: INT64, FLOAT64, BOOL, STRING(n|MAX), BYTES(n|MAX), TIMESTAMP, DATE, NUMERIC, JSON

	private static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength)> StringTypeWithLength { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.StringType)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from len in Token.EqualTo(GoogleSqlToken.Max).Value((long?)-1)
			.Or(Token.EqualTo(GoogleSqlToken.Number).Select(t => (long?)long.Parse(t.ToStringValue())))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (TypeCode.String, len == -1 ? (long?)null : len);

	private static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength)> BytesTypeWithLength { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.BytesType)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from len in Token.EqualTo(GoogleSqlToken.Max).Value((long?)-1)
			.Or(Token.EqualTo(GoogleSqlToken.Number).Select(t => (long?)long.Parse(t.ToStringValue())))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (TypeCode.Bytes, len == -1 ? (long?)null : len);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#protocol_buffers
	//   Proto/Enum column types use __PROTO_FQN_N__ placeholders injected by DdlParser.PreprocessProtoBundleColumnTypes.
	private static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength, TypeCode? ArrayElement, string? ProtoTypeFqn)> ProtoFqnType { get; } =
		Token.EqualTo(GoogleSqlToken.Identifier)
			.Where(t => t.ToStringValue().StartsWith("__PROTO_FQN_"))
			.Select(t =>
			{
				var fqn = DdlParser.DecodeProtoFqnPlaceholder(t.ToStringValue());
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
				//   TypeCode 13 = PROTO. Without actual proto descriptors, all FQN types default to PROTO.
				return ((TypeCode)13, (long?)null, (TypeCode?)null, (string?)fqn);
			});

	private static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength, TypeCode? ArrayElement, string? ProtoTypeFqn)> SimpleType { get; } =
		Token.EqualTo(GoogleSqlToken.Int64Type).Value((TypeCode.Int64, (long?)null, (TypeCode?)null, (string?)null))
		.Or(Token.EqualTo(GoogleSqlToken.Float64Type).Value((TypeCode.Float64, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.Float32Type).Value((TypeCode.Float32, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.BoolType).Value((TypeCode.Bool, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.TimestampType).Value((TypeCode.Timestamp, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.DateType).Value((TypeCode.Date, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.NumericType).Value((TypeCode.Numeric, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(Token.EqualTo(GoogleSqlToken.JsonType).Value((TypeCode.Json, (long?)null, (TypeCode?)null, (string?)null)))
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
		//   "UUID is a universally unique identifier (RFC 9562)."
		.Or(Token.EqualTo(GoogleSqlToken.UuidType).Value(((TypeCode)17, (long?)null, (TypeCode?)null, (string?)null)))
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#tokenlist_type
		//   "TOKENLIST is a collection of tokens produced by one of the TOKENIZE_* functions."
		.Or(Token.EqualTo(GoogleSqlToken.Tokenlist).Value((TypeCode.Unspecified, (long?)null, (TypeCode?)null, (string?)null)))
		.Or(StringTypeWithLength.Select(t => (t.Type, t.MaxLength, (TypeCode?)null, (string?)null)))
		.Or(BytesTypeWithLength.Select(t => (t.Type, t.MaxLength, (TypeCode?)null, (string?)null)))
		.Or(ProtoFqnType);

	private static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength, TypeCode? ArrayElement, string? ProtoTypeFqn)> ArrayType { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Array)
		from open in Token.EqualTo(GoogleSqlToken.LessThan)
		from elementType in SimpleType
		from close in Token.EqualTo(GoogleSqlToken.GreaterThan)
		select (TypeCode.Array, (long?)null, (TypeCode?)elementType.Type, elementType.ProtoTypeFqn);

	public static TokenListParser<GoogleSqlToken, (TypeCode Type, long? MaxLength, TypeCode? ArrayElement, string? ProtoTypeFqn)> SpannerType { get; } =
		ArrayType.Or(SimpleType);

	// ──────────────────────────────────────────
	// Column Definition
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   column_def: column_name type_name [NOT NULL] [OPTIONS (...)] [DEFAULT (...)] [AS (...) STORED]

	private static TokenListParser<GoogleSqlToken, bool> NotNull { get; } =
		(from not in Token.EqualTo(GoogleSqlToken.Not)
		 from n in Token.EqualTo(GoogleSqlToken.Null)
		 select true)
		.OptionalOrDefault(false);

	private static TokenListParser<GoogleSqlToken, bool> AllowCommitTimestampOption { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Options)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from name in Token.EqualTo(GoogleSqlToken.Identifier) // allow_commit_timestamp
		from eq in Token.EqualTo(GoogleSqlToken.Equal)
		from val in Token.EqualTo(GoogleSqlToken.True).Value(true)
			.Or(Token.EqualTo(GoogleSqlToken.False).Value(false))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select val;

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   "DEFAULT (expression)" — default value for the column
#pragma warning disable CS8603 // LINQ query syntax over Superpower parsers produces nullable inference
	private static TokenListParser<GoogleSqlToken, string> DefaultExpressionClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Default)
		from expr in BalancedParenExpression
		select expr;

	// Helper for generated column parsing result
	private record GeneratedColumnInfo(string Expression, bool IsStored, bool IsHidden);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   "AS (expression) STORED" — generated column
	//   "AS (expression) STORED HIDDEN" — generated TOKENLIST column (HIDDEN is optional)
	// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
	//   TOKENLIST columns use: TOKENLIST AS (TOKENIZE_FULLTEXT(col)) HIDDEN
	private static TokenListParser<GoogleSqlToken, GeneratedColumnInfo> GeneratedColumnClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.As)
		from expr in BalancedParenExpression
		from stored in Token.EqualTo(GoogleSqlToken.Stored).Value(true).OptionalOrDefault(false)
		from hidden in Token.EqualTo(GoogleSqlToken.Hidden).Value(true).OptionalOrDefault(false)
		select new GeneratedColumnInfo(expr, stored, hidden);
#pragma warning restore CS8603

	public static TokenListParser<GoogleSqlToken, ParsedColumnDef> ColumnDefinition { get; } =
		from name in IdentifierOrKeywordAsName
		from type in SpannerType
		from notNull in NotNull
		from options in AllowCommitTimestampOption.OptionalOrDefault(false)
		from defaultExpr in DefaultExpressionClause.Select(x => (string?)x).OptionalOrDefault()
		from generated in GeneratedColumnClause.Select(x => (GeneratedColumnInfo?)x).OptionalOrDefault()
		select new ParsedColumnDef(
			name,
			type.Type,
			!notNull,
			type.MaxLength,
			type.ArrayElement,
			generated?.Expression, generated?.IsStored ?? false, defaultExpr,
			options,
			generated?.IsHidden ?? false,
			type.ProtoTypeFqn);

	// ──────────────────────────────────────────
	// PRIMARY KEY
	// ──────────────────────────────────────────

	private static TokenListParser<GoogleSqlToken, PrimaryKeyPart> PrimaryKeyColumn { get; } =
		from name in IdentifierOrKeywordAsName
		from order in Token.EqualTo(GoogleSqlToken.Asc).Value(SortOrder.Asc)
			.Or(Token.EqualTo(GoogleSqlToken.Desc).Value(SortOrder.Desc))
			.OptionalOrDefault(SortOrder.Asc)
		select new PrimaryKeyPart(name, order);

	private static TokenListParser<GoogleSqlToken, List<PrimaryKeyPart>> PrimaryKeyClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Primary)
		from __ in Token.EqualTo(GoogleSqlToken.Key)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from columns in PrimaryKeyColumn.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select columns.ToList();

	// ──────────────────────────────────────────
	// INTERLEAVE IN PARENT
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#interleave_in
	private static TokenListParser<GoogleSqlToken, InterleaveInfo> InterleaveClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Comma)
		from __ in Token.EqualTo(GoogleSqlToken.Interleave)
		from ___ in Token.EqualTo(GoogleSqlToken.In)
		from ____ in Token.EqualTo(GoogleSqlToken.Parent)
		from parent in IdentifierOrKeywordAsName
		from onDelete in OnDeleteClause!.OptionalOrDefault(OnDeleteAction.NoAction)
		select new InterleaveInfo(parent, onDelete);

	private static TokenListParser<GoogleSqlToken, OnDeleteAction> OnDeleteClause { get; } =
		from _on in Token.EqualTo(GoogleSqlToken.On)
		from del in Token.EqualTo(GoogleSqlToken.Delete)
		from action in Token.EqualTo(GoogleSqlToken.Cascade).Value(OnDeleteAction.Cascade)
			.Or((from no in Token.EqualTo(GoogleSqlToken.No)
				 from act in Token.EqualTo(GoogleSqlToken.Action)
				 select OnDeleteAction.NoAction))
		select action;

	// ──────────────────────────────────────────
	// Table Constraints (CHECK, FOREIGN KEY)
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
	//   CONSTRAINT name CHECK (expression)
	private static TokenListParser<GoogleSqlToken, string?> OptionalConstraintName { get; } =
		(from c in Token.EqualTo(GoogleSqlToken.Constraint)
		 from name in IdentifierOrKeywordAsName
		 select (string?)name)
		.OptionalOrDefault();

	// Parse a balanced parenthesized expression as raw text for CHECK constraints
	// Uses a custom parser to handle nested parentheses
	private static TokenListParser<GoogleSqlToken, string> BalancedParenExpression { get; } =
		input =>
		{
			if (input.IsAtEnd || !input.ConsumeToken().HasValue || input.ConsumeToken().Value.Kind != GoogleSqlToken.OpenParen)
				return Superpower.Model.TokenListParserResult.Empty<GoogleSqlToken, string>(input, "expected '('");

			var rest = input.ConsumeToken().Remainder;
			int depth = 1;
			var tokens = new List<string>();
			while (!rest.IsAtEnd && depth > 0)
			{
				var next = rest.ConsumeToken();
				if (!next.HasValue) break;
				if (next.Value.Kind == GoogleSqlToken.OpenParen) depth++;
				else if (next.Value.Kind == GoogleSqlToken.CloseParen) depth--;
				if (depth > 0) tokens.Add(next.Value.ToStringValue());
				rest = next.Remainder;
			}
			if (depth != 0)
				return Superpower.Model.TokenListParserResult.Empty<GoogleSqlToken, string>(input, "unmatched parenthesis");
			return Superpower.Model.TokenListParserResult.Value(string.Join(" ", tokens), input, rest);
		};

	private static TokenListParser<GoogleSqlToken, ParsedCheckConstraint> CheckConstraintParser { get; } =
		from name in OptionalConstraintName
		from _ in Token.EqualTo(GoogleSqlToken.Check)
		from expr in BalancedParenExpression
		select new ParsedCheckConstraint(name, expr);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	//   [CONSTRAINT name] FOREIGN KEY (col, ...) REFERENCES table (col, ...)
	private static TokenListParser<GoogleSqlToken, List<string>> ParenColumnList { get; } =
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from cols in IdentifierOrKeywordAsName.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select cols.ToList();

	private static TokenListParser<GoogleSqlToken, ForeignKeyDeleteAction> ForeignKeyOnDeleteClause { get; } =
		(from _on in Token.EqualTo(GoogleSqlToken.On)
		 from del in Token.EqualTo(GoogleSqlToken.Delete)
		 from action in Token.EqualTo(GoogleSqlToken.Cascade).Value(ForeignKeyDeleteAction.Cascade)
			.Or((from no in Token.EqualTo(GoogleSqlToken.No)
				 from act in Token.EqualTo(GoogleSqlToken.Action)
				 select ForeignKeyDeleteAction.NoAction))
		 select action)
		.OptionalOrDefault(ForeignKeyDeleteAction.NoAction);

	// NOT ENFORCED clause
	private static TokenListParser<GoogleSqlToken, bool> EnforcedClause { get; } =
		(from not in Token.EqualTo(GoogleSqlToken.Not)
		 from enforced in Token.EqualTo(GoogleSqlToken.Enforced)
		 select false)
		.OptionalOrDefault(true);

	private static TokenListParser<GoogleSqlToken, ParsedForeignKey> ForeignKeyConstraintParser { get; } =
		from name in OptionalConstraintName
		from fk in Token.EqualTo(GoogleSqlToken.Foreign)
		from _key in Token.EqualTo(GoogleSqlToken.Key)
		from cols in ParenColumnList
		from _ref in Token.EqualTo(GoogleSqlToken.References)
		from refTable in IdentifierOrKeywordAsName
		from refCols in ParenColumnList
		from onDelete in ForeignKeyOnDeleteClause
		from enforced in EnforcedClause
		select new ParsedForeignKey(name, cols, refTable, refCols, enforced, onDelete);

	// A table body item is either a column def, CHECK constraint, or FOREIGN KEY constraint
	private abstract record TableBodyItem;
	private record ColumnItem(ParsedColumnDef Column) : TableBodyItem;
	private record CheckItem(ParsedCheckConstraint Check) : TableBodyItem;
	private record ForeignKeyItem(ParsedForeignKey ForeignKey) : TableBodyItem;

	private static TokenListParser<GoogleSqlToken, TableBodyItem> TableBodyItemParser { get; } =
		CheckConstraintParser.Try().Select(c => (TableBodyItem)new CheckItem(c))
		.Or(ForeignKeyConstraintParser.Try().Select(fk => (TableBodyItem)new ForeignKeyItem(fk)))
		.Or(ColumnDefinition.Select(c => (TableBodyItem)new ColumnItem(c)));

	// ──────────────────────────────────────────
	// ROW DELETION POLICY
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
	//   ROW DELETION POLICY (OLDER_THAN(column, INTERVAL n DAY))
	//   Parsed but not enforced at runtime — DDL compatibility only.
	private static TokenListParser<GoogleSqlToken, RowDeletionPolicyDef> RowDeletionPolicyClause { get; } =
		from _comma in Token.EqualTo(GoogleSqlToken.Comma)
		from _row in Token.EqualTo(GoogleSqlToken.Row)
		from _deletion in Token.EqualTo(GoogleSqlToken.Deletion)
		from _policy in Token.EqualTo(GoogleSqlToken.Policy)
		from _open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from _olderThan in Token.EqualTo(GoogleSqlToken.OlderThan)
		from _open2 in Token.EqualTo(GoogleSqlToken.OpenParen)
		from column in IdentifierOrKeywordAsName
		from _comma2 in Token.EqualTo(GoogleSqlToken.Comma)
		from _interval in Token.EqualTo(GoogleSqlToken.Interval)
		from days in Token.EqualTo(GoogleSqlToken.Number).Apply(Numerics.IntegerInt64)
		from _day in Token.EqualTo(GoogleSqlToken.Day)
		from _close2 in Token.EqualTo(GoogleSqlToken.CloseParen)
		from _close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new RowDeletionPolicyDef(column, (int)days);

	// ──────────────────────────────────────────
	// CREATE TABLE
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	//   CREATE TABLE table_name ( column_def, ... ) PRIMARY KEY ( column, ... )
	//   [, INTERLEAVE IN PARENT parent_table [ON DELETE CASCADE | NO ACTION]]

	public static TokenListParser<GoogleSqlToken, CreateTableStatement> CreateTable { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Create)
		from __ in Token.EqualTo(GoogleSqlToken.Table)
		from ifNotExists in IfNotExists
		from name in IdentifierOrKeywordAsName
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from items in TableBodyItemParser.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from pk in PrimaryKeyClause
		from interleave in InterleaveClause.Try().Select(x => (InterleaveInfo?)x).OptionalOrDefault()
		from rowDeletionPolicy in RowDeletionPolicyClause.Try().Select(x => (RowDeletionPolicyDef?)x).OptionalOrDefault()
		select new CreateTableStatement(
			name,
			items.OfType<ColumnItem>().Select(c => c.Column).ToList(),
			pk,
			interleave?.Parent,
			interleave?.OnDelete,
			items.OfType<CheckItem>().Select(c => c.Check).ToList(),
			items.OfType<ForeignKeyItem>().Select(fk => fk.ForeignKey).ToList(),
			ifNotExists,
			rowDeletionPolicy);

	// ──────────────────────────────────────────
	// DROP TABLE
	// ──────────────────────────────────────────

	public static TokenListParser<GoogleSqlToken, DropTableStatement> DropTable { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Drop)
		from __ in Token.EqualTo(GoogleSqlToken.Table)
		from ifExists in IfExists
		from name in IdentifierOrKeywordAsName
		select new DropTableStatement(name, ifExists);

	// ──────────────────────────────────────────
	// ALTER TABLE
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   ALTER TABLE table_name ADD COLUMN [IF NOT EXISTS] column_def
	//   ALTER TABLE table_name DROP COLUMN [IF EXISTS] column_name

	private static TokenListParser<GoogleSqlToken, AlterAction> AddColumnAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Add)
		from __ in Token.EqualTo(GoogleSqlToken.Column)
		from ifNotExists in IfNotExists
		from col in ColumnDefinition
		select (AlterAction)new AddColumnAction(col, ifNotExists);

	private static TokenListParser<GoogleSqlToken, AlterAction> DropColumnAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Drop)
		from __ in Token.EqualTo(GoogleSqlToken.Column)
		from ifExists in IfExists
		from name in IdentifierOrKeywordAsName
		select (AlterAction)new DropColumnAction(name, ifExists);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
	//   ALTER TABLE t ALTER COLUMN c SET OPTIONS (allow_commit_timestamp = true|false)
	private static TokenListParser<GoogleSqlToken, AlterAction> AlterColumnSetOptionsAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Alter)
		from __ in Token.EqualTo(GoogleSqlToken.Column)
		from name in IdentifierOrKeywordAsName
		from _set in Token.EqualTo(GoogleSqlToken.Set)
		from val in AllowCommitTimestampOption
		select (AlterAction)new AlterColumnSetOptionsAction(name, val);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
	//   ALTER TABLE t ALTER COLUMN c type [NOT NULL] [DEFAULT (expr)]
	private static TokenListParser<GoogleSqlToken, AlterAction> AlterColumnAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Alter)
		from __ in Token.EqualTo(GoogleSqlToken.Column)
		from col in ColumnDefinition
		select (AlterAction)new AlterColumnAction(col.Name, col);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   ALTER TABLE t ADD CONSTRAINT name CHECK (...) / FOREIGN KEY ...
	private static TokenListParser<GoogleSqlToken, AlterAction> AddConstraintAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Add)
		from action in
			CheckConstraintParser.Try().Select(c => (AlterAction)new AddConstraintAction(c, null))
			.Or(ForeignKeyConstraintParser.Select(fk => (AlterAction)new AddConstraintAction(null, fk)))
		select action;

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   ALTER TABLE t DROP CONSTRAINT name
	private static TokenListParser<GoogleSqlToken, AlterAction> DropConstraintAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Drop)
		from __ in Token.EqualTo(GoogleSqlToken.Constraint)
		from name in IdentifierOrKeywordAsName
		select (AlterAction)new DropConstraintAction(name);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   ALTER TABLE t SET ON DELETE CASCADE | NO ACTION
	private static TokenListParser<GoogleSqlToken, AlterAction> SetOnDeleteActionParser { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Set)
		from onDelete in OnDeleteClause
		select (AlterAction)new SetOnDeleteAction(onDelete);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
	//   ALTER TABLE t ADD ROW DELETION POLICY (OLDER_THAN(col, INTERVAL n DAY))
	private static TokenListParser<GoogleSqlToken, RowDeletionPolicyDef> RowDeletionPolicyBody { get; } =
		from _row in Token.EqualTo(GoogleSqlToken.Row)
		from _deletion in Token.EqualTo(GoogleSqlToken.Deletion)
		from _policy in Token.EqualTo(GoogleSqlToken.Policy)
		from _open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from _olderThan in Token.EqualTo(GoogleSqlToken.OlderThan)
		from _open2 in Token.EqualTo(GoogleSqlToken.OpenParen)
		from column in IdentifierOrKeywordAsName
		from _comma in Token.EqualTo(GoogleSqlToken.Comma)
		from _interval in Token.EqualTo(GoogleSqlToken.Interval)
		from days in Token.EqualTo(GoogleSqlToken.Number).Apply(Numerics.IntegerInt64)
		from _day in Token.EqualTo(GoogleSqlToken.Day)
		from _close2 in Token.EqualTo(GoogleSqlToken.CloseParen)
		from _close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new RowDeletionPolicyDef(column, (int)days);

	private static TokenListParser<GoogleSqlToken, AlterAction> AddRowDeletionPolicyAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Add)
		from policy in RowDeletionPolicyBody
		select (AlterAction)new AddRowDeletionPolicyAction(policy);

	private static TokenListParser<GoogleSqlToken, AlterAction> ReplaceRowDeletionPolicyAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Replace)
		from policy in RowDeletionPolicyBody
		select (AlterAction)new ReplaceRowDeletionPolicyAction(policy);

	private static TokenListParser<GoogleSqlToken, AlterAction> DropRowDeletionPolicyAction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Drop)
		from _row in Token.EqualTo(GoogleSqlToken.Row)
		from _deletion in Token.EqualTo(GoogleSqlToken.Deletion)
		from _policy in Token.EqualTo(GoogleSqlToken.Policy)
		select (AlterAction)new DropRowDeletionPolicyAction();

	public static TokenListParser<GoogleSqlToken, AlterTableStatement> AlterTable { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Alter)
		from __ in Token.EqualTo(GoogleSqlToken.Table)
		from ifExists in IfExists
		from name in IdentifierOrKeywordAsName
		from action in AddRowDeletionPolicyAction.Try()
			.Or(AddColumnAction.Try())
			.Or(AlterColumnSetOptionsAction.Try())
			.Or(AlterColumnAction.Try())
			.Or(DropRowDeletionPolicyAction.Try())
			.Or(DropConstraintAction.Try())
			.Or(DropColumnAction.Try())
			.Or(ReplaceRowDeletionPolicyAction.Try())
			.Or(AddConstraintAction.Try())
			.Or(SetOnDeleteActionParser)
		select new AlterTableStatement(name, action, ifExists);

	// ──────────────────────────────────────────
	// CREATE INDEX
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
	//   CREATE [UNIQUE] [NULL_FILTERED] INDEX index_name ON table_name (col [ASC|DESC], ...)
	//   [STORING (col, ...)]

	private static TokenListParser<GoogleSqlToken, IndexColumnDef> IndexColumnDefinition { get; } =
		from name in IdentifierOrKeywordAsName
		from order in Token.EqualTo(GoogleSqlToken.Asc).Value(SortOrder.Asc)
			.Or(Token.EqualTo(GoogleSqlToken.Desc).Value(SortOrder.Desc))
			.OptionalOrDefault(SortOrder.Asc)
		select new IndexColumnDef(name, order);

	private static TokenListParser<GoogleSqlToken, List<string>> StoringClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Storing)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from columns in IdentifierOrKeywordAsName.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select columns.ToList();

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
	//   ", INTERLEAVE IN table_name" — storage interleaving for index; no-op in memory.
	private static TokenListParser<GoogleSqlToken, string> IndexInterleaveClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Comma)
		from __ in Token.EqualTo(GoogleSqlToken.Interleave)
		from ___ in Token.EqualTo(GoogleSqlToken.In)
		from table in IdentifierOrKeywordAsName
		select table;

	public static TokenListParser<GoogleSqlToken, CreateIndexStatement> CreateIndex { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Create)
		from isUnique in Token.EqualTo(GoogleSqlToken.Unique).Value(true).OptionalOrDefault(false)
		from isNullFiltered in Token.EqualTo(GoogleSqlToken.NullFiltered).Value(true).OptionalOrDefault(false)
		from __ in Token.EqualTo(GoogleSqlToken.Index)
		from ifNotExists in IfNotExists
		from name in IdentifierOrKeywordAsName
		from ___ in Token.EqualTo(GoogleSqlToken.On)
		from tableName in IdentifierOrKeywordAsName
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from columns in IndexColumnDefinition.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from storing in StoringClause.Select(x => (List<string>?)x).OptionalOrDefault()
		from _interleave in IndexInterleaveClause.Try().Select(x => (string?)x).OptionalOrDefault()
		select new CreateIndexStatement(name, tableName, columns.ToList(), storing, isUnique, isNullFiltered, ifNotExists);

	// ──────────────────────────────────────────
	// DROP INDEX
	// ──────────────────────────────────────────

	public static TokenListParser<GoogleSqlToken, DropIndexStatement> DropIndex { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Drop)
		from __ in Token.EqualTo(GoogleSqlToken.Index)
		from ifExists in IfExists
		from name in IdentifierOrKeywordAsName
		select new DropIndexStatement(name, ifExists);

	// ──────────────────────────────────────────
	// Top-level DDL dispatcher
	// ──────────────────────────────────────────

	public static TokenListParser<GoogleSqlToken, object> DdlStatement { get; } =
		CreateTable.Try().Select(x => (object)x)
		.Or(DropTable.Try().Select(x => (object)x))
		.Or(AlterTable.Try().Select(x => (object)x))
		.Or(CreateIndex.Try().Select(x => (object)x))
		.Or(DropIndex.Select(x => (object)x));
}
