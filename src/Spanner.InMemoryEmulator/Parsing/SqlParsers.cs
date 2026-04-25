using Superpower;
using Superpower.Parsers;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator.Parsing;

/// <summary>
/// Parsers for SQL SELECT statements, DML, and expressions.
/// </summary>
internal static class SqlParsers
{
	// ──────────────────────────────────────────
	// Expression Parsing (shared by SELECT, WHERE, DML)
	// ──────────────────────────────────────────

	// Forward reference for recursive expression parsing
	private static readonly TokenListParser<GoogleSqlToken, SqlExpression> ExpressionRef =
		Parse.Ref(() => Expression!);  // Suppress null warning — Expression is set below

	// ── Atoms ──

	private static TokenListParser<GoogleSqlToken, SqlExpression> NumberLiteral { get; } =
		Token.EqualTo(GoogleSqlToken.Number).Select(t =>
		{
			var text = t.ToStringValue();
			if (text.Contains('.'))
				return (SqlExpression)new LiteralExpr(double.Parse(text));
			return (SqlExpression)new LiteralExpr(long.Parse(text));
		});

	private static TokenListParser<GoogleSqlToken, SqlExpression> StringLiteral { get; } =
		Token.EqualTo(GoogleSqlToken.StringLiteral).Select(t =>
		{
			var text = t.ToStringValue();
			// Remove surrounding quotes and unescape ''
			var unquoted = text[1..^1].Replace("''", "'");
			return (SqlExpression)new LiteralExpr(unquoted);
		});

	private static TokenListParser<GoogleSqlToken, SqlExpression> BoolLiteral { get; } =
		Token.EqualTo(GoogleSqlToken.True).Value((SqlExpression)new LiteralExpr(true))
		.Or(Token.EqualTo(GoogleSqlToken.False).Value((SqlExpression)new LiteralExpr(false)));

	private static TokenListParser<GoogleSqlToken, SqlExpression> NullLiteral { get; } =
		Token.EqualTo(GoogleSqlToken.Null).Value((SqlExpression)new LiteralExpr(null));

	private static TokenListParser<GoogleSqlToken, SqlExpression> Parameter { get; } =
		Token.EqualTo(GoogleSqlToken.Parameter).Select(t =>
		{
			// Remove @ prefix
			var name = t.ToStringValue()[1..];
			return (SqlExpression)new ParameterExpr(name);
		});

	private static TokenListParser<GoogleSqlToken, SqlExpression> Star { get; } =
		Token.EqualTo(GoogleSqlToken.Star).Value((SqlExpression)new StarExpr());

	// ── Column references / identifiers ──

	private static TokenListParser<GoogleSqlToken, string> AnyIdentifier { get; } =
		Token.EqualTo(GoogleSqlToken.Identifier).Select(t => t.ToStringValue())
		.Or(Token.EqualTo(GoogleSqlToken.QuotedIdentifier).Select(t => t.ToStringValue().Trim('`')))
		// Allow keywords as column names when they appear in expression position
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
		.Or(Token.EqualTo(GoogleSqlToken.Replace).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Left).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Right).Select(t => t.ToStringValue()));

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
	private static TokenListParser<GoogleSqlToken, WindowExpr> OverClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Over)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from partitionBy in (
			from __ in Token.EqualTo(GoogleSqlToken.Partition)
			from ___ in Token.EqualTo(GoogleSqlToken.By)
			from exprs in ExpressionRef.AtLeastOnceDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select exprs.ToList()
		).AsNullable().OptionalOrDefault()
		from orderBy in (
			from __ in Token.EqualTo(GoogleSqlToken.Order)
			from ___ in Token.EqualTo(GoogleSqlToken.By)
			from items in (
				from expr in ExpressionRef
				from order in Token.EqualTo(GoogleSqlToken.Asc).Value(SortOrder.Asc)
					.Or(Token.EqualTo(GoogleSqlToken.Desc).Value(SortOrder.Desc))
					.OptionalOrDefault(SortOrder.Asc)
				select new OrderByColumn(expr, order)
			).AtLeastOnceDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select items.ToList()
		).AsNullable().OptionalOrDefault()
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new WindowExpr(null!, partitionBy, orderBy); // Function filled in by caller

	private static TokenListParser<GoogleSqlToken, SqlExpression> ColumnRefOrFunction { get; } =
		from name in AnyIdentifier
		from result in
			// Function call: name(args...)
			(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
			 from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
			 from args in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
			 from window in OverClause.AsNullable().OptionalOrDefault()
			 select window != null
				? (SqlExpression)new WindowExpr(new FunctionCallExpr(name, args.ToList(), distinct), window.PartitionBy, window.OrderBy)
				: (SqlExpression)new FunctionCallExpr(name, args.ToList(), distinct))
			// Qualified column: alias.column
			.Or(from dot in Token.EqualTo(GoogleSqlToken.Dot)
				from col in AnyIdentifier
				select (SqlExpression)new ColumnRefExpr(name, col))
			// Simple column reference
			.OptionalOrDefault((SqlExpression)new ColumnRefExpr(null, name))
		select result;

	// Built-in function keywords (COUNT, SUM, etc.)
	private static TokenListParser<GoogleSqlToken, SqlExpression> CountFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Count)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from result in Token.EqualTo(GoogleSqlToken.Star).Value((SqlExpression)new CountStarExpr())
			.Or(from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
				from arg in ExpressionRef
				select (SqlExpression)new FunctionCallExpr("COUNT", new List<SqlExpression> { arg }, distinct))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from window in OverClause.AsNullable().OptionalOrDefault()
		select window != null
			? (SqlExpression)new WindowExpr(result, window.PartitionBy, window.OrderBy)
			: result;

	private static TokenListParser<GoogleSqlToken, SqlExpression> AggregateKeywordFunction(GoogleSqlToken keyword, string name) =>
		from _ in Token.EqualTo(keyword)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
		from arg in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from window in OverClause.AsNullable().OptionalOrDefault()
		select window != null
			? (SqlExpression)new WindowExpr(new FunctionCallExpr(name, new List<SqlExpression> { arg }, distinct), window.PartitionBy, window.OrderBy)
			: (SqlExpression)new FunctionCallExpr(name, new List<SqlExpression> { arg }, distinct);

	// CAST(expr AS type) — supports bare type names without length specifiers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	private static TokenListParser<GoogleSqlToken, TypeCode> CastTargetType { get; } =
		Token.EqualTo(GoogleSqlToken.Int64Type).Value(TypeCode.Int64)
		.Or(Token.EqualTo(GoogleSqlToken.Float64Type).Value(TypeCode.Float64))
		.Or(Token.EqualTo(GoogleSqlToken.Float32Type).Value(TypeCode.Float32))
		.Or(Token.EqualTo(GoogleSqlToken.BoolType).Value(TypeCode.Bool))
		.Or(Token.EqualTo(GoogleSqlToken.StringType).Value(TypeCode.String))
		.Or(Token.EqualTo(GoogleSqlToken.BytesType).Value(TypeCode.Bytes))
		.Or(Token.EqualTo(GoogleSqlToken.TimestampType).Value(TypeCode.Timestamp))
		.Or(Token.EqualTo(GoogleSqlToken.DateType).Value(TypeCode.Date))
		.Or(Token.EqualTo(GoogleSqlToken.NumericType).Value(TypeCode.Numeric))
		.Or(Token.EqualTo(GoogleSqlToken.JsonType).Value(TypeCode.Json));

	private static TokenListParser<GoogleSqlToken, SqlExpression> CastFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Cast)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from value in ExpressionRef
		from __ in Token.EqualTo(GoogleSqlToken.As)
		from type in CastTargetType
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new CastExpr(value, type);

	private static TokenListParser<GoogleSqlToken, SqlExpression> SafeCastFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.SafeCast)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from value in ExpressionRef
		from __ in Token.EqualTo(GoogleSqlToken.As)
		from type in CastTargetType
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new CastExpr(value, type, Safe: true);

	// IF(condition, then, else)
	private static TokenListParser<GoogleSqlToken, SqlExpression> IfFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.If)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from cond in ExpressionRef
		from c1 in Token.EqualTo(GoogleSqlToken.Comma)
		from then in ExpressionRef
		from c2 in Token.EqualTo(GoogleSqlToken.Comma)
		from elseExpr in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr("IF", new List<SqlExpression> { cond, then, elseExpr });

	// COALESCE(expr, ...)
	private static TokenListParser<GoogleSqlToken, SqlExpression> CoalesceFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Coalesce)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from args in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr("COALESCE", args.ToList());

	// NULLIF(expr, expr)
	private static TokenListParser<GoogleSqlToken, SqlExpression> NullIfFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Nullif)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from a in ExpressionRef
		from c in Token.EqualTo(GoogleSqlToken.Comma)
		from b in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr("NULLIF", new List<SqlExpression> { a, b });

	// IFNULL(expr, expr)
	private static TokenListParser<GoogleSqlToken, SqlExpression> IfNullFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Ifnull)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from a in ExpressionRef
		from c in Token.EqualTo(GoogleSqlToken.Comma)
		from b in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr("IFNULL", new List<SqlExpression> { a, b });

	// CASE WHEN ... THEN ... [ELSE ...] END
	private static TokenListParser<GoogleSqlToken, SqlExpression> CaseExpression { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Case)
		from operand in ExpressionRef.AsNullable().OptionalOrDefault()
		from whens in (
			from __ in Token.EqualTo(GoogleSqlToken.When)
			from cond in ExpressionRef
			from ___ in Token.EqualTo(GoogleSqlToken.Then)
			from result in ExpressionRef
			select new WhenClause(cond, result)
		).AtLeastOnce()
		from elseExpr in (
			from ___ in Token.EqualTo(GoogleSqlToken.Else)
			from e in ExpressionRef
			select e
		).AsNullable().OptionalOrDefault()
		from ____ in Token.EqualTo(GoogleSqlToken.End)
		select (SqlExpression)new CaseExpr(operand, whens.ToList(), elseExpr);

	// Parenthesized expression — or scalar subquery if contents start with SELECT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery_concepts
	private static TokenListParser<GoogleSqlToken, SqlExpression> Parenthesized { get; } =
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from result in
			(from sub in Parse.Ref(() => SelectStatement!)
			 select (SqlExpression)new ScalarSubqueryExpr(sub))
			.Try()
			.Or(ExpressionRef)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select result;

	// EXISTS (SELECT ...)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subquery_concepts
	private static TokenListParser<GoogleSqlToken, SqlExpression> ExistsFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Exists)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from sub in Parse.Ref(() => SelectStatement!)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new ExistsExpr(sub, false);

	// ARRAY(SELECT ...)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery_concepts
	private static TokenListParser<GoogleSqlToken, SqlExpression> ArraySubquery { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Array)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from sub in Parse.Ref(() => SelectStatement!)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new ArraySubqueryExpr(sub);

	// Unary NOT
	private static TokenListParser<GoogleSqlToken, SqlExpression> UnaryNot { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Not)
		from operand in Parse.Ref(() => Atom!)
		select (SqlExpression)new UnaryExpr(UnaryOp.Not, operand);

	// Unary negation
	private static TokenListParser<GoogleSqlToken, SqlExpression> UnaryNegate { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Minus)
		from operand in Parse.Ref(() => Atom!)
		select (SqlExpression)new UnaryExpr(UnaryOp.Negate, operand);

	// SEQUENCE name → treated as a column reference for sequence function arguments
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_next_sequence_value
	private static TokenListParser<GoogleSqlToken, SqlExpression> SequenceRef { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Sequence)
		from name in AnyIdentifier
		select (SqlExpression)new ColumnRefExpr(null, name);

	// ── Atom (lowest precedence building block) ──

	// Array literal: [expr, expr, ...]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array
	private static TokenListParser<GoogleSqlToken, SqlExpression> ArrayLiteral { get; } =
		from open in Token.EqualTo(GoogleSqlToken.OpenBracket)
		from elements in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseBracket)
		select (SqlExpression)new ArrayLiteralExpr(elements.ToList());

	// STRUCT(expr [AS name], ...)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#constructing_a_struct
	private static TokenListParser<GoogleSqlToken, SqlExpression> StructConstructor { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Struct)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from fields in (
			from expr in ExpressionRef
			from name in (from __ in Token.EqualTo(GoogleSqlToken.As) from n in AnyIdentifier select (string?)n)
				.OptionalOrDefault()
			select (Name: name, Value: expr)
		).ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new StructExpr(fields.Select(f => ((string?)f.Name, f.Value)).ToList());

	private static TokenListParser<GoogleSqlToken, SqlExpression> Atom { get; } =
		CastFunction
		.Or(SafeCastFunction)
		.Or(IfFunction)
		.Or(CoalesceFunction)
		.Or(NullIfFunction)
		.Or(IfNullFunction)
		.Or(CountFunction)
		.Or(AggregateKeywordFunction(GoogleSqlToken.Sum, "SUM"))
		.Or(AggregateKeywordFunction(GoogleSqlToken.Avg, "AVG"))
		.Or(AggregateKeywordFunction(GoogleSqlToken.Min, "MIN"))
		.Or(AggregateKeywordFunction(GoogleSqlToken.Max, "MAX"))
		.Or(ExistsFunction)
		.Or(ArraySubquery.Try())
		.Or(ArrayLiteral)
		.Or(CaseExpression)
		.Or(SequenceRef)
		.Or(BoolLiteral)
		.Or(NullLiteral)
		.Or(StringLiteral)
		.Or(NumberLiteral)
		.Or(Parameter)
		.Or(UnaryNot)
		.Or(UnaryNegate)
		.Or(Parenthesized)
		.Or(StructConstructor)
		.Or(ColumnRefOrFunction);

	// ── Array element access: expr[OFFSET(n)], expr[ORDINAL(n)], expr[SAFE_OFFSET(n)], expr[SAFE_ORDINAL(n)] ──
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_subscript_operator

	private static TokenListParser<GoogleSqlToken, ArrayAccessExpr> ArrayAccessSuffix(SqlExpression array) =>
		from open in Token.EqualTo(GoogleSqlToken.OpenBracket)
		from mode in
			Token.EqualTo(GoogleSqlToken.Identifier)
				.Where(t => t.ToStringValue().Equals("SAFE_OFFSET", StringComparison.OrdinalIgnoreCase))
				.Value(ArrayAccessMode.SafeOffset)
			.Or(Token.EqualTo(GoogleSqlToken.Identifier)
				.Where(t => t.ToStringValue().Equals("SAFE_ORDINAL", StringComparison.OrdinalIgnoreCase))
				.Value(ArrayAccessMode.SafeOrdinal))
			.Or(Token.EqualTo(GoogleSqlToken.Offset).Value(ArrayAccessMode.Offset))
			.Or(Token.EqualTo(GoogleSqlToken.Identifier)
				.Where(t => t.ToStringValue().Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
				.Value(ArrayAccessMode.Offset))
			.Or(Token.EqualTo(GoogleSqlToken.Identifier)
				.Where(t => t.ToStringValue().Equals("ORDINAL", StringComparison.OrdinalIgnoreCase))
				.Value(ArrayAccessMode.Ordinal))
		from openP in Token.EqualTo(GoogleSqlToken.OpenParen)
		from index in ExpressionRef
		from closeP in Token.EqualTo(GoogleSqlToken.CloseParen)
		from close in Token.EqualTo(GoogleSqlToken.CloseBracket)
		select new ArrayAccessExpr(array, index, mode);

	private static TokenListParser<GoogleSqlToken, SqlExpression> PostfixAtom { get; } =
		Atom.Then(atom =>
			ArrayAccessSuffix(atom).Select(x => (SqlExpression)x)
				.OptionalOrDefault(atom));

	// ── Multiplicative: *, /, % ──

	private static TokenListParser<GoogleSqlToken, SqlExpression> Multiplicative { get; } =
		Parse.Chain(
			Token.EqualTo(GoogleSqlToken.Star).Value(BinaryOp.Multiply)
				.Or(Token.EqualTo(GoogleSqlToken.Divide).Value(BinaryOp.Divide))
				.Or(Token.EqualTo(GoogleSqlToken.Modulo).Value(BinaryOp.Modulo)),
			PostfixAtom,
			(op, left, right) => new BinaryExpr(left, op, right));

	// ── Additive: +, -, || ──

	private static TokenListParser<GoogleSqlToken, SqlExpression> Additive { get; } =
		Parse.Chain(
			Token.EqualTo(GoogleSqlToken.Plus).Value(BinaryOp.Add)
				.Or(Token.EqualTo(GoogleSqlToken.Minus).Value(BinaryOp.Subtract))
				.Or(Token.EqualTo(GoogleSqlToken.DoublePipe).Value(BinaryOp.Concat)),
			Multiplicative,
			(op, left, right) => new BinaryExpr(left, op, right));

	// ── Comparison: =, !=, <, >, <=, >= ──

	private static TokenListParser<GoogleSqlToken, SqlExpression> Comparison { get; } =
		from left in Additive
		from postfix in (
			// IS [NOT] NULL
			(from _ in Token.EqualTo(GoogleSqlToken.Is)
			 from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
			 from __ in Token.EqualTo(GoogleSqlToken.Null)
			 select (Func<SqlExpression, SqlExpression>)(l => new IsNullExpr(l, not)))
			// [NOT] IN (SELECT ...) or [NOT] IN (list)
			.Or(from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.In)
				from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				from inBody in
					(from sub in Parse.Ref(() => SelectStatement!)
					 select (object)sub).Try()
					.Or(from list in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
						select (object)list.ToList())
				from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				select (Func<SqlExpression, SqlExpression>)(l =>
					inBody is SelectStatement subq
						? new InSubqueryExpr(l, subq, not)
						: new InExpr(l, (List<SqlExpression>)inBody, not)))
			// [NOT] BETWEEN low AND high
			.Or(from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.Between)
				from low in Additive
				from __ in Token.EqualTo(GoogleSqlToken.And)
				from high in Additive
				select (Func<SqlExpression, SqlExpression>)(l => new BetweenExpr(l, low, high, not)))
			// [NOT] LIKE pattern
			.Or(from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.Like)
				from pattern in Additive
				select (Func<SqlExpression, SqlExpression>)(l =>
				{
					var func = new FunctionCallExpr("LIKE", new List<SqlExpression> { l, pattern });
					return not ? new UnaryExpr(UnaryOp.Not, func) : (SqlExpression)func;
				}))
			// Binary comparison
			.Or(from op in
					Token.EqualTo(GoogleSqlToken.Equal).Value(BinaryOp.Equal)
					.Or(Token.EqualTo(GoogleSqlToken.NotEqual).Value(BinaryOp.NotEqual))
					.Or(Token.EqualTo(GoogleSqlToken.LessGreater).Value(BinaryOp.NotEqual))
					.Or(Token.EqualTo(GoogleSqlToken.LessThanOrEqual).Value(BinaryOp.LessThanOrEqual))
					.Or(Token.EqualTo(GoogleSqlToken.GreaterThanOrEqual).Value(BinaryOp.GreaterThanOrEqual))
					.Or(Token.EqualTo(GoogleSqlToken.LessThan).Value(BinaryOp.LessThan))
					.Or(Token.EqualTo(GoogleSqlToken.GreaterThan).Value(BinaryOp.GreaterThan))
				from right in Additive
				select (Func<SqlExpression, SqlExpression>)(l => new BinaryExpr(l, op, right)))
		).OptionalOrDefault(l => l)
		select postfix(left);

	// ── AND ──

	private static TokenListParser<GoogleSqlToken, SqlExpression> AndExpression { get; } =
		Parse.Chain(
			Token.EqualTo(GoogleSqlToken.And).Value(BinaryOp.And),
			Comparison,
			(op, left, right) => new BinaryExpr(left, op, right));

	// ── OR ──

	public static TokenListParser<GoogleSqlToken, SqlExpression> Expression { get; } =
		Parse.Chain(
			Token.EqualTo(GoogleSqlToken.Or).Value(BinaryOp.Or),
			AndExpression,
			(op, left, right) => new BinaryExpr(left, op, right));

	// ──────────────────────────────────────────
	// SELECT statement
	// ──────────────────────────────────────────

	private static TokenListParser<GoogleSqlToken, SelectColumn> SelectColumnItem { get; } =
		from expr in Token.EqualTo(GoogleSqlToken.Star).Value((SqlExpression)new StarExpr()).Or(Expression)
		from alias in (
			from _ in Token.EqualTo(GoogleSqlToken.As)
			from name in AnyIdentifier
			select name
		).Or(
			// Alias without AS (just identifier after expression, but only if the expression isn't an identifier itself)
			// This is tricky - skip for now and require AS keyword
			Parse.Return<GoogleSqlToken, string?>(null)
		).OptionalOrDefault()
		select new SelectColumn(expr, alias);

	private static TokenListParser<GoogleSqlToken, OrderByColumn> OrderByItem { get; } =
		from expr in Expression
		from order in Token.EqualTo(GoogleSqlToken.Asc).Value(SortOrder.Asc)
			.Or(Token.EqualTo(GoogleSqlToken.Desc).Value(SortOrder.Desc))
			.OptionalOrDefault(SortOrder.Asc)
		select new OrderByColumn(expr, order);

	private static TokenListParser<GoogleSqlToken, JoinClause> JoinItem { get; } =
		from joinType in
			(from _ in Token.EqualTo(GoogleSqlToken.Inner) select JoinType.Inner)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Left)
				from __ in Token.EqualTo(GoogleSqlToken.Outer).Value(true).OptionalOrDefault(false)
				select JoinType.Left)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Right)
				from __ in Token.EqualTo(GoogleSqlToken.Outer).Value(true).OptionalOrDefault(false)
				select JoinType.Right)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Full)
				from __ in Token.EqualTo(GoogleSqlToken.Outer).Value(true).OptionalOrDefault(false)
				select JoinType.Full)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Cross) select JoinType.Cross)
			.OptionalOrDefault(JoinType.Inner)
		from _ in Token.EqualTo(GoogleSqlToken.Join)
		from table in AnyIdentifier
		from alias in (from __ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
			.Or(AnyIdentifier)
			.AsNullable()
			.OptionalOrDefault()
		from onExpr in (from __ in Token.EqualTo(GoogleSqlToken.On) from expr in Expression select expr)
			.AsNullable()
			.OptionalOrDefault()
		select new JoinClause(joinType, table, alias, onExpr);

	public static TokenListParser<GoogleSqlToken, SelectStatement> SelectStatement { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Select)
		from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
		from columns in SelectColumnItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from fromClause in (
			from __ in Token.EqualTo(GoogleSqlToken.From)
			from source in
				// Subquery in FROM: FROM (SELECT ...) AS alias
				(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				 from sub in Parse.Ref(() => SelectStatement!)
				 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				 from alias in (from ___ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
					.Or(AnyIdentifier)
				 from joins in JoinItem.Many()
				 select (FromClause)new SubqueryFromClause(sub, alias, joins.Length > 0 ? joins.ToList() : null)).Try()
				// UNNEST(array_expression) [AS alias] [WITH OFFSET [AS offset_alias]]
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#unnest_operator
				.Or(from ___ in Token.EqualTo(GoogleSqlToken.Unnest)
					from open in Token.EqualTo(GoogleSqlToken.OpenParen)
					from arrExpr in ExpressionRef
					from close in Token.EqualTo(GoogleSqlToken.CloseParen)
					from alias in (from ____ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
						.Or(AnyIdentifier)
						.AsNullable()
						.OptionalOrDefault()
					from withOffset in (
						from ____ in Token.EqualTo(GoogleSqlToken.With)
						from _____ in Token.EqualTo(GoogleSqlToken.Offset)
						from offsetAlias in (from ______ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
							.Or(AnyIdentifier)
							.AsNullable()
							.OptionalOrDefault()
						select (WithOffset: true, OffsetAlias: offsetAlias)
					).OptionalOrDefault((WithOffset: false, OffsetAlias: (string?)null))
					from joins in JoinItem.Many()
					select (FromClause)new UnnestFromClause(arrExpr, alias, withOffset.WithOffset, withOffset.OffsetAlias, joins.Length > 0 ? joins.ToList() : null)).Try()
				// Regular table reference (with optional schema prefix like INFORMATION_SCHEMA.TABLES)
				.Or(from schema in (from s in AnyIdentifier
						from dot in Token.EqualTo(GoogleSqlToken.Dot)
						select s + ".").Try().OptionalOrDefault("")
					from table in AnyIdentifier
					from alias in (from ___ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
						.Or(AnyIdentifier)
						.AsNullable()
						.OptionalOrDefault()
					from joins in JoinItem.Many()
					select new FromClause(schema + table, alias, joins.Length > 0 ? joins.ToList() : null))
			select source
		).AsNullable().OptionalOrDefault()
		from whereExpr in (from __ in Token.EqualTo(GoogleSqlToken.Where) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		from groupBy in (
			from __ in Token.EqualTo(GoogleSqlToken.Group) from ___ in Token.EqualTo(GoogleSqlToken.By)
			from exprs in Expression.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select exprs.ToList()
		).AsNullable().OptionalOrDefault()
		from having in (from __ in Token.EqualTo(GoogleSqlToken.Having) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		from orderBy in (
			from __ in Token.EqualTo(GoogleSqlToken.Order) from ___ in Token.EqualTo(GoogleSqlToken.By)
			from items in OrderByItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select items.ToList()
		).AsNullable().OptionalOrDefault()
		from limit in (from __ in Token.EqualTo(GoogleSqlToken.Limit) from n in Token.EqualTo(GoogleSqlToken.Number) select long.Parse(n.ToStringValue()))
			.Select(x => (long?)x).OptionalOrDefault()
		from offset in (from __ in Token.EqualTo(GoogleSqlToken.Offset) from n in Token.EqualTo(GoogleSqlToken.Number) select long.Parse(n.ToStringValue()))
			.Select(x => (long?)x).OptionalOrDefault()
		select new SelectStatement(distinct, columns.ToList(), fromClause, whereExpr, groupBy, having, orderBy, limit, offset);

	// ──────────────────────────────────────────
	// CTE + Set Operations → FullQuery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#with_clause
	// ──────────────────────────────────────────

	private static TokenListParser<GoogleSqlToken, CteDefinition> CteItem { get; } =
		from name in AnyIdentifier
		from _ in Token.EqualTo(GoogleSqlToken.As)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from query in Parse.Ref(() => SelectStatement!)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new CteDefinition(name, query);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	private static TokenListParser<GoogleSqlToken, SetOperation> SetOperationItem { get; } =
		from opType in
			(from _ in Token.EqualTo(GoogleSqlToken.Union) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.UnionAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Union) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.UnionDistinct)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Intersect) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.IntersectAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Intersect) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.IntersectDistinct)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Except) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.ExceptAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Except) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.ExceptDistinct)
		from right in SelectStatement
		select new SetOperation(opType, right);

	public static TokenListParser<GoogleSqlToken, FullQuery> FullQueryParser { get; } =
		from ctes in (
			from _ in Token.EqualTo(GoogleSqlToken.With)
			from items in CteItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select items.ToList()
		).AsNullable().OptionalOrDefault()
		from sel in SelectStatement
		from setOps in SetOperationItem.Many()
		select new FullQuery(ctes, sel, setOps.Length > 0 ? setOps.ToList() : null);

	// ──────────────────────────────────────────
	// DML Statements
	// ──────────────────────────────────────────

	// INSERT INTO table (col, ...) VALUES (val, ...), ...
	// INSERT [OR UPDATE | OR IGNORE] INTO table (col, ...) VALUES (...) | SELECT ...
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	public static TokenListParser<GoogleSqlToken, InsertStatement> InsertStatement { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Insert)
		from mode in
			(from _or in Token.EqualTo(GoogleSqlToken.Or)
			 from m in
				Token.EqualTo(GoogleSqlToken.Update).Value(InsertMode.InsertOrUpdate)
				.Or(Token.EqualTo(GoogleSqlToken.Ignore).Value(InsertMode.InsertOrIgnore))
			 select m).OptionalOrDefault(InsertMode.Insert)
		from __ in Token.EqualTo(GoogleSqlToken.Into)
		from table in AnyIdentifier
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from columns in AnyIdentifier.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from valuesOrSelect in
			// VALUES (...)
			(from ___ in Token.EqualTo(GoogleSqlToken.Values)
			 from valueRows in (
				from vo in Token.EqualTo(GoogleSqlToken.OpenParen)
				from vals in Expression.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
				from vc in Token.EqualTo(GoogleSqlToken.CloseParen)
				select vals.ToList()
			 ).ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			 select (List<List<SqlExpression>>?)valueRows.ToList() as object)
			// SELECT ...
			.Or(SelectStatement.Select(x => (object)x))
		select valuesOrSelect switch
		{
			List<List<SqlExpression>> vr => new InsertStatement(table, columns.ToList(), vr, mode),
			SelectStatement sel => new InsertStatement(table, columns.ToList(), null, mode, sel),
			_ => throw new InvalidOperationException("Unexpected INSERT source type.")
		};

	// UPDATE table SET col = expr, ... WHERE ...
	public static TokenListParser<GoogleSqlToken, UpdateStatement> UpdateStatement { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Update)
		from table in AnyIdentifier
		from __ in Token.EqualTo(GoogleSqlToken.Set)
		from sets in (
			from col in AnyIdentifier
			from ___ in Token.EqualTo(GoogleSqlToken.Equal)
			from val in Expression
			select new SetClause(col, val)
		).ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from whereExpr in (from ___ in Token.EqualTo(GoogleSqlToken.Where) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		select new UpdateStatement(table, sets.ToList(), whereExpr);

	// DELETE FROM table WHERE ...
	public static TokenListParser<GoogleSqlToken, DeleteStatement> DeleteStatement { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Delete)
		from __ in Token.EqualTo(GoogleSqlToken.From)
		from table in AnyIdentifier
		from whereExpr in (from ___ in Token.EqualTo(GoogleSqlToken.Where) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		select new DeleteStatement(table, whereExpr);

	// ──────────────────────────────────────────
	// Top-level SQL dispatcher
	// ──────────────────────────────────────────

	public static TokenListParser<GoogleSqlToken, object> SqlStatement { get; } =
		FullQueryParser.Select(x => (object)x)
		.Or(InsertStatement.Select(x => (object)x))
		.Or(UpdateStatement.Select(x => (object)x))
		.Or(DeleteStatement.Select(x => (object)x));
}
