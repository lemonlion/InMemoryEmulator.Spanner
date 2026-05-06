using Google.Cloud.Spanner.V1;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator.Parsing;

// ──────────────────────────────────────────────
// DDL Statements
// ──────────────────────────────────────────────

internal record CreateTableStatement(
	string Name,
	List<ParsedColumnDef> Columns,
	List<PrimaryKeyPart> PrimaryKey,
	string? ParentTable,
	OnDeleteAction? OnDelete,
	List<ParsedCheckConstraint>? CheckConstraints = null,
	List<ParsedForeignKey>? ForeignKeys = null,
	bool IfNotExists = false,
	RowDeletionPolicyDef? RowDeletionPolicy = null);

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
//   ROW DELETION POLICY (OLDER_THAN(column, INTERVAL n DAY))
internal record RowDeletionPolicyDef(string Column, int IntervalDays);

internal record ParsedCheckConstraint(string? Name, string Expression);
internal record ParsedForeignKey(
	string? Name,
	List<string> Columns,
	string ReferencedTable,
	List<string> ReferencedColumns,
	bool IsEnforced = true,
	ForeignKeyDeleteAction OnDelete = ForeignKeyDeleteAction.NoAction);

internal record DropTableStatement(string Name, bool IfExists = false);

internal record AlterTableStatement(string Name, AlterAction Action, bool IfExists = false);

internal abstract record AlterAction;
internal record AddColumnAction(ParsedColumnDef Column, bool IfNotExists = false) : AlterAction;
internal record DropColumnAction(string ColumnName, bool IfExists = false) : AlterAction;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
//   ALTER TABLE t ALTER COLUMN c type [NOT NULL] [DEFAULT (expr)]
internal record AlterColumnAction(string ColumnName, ParsedColumnDef NewDefinition) : AlterAction;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
//   ALTER TABLE t ALTER COLUMN c SET OPTIONS (allow_commit_timestamp = true|false)
internal record AlterColumnSetOptionsAction(string ColumnName, bool AllowCommitTimestamp) : AlterAction;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
//   ALTER TABLE t ADD CONSTRAINT ...
internal record AddConstraintAction(ParsedCheckConstraint? Check, ParsedForeignKey? ForeignKey) : AlterAction;
internal record DropConstraintAction(string ConstraintName) : AlterAction;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
//   ALTER TABLE t SET ON DELETE CASCADE | NO ACTION
internal record SetOnDeleteAction(OnDeleteAction OnDelete) : AlterAction;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
//   ALTER TABLE t ADD ROW DELETION POLICY (OLDER_THAN(col, INTERVAL n DAY))
//   ALTER TABLE t REPLACE ROW DELETION POLICY (OLDER_THAN(col, INTERVAL n DAY))
//   ALTER TABLE t DROP ROW DELETION POLICY
internal record AddRowDeletionPolicyAction(RowDeletionPolicyDef Policy) : AlterAction;
internal record ReplaceRowDeletionPolicyAction(RowDeletionPolicyDef Policy) : AlterAction;
internal record DropRowDeletionPolicyAction() : AlterAction;

internal record CreateIndexStatement(
	string Name,
	string TableName,
	List<IndexColumnDef> Columns,
	List<string>? StoringColumns,
	bool IsUnique,
	bool IsNullFiltered,
	bool IfNotExists = false);

internal record DropIndexStatement(string Name, bool IfExists = false);

/// <summary>
/// CREATE SEARCH INDEX — accepted by DDL parser but not enforced at runtime.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-search-index
/// </summary>
internal record CreateSearchIndexStatement(string Name, string TableName);

/// <summary>
/// DROP SEARCH INDEX — accepted by DDL parser but not enforced at runtime.
/// </summary>
internal record DropSearchIndexStatement(string Name);

internal record CreateViewStatement(string Name, string SqlBody, bool OrReplace);
internal record DropViewStatement(string Name);

internal record CreateSequenceStatement(string Name, string SequenceKind, long? StartWithCounter, long? SkipRangeMin, long? SkipRangeMax);
internal record DropSequenceStatement(string Name);

// ──────────────────────────────────────────────
// DDL Supporting Types
// ──────────────────────────────────────────────

internal record ParsedColumnDef(
	string Name,
	TypeCode SpannerType,
	bool IsNullable,
	long? MaxLength,
	TypeCode? ArrayElementType,
	string? GeneratedExpression,
	bool IsStored,
	string? DefaultExpression,
	bool AllowCommitTimestamp,
	bool IsHidden = false,
	string? ProtoTypeFqn = null);

internal record PrimaryKeyPart(string ColumnName, SortOrder Order = SortOrder.Asc);

internal record IndexColumnDef(string ColumnName, SortOrder Order = SortOrder.Asc);

// ──────────────────────────────────────────────
// DML Statements
// ──────────────────────────────────────────────

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#then_return
internal record ReturningClause(bool WithAction, string? ActionAlias, List<SelectColumn> Columns);

internal record InsertStatement(
	string Table,
	List<string> Columns,
	List<List<SqlExpression>>? ValueRows,
	InsertMode Mode = InsertMode.Insert,
	QueryBody? SelectSource = null,
	ReturningClause? Returning = null,
	OnConflictClause? OnConflict = null);

internal enum InsertMode { Insert, InsertOrUpdate, InsertOrIgnore }

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
//   ON CONFLICT [conflict_target] conflict_action
internal record OnConflictClause(
	List<string>? ConflictColumns,
	string? UniqueConstraintName,
	OnConflictAction Action,
	List<SetClause>? UpdateSets = null,
	SqlExpression? UpdateWhere = null);

internal enum OnConflictAction { DoNothing, DoUpdate }

internal record UpdateStatement(
	string Table,
	List<SetClause> Sets,
	SqlExpression? Where,
	ReturningClause? Returning = null);

internal record SetClause(string Column, SqlExpression Value);

internal record DeleteStatement(string Table, SqlExpression? Where, ReturningClause? Returning = null);

// ──────────────────────────────────────────────
// SELECT / Query
// ──────────────────────────────────────────────

internal record SelectStatement(
	bool IsDistinct,
	List<SelectColumn> Columns,
	FromClause? From,
	SqlExpression? Where,
	List<SqlExpression>? GroupBy,
	SqlExpression? Having,
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#qualify_clause
	//   "Filters the results of window functions."
	SqlExpression? Qualify,
	List<OrderByColumn>? OrderBy,
	SqlExpression? Limit,
	SqlExpression? Offset,
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_as_struct
	//   "SELECT AS STRUCT produces a value table with a STRUCT row type."
	bool AsStruct = false);

internal record SelectColumn(SqlExpression Expr, string? Alias);

internal record FromClause(
	string Table,
	string? Alias,
	List<JoinClause>? Joins,
	TableSampleClause? TableSample = null);

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
internal enum TableSampleMethod { Bernoulli, Reservoir }
internal enum TableSampleUnit { Percent, Rows }
internal record TableSampleClause(TableSampleMethod Method, double Size, TableSampleUnit Unit);

internal record JoinClause(
	JoinType Type,
	string Table,
	string? Alias,
	SqlExpression? On,
	List<string>? UsingColumns = null,
	QueryBody? Subquery = null,
	SqlExpression? UnnestExpr = null,
	bool UnnestWithOffset = false,
	string? UnnestOffsetAlias = null);

internal enum JoinType
{
	Inner,
	Left,
	Right,
	Full,
	Cross
}

internal record OrderByColumn(SqlExpression Expr, SortOrder Order);

// ──────────────────────────────────────────────
// SQL Expressions (shared by DML, SELECT, WHERE)
// ──────────────────────────────────────────────

internal abstract record SqlExpression;

internal record ColumnRefExpr(string? TableAlias, string Column) : SqlExpression;

internal record LiteralExpr(object? Value) : SqlExpression;

internal record ParameterExpr(string Name) : SqlExpression;

internal record BinaryExpr(SqlExpression Left, BinaryOp Op, SqlExpression Right) : SqlExpression;

internal record UnaryExpr(UnaryOp Op, SqlExpression Operand) : SqlExpression;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
// NullHandling: null=default, true=RESPECT NULLS, false=IGNORE NULLS
internal record FunctionCallExpr(string Name, List<SqlExpression> Arguments, bool IsDistinct = false,
	List<OrderByColumn>? AggregateOrderBy = null, bool? NullHandling = null) : SqlExpression;

/// <summary>Named argument in a function call, e.g. dialect => 'words'.</summary>
internal record NamedArgExpr(string ArgName, SqlExpression Value) : SqlExpression;

internal record CastExpr(SqlExpression Value, TypeCode TargetType, bool Safe = false) : SqlExpression;

internal record CaseExpr(SqlExpression? Operand, List<WhenClause> Whens, SqlExpression? Else) : SqlExpression;

internal record WhenClause(SqlExpression Condition, SqlExpression Result);

internal record InExpr(SqlExpression Value, List<SqlExpression> List, bool IsNegated) : SqlExpression;

internal record BetweenExpr(SqlExpression Value, SqlExpression Low, SqlExpression High, bool IsNegated) : SqlExpression;

internal record IsNullExpr(SqlExpression Value, bool IsNegated) : SqlExpression;

internal record StarExpr() : SqlExpression;

internal record CountStarExpr() : SqlExpression;

// Subquery expressions
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries

/// <summary>Scalar subquery: (SELECT col FROM ...)</summary>
internal record ScalarSubqueryExpr(QueryBody Subquery) : SqlExpression;

/// <summary>EXISTS (SELECT ...)</summary>
internal record ExistsExpr(QueryBody Subquery, bool IsNegated) : SqlExpression;

/// <summary>expr [NOT] IN (SELECT ...)</summary>
internal record InSubqueryExpr(SqlExpression Value, QueryBody Subquery, bool IsNegated) : SqlExpression;

// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
//   "value [NOT] IN UNNEST(array_expression)"
internal record InUnnestExpr(SqlExpression Value, SqlExpression ArrayExpr, bool IsNegated) : SqlExpression;

/// <summary>ARRAY(SELECT ...)</summary>
internal record ArraySubqueryExpr(QueryBody Subquery) : SqlExpression;
internal record ArrayLiteralExpr(List<SqlExpression> Elements) : SqlExpression;

// ──────────────────────────────────────────────
// Window Functions
// ──────────────────────────────────────────────
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls

internal enum FrameBoundType
{
	UnboundedPreceding, UnboundedFollowing, CurrentRow, OffsetPreceding, OffsetFollowing
}

internal record WindowFrame(FrameBoundType Type, long Offset);
internal record WindowFrameClause(WindowFrame Start, WindowFrame End);

internal record WindowExpr(
	SqlExpression Function,
	List<SqlExpression>? PartitionBy,
	List<OrderByColumn>? OrderBy,
	WindowFrameClause? Frame = null) : SqlExpression;

// ──────────────────────────────────────────────
// UNNEST
// ──────────────────────────────────────────────
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#unnest_operator

internal record UnnestFromClause(
	SqlExpression ArrayExpr,
	string? Alias,
	bool WithOffset,
	string? OffsetAlias,
	List<JoinClause>? Joins) : FromClause("__unnest__", Alias, Joins);

// ──────────────────────────────────────────────
// Array element access: arr[OFFSET(n)], arr[ORDINAL(n)], arr[SAFE_OFFSET(n)], arr[SAFE_ORDINAL(n)]
// ──────────────────────────────────────────────
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_subscript_operator

internal enum ArrayAccessMode { Offset, Ordinal, SafeOffset, SafeOrdinal }
internal record ArrayAccessExpr(SqlExpression Array, SqlExpression Index, ArrayAccessMode Mode) : SqlExpression;

// ──────────────────────────────────────────────
// STRUCT
// ──────────────────────────────────────────────
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#struct_type

internal record StructExpr(List<(string? Name, SqlExpression Value)> Fields) : SqlExpression;
internal record StructFieldAccessExpr(SqlExpression Struct, string FieldName) : SqlExpression;
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#struct_field_access_operator
//   "STRUCT(...).*": The dot star operator returns all fields of a STRUCT.
internal record StructExpandExpr(SqlExpression Struct) : SqlExpression;

/// <summary>Subquery as FROM source: FROM (SELECT ...) AS alias</summary>
internal record SubqueryFromClause(
	QueryBody Subquery,
	string Alias,
	List<JoinClause>? Joins) : FromClause(Alias, Alias, Joins);

// Set operations
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators

internal enum SetOperationType { UnionAll, UnionDistinct, IntersectAll, IntersectDistinct, ExceptAll, ExceptDistinct }

internal record SetOperation(SetOperationType Type, SelectStatement Right);

/// <summary>A SELECT with optional set operations (no CTEs).</summary>
internal record QueryBody(SelectStatement Select, List<SetOperation>? SetOps);

/// <summary>A query with optional CTEs and a body (SELECT + set operations).</summary>
internal record FullQuery(
	List<CteDefinition>? Ctes,
	QueryBody Body,
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#recursive_keyword
	//   "RECURSIVE enables references to CTEs from within their own definitions."
	bool IsRecursive = false);

internal record CteDefinition(string Name, QueryBody Query);

internal enum BinaryOp
{
	Equal,
	NotEqual,
	LessThan,
	GreaterThan,
	LessThanOrEqual,
	GreaterThanOrEqual,
	And,
	Or,
	Add,
	Subtract,
	Multiply,
	Divide,
	Modulo,
	Concat // ||
}

internal enum UnaryOp
{
	Not,
	Negate
}
