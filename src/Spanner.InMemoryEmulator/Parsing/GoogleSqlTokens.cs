using Superpower.Display;

namespace Spanner.InMemoryEmulator.Parsing;

/// <summary>
/// Token types for the GoogleSQL tokenizer.
/// </summary>
internal enum GoogleSqlToken
{
	// ── Identifiers & literals ──
	[Token(Example = "myColumn")]
	Identifier,

	[Token(Example = "`quoted`")]
	QuotedIdentifier,

	[Token(Example = "42")]
	Number,

	[Token(Example = "'hello'")]
	StringLiteral,

	[Token(Example = "b'\\xff'")]
	ByteLiteral,

	[Token(Example = "@param")]
	Parameter,

	// ── Keywords ──
	[Token(Example = "SELECT")]
	Select,

	[Token(Example = "FROM")]
	From,

	[Token(Example = "WHERE")]
	Where,

	[Token(Example = "AND")]
	And,

	[Token(Example = "OR")]
	Or,

	[Token(Example = "NOT")]
	Not,

	[Token(Example = "AS")]
	As,

	[Token(Example = "IN")]
	In,

	[Token(Example = "BETWEEN")]
	Between,

	[Token(Example = "LIKE")]
	Like,

	[Token(Example = "ORDER")]
	Order,

	[Token(Example = "BY")]
	By,

	[Token(Example = "ASC")]
	Asc,

	[Token(Example = "DESC")]
	Desc,

	[Token(Example = "LIMIT")]
	Limit,

	[Token(Example = "OFFSET")]
	Offset,

	[Token(Example = "GROUP")]
	Group,

	[Token(Example = "HAVING")]
	Having,

	[Token(Example = "INSERT")]
	Insert,

	[Token(Example = "INTO")]
	Into,

	[Token(Example = "VALUES")]
	Values,

	[Token(Example = "UPDATE")]
	Update,

	[Token(Example = "SET")]
	Set,

	[Token(Example = "DELETE")]
	Delete,

	[Token(Example = "CREATE")]
	Create,

	[Token(Example = "DROP")]
	Drop,

	[Token(Example = "ALTER")]
	Alter,

	[Token(Example = "TABLE")]
	Table,

	[Token(Example = "INDEX")]
	Index,

	[Token(Example = "VIEW")]
	View,

	[Token(Example = "PRIMARY")]
	Primary,

	[Token(Example = "KEY")]
	Key,

	[Token(Example = "NULL")]
	Null,

	[Token(Example = "UNIQUE")]
	Unique,

	[Token(Example = "INTERLEAVE")]
	Interleave,

	[Token(Example = "PARENT")]
	Parent,

	[Token(Example = "ARRAY")]
	Array,

	[Token(Example = "STRUCT")]
	Struct,

	[Token(Example = "TRUE")]
	True,

	[Token(Example = "FALSE")]
	False,

	[Token(Example = "IS")]
	Is,

	[Token(Example = "JOIN")]
	Join,

	[Token(Example = "INNER")]
	Inner,

	[Token(Example = "LEFT")]
	Left,

	[Token(Example = "RIGHT")]
	Right,

	[Token(Example = "CROSS")]
	Cross,

	[Token(Example = "FULL")]
	Full,

	[Token(Example = "OUTER")]
	Outer,

	[Token(Example = "ON")]
	On,

	[Token(Example = "UNION")]
	Union,

	[Token(Example = "ALL")]
	All,

	[Token(Example = "EXCEPT")]
	Except,

	[Token(Example = "INTERSECT")]
	Intersect,

	[Token(Example = "EXISTS")]
	Exists,

	[Token(Example = "CASE")]
	Case,

	[Token(Example = "WHEN")]
	When,

	[Token(Example = "THEN")]
	Then,

	[Token(Example = "ELSE")]
	Else,

	[Token(Example = "END")]
	End,

	[Token(Example = "CAST")]
	Cast,

	[Token(Example = "SAFE_CAST")]
	SafeCast,

	[Token(Example = "EXTRACT")]
	Extract,

	[Token(Example = "IF")]
	If,

	[Token(Example = "COALESCE")]
	Coalesce,

	[Token(Example = "NULLIF")]
	Nullif,

	[Token(Example = "IFNULL")]
	Ifnull,

	[Token(Example = "UNNEST")]
	Unnest,

	[Token(Example = "WITH")]
	With,

	[Token(Example = "RECURSIVE")]
	Recursive,

	[Token(Example = "DISTINCT")]
	Distinct,

	[Token(Example = "COUNT")]
	Count,

	[Token(Example = "SUM")]
	Sum,

	[Token(Example = "AVG")]
	Avg,

	[Token(Example = "MIN")]
	Min,

	[Token(Example = "MAX")]
	Max,

	// ── Type keywords ──
	[Token(Example = "INT64")]
	Int64Type,

	[Token(Example = "FLOAT64")]
	Float64Type,

	[Token(Example = "FLOAT32")]
	Float32Type,

	[Token(Example = "BOOL")]
	BoolType,

	[Token(Example = "STRING")]
	StringType,

	[Token(Example = "BYTES")]
	BytesType,

	[Token(Example = "DATE")]
	DateType,

	[Token(Example = "TIMESTAMP")]
	TimestampType,

	[Token(Example = "NUMERIC")]
	NumericType,

	[Token(Example = "JSON")]
	JsonType,

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
	[Token(Example = "UUID")]
	UuidType,

	// ── DDL keywords ──
	[Token(Example = "COLUMN")]
	Column,

	[Token(Example = "ADD")]
	Add,

	[Token(Example = "CASCADE")]
	Cascade,

	[Token(Example = "NO")]
	No,

	[Token(Example = "ACTION")]
	Action,

	[Token(Example = "IGNORE")]
	Ignore,

	[Token(Example = "RESPECT")]
	Respect,

	[Token(Example = "NULLS")]
	Nulls,

	[Token(Example = "OPTIONS")]
	Options,

	[Token(Example = "STORED")]
	Stored,

	[Token(Example = "GENERATED")]
	Generated,

	[Token(Example = "ALWAYS")]
	Always,

	[Token(Example = "DEFAULT")]
	Default,

	[Token(Example = "STORING")]
	Storing,

	[Token(Example = "NULL_FILTERED")]
	NullFiltered,

	[Token(Example = "REPLACE")]
	Replace,

	[Token(Example = "OVER")]
	Over,

	[Token(Example = "PARTITION")]
	Partition,

	[Token(Example = "ROWS")]
	Rows,

	[Token(Example = "RANGE")]
	Range,

	[Token(Example = "PRECEDING")]
	Preceding,

	[Token(Example = "FOLLOWING")]
	Following,

	[Token(Example = "UNBOUNDED")]
	Unbounded,

	[Token(Example = "CURRENT")]
	Current,

	[Token(Example = "ROW")]
	Row,

	[Token(Example = "USING")]
	Using,

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
	[Token(Example = "TABLESAMPLE")]
	Tablesample,

	[Token(Example = "BERNOULLI")]
	Bernoulli,

	[Token(Example = "RESERVOIR")]
	Reservoir,

	[Token(Example = "PERCENT")]
	Percent,

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
	[Token(Example = "DELETION")]
	Deletion,

	[Token(Example = "POLICY")]
	Policy,

	[Token(Example = "OLDER_THAN")]
	OlderThan,

	[Token(Example = "INTERVAL")]
	Interval,

	[Token(Example = "DAY")]
	Day,

	// ── Operators ──
	[Token(Example = "+")]
	Plus,

	[Token(Example = "-")]
	Minus,

	[Token(Example = "*")]
	Star,

	[Token(Example = "/")]
	Divide,

	[Token(Example = "%")]
	Modulo,

	[Token(Example = "=")]
	Equal,

	[Token(Example = "!=")]
	NotEqual,

	[Token(Example = "<")]
	LessThan,

	[Token(Example = ">")]
	GreaterThan,

	[Token(Example = "<=")]
	LessThanOrEqual,

	[Token(Example = ">=")]
	GreaterThanOrEqual,

	[Token(Example = "<>")]
	LessGreater,

	[Token(Example = "||")]
	DoublePipe,

	[Token(Example = "&&")]
	DoubleAmpersand,

	[Token(Example = "=>")]
	FatArrow,

	// ── Punctuation ──
	[Token(Example = "(")]
	OpenParen,

	[Token(Example = ")")]
	CloseParen,

	[Token(Example = ",")]
	Comma,

	[Token(Example = ".")]
	Dot,

	[Token(Example = ";")]
	Semicolon,

	[Token(Example = "[")]
	OpenBracket,

	[Token(Example = "]")]
	CloseBracket,

	[Token(Example = "CHECK")]
	Check,

	[Token(Example = "CONSTRAINT")]
	Constraint,

	[Token(Example = "FOREIGN")]
	Foreign,

	[Token(Example = "REFERENCES")]
	References,

	[Token(Example = "ENFORCED")]
	Enforced,

	[Token(Example = "SEQUENCE")]
	Sequence,

	// ── THEN RETURN support ──
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#then_return
	[Token(Example = "RETURN")]
	Return,

	// ── QUALIFY support ──
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#qualify_clause
	[Token(Example = "QUALIFY")]
	Qualify,

	[Token(Example = "HIDDEN")]
	Hidden,

	[Token(Example = "SEARCH")]
	Search,

	[Token(Example = "TOKENLIST")]
	Tokenlist,
}
