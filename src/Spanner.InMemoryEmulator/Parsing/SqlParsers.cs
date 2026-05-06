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
			if (text.Contains('.') || text.Contains('e', StringComparison.OrdinalIgnoreCase))
				return (SqlExpression)new LiteralExpr(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
			if (long.TryParse(text, out var l))
				return (SqlExpression)new LiteralExpr(l);
			// Overflow — parse as double (e.g. 9223372036854775808 used with unary minus)
			return (SqlExpression)new LiteralExpr(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
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

	// Byte literal: b'...' — parsed as a byte array
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
	private static TokenListParser<GoogleSqlToken, SqlExpression> ByteLiteral { get; } =
		Token.EqualTo(GoogleSqlToken.ByteLiteral).Select(t =>
		{
			var raw = t.ToStringValue(); // e.g. b'\xff' or b'abc'
			var content = raw[2..^1]; // remove b' and trailing '
			return (SqlExpression)new LiteralExpr(ParseByteString(content));
		});

	private static byte[] ParseByteString(string content)
	{
		var bytes = new List<byte>();
		for (var i = 0; i < content.Length; i++)
		{
			if (content[i] == '\\' && i + 1 < content.Length)
			{
				if (content[i + 1] == 'x' && i + 3 < content.Length)
				{
					bytes.Add(Convert.ToByte(content.Substring(i + 2, 2), 16));
					i += 3;
				}
				else
				{
					bytes.Add((byte)content[i + 1]);
					i++;
				}
			}
			else
			{
				bytes.Add((byte)content[i]);
			}
		}
		return bytes.ToArray();
	}

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
		.Or(Token.EqualTo(GoogleSqlToken.Right).Select(t => t.ToStringValue()))
		// Allow aggregate keywords as identifiers (e.g., column aliases, column names)
		.Or(Token.EqualTo(GoogleSqlToken.Avg).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Sum).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Min).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Max).Select(t => t.ToStringValue()))
		// Other keywords used as column/table names
		.Or(Token.EqualTo(GoogleSqlToken.Parent).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Row).Select(t => t.ToStringValue()))
		// Type keywords used as function names (JSON conversion: BOOL, INT64, FLOAT64, STRING)
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#bool
		.Or(Token.EqualTo(GoogleSqlToken.BoolType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Int64Type).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Float64Type).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Float32Type).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.StringType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.BytesType).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.NumericType).Select(t => t.ToStringValue()))
		// Allow new keyword tokens as identifiers (used as date parts, column names, etc.)
		.Or(Token.EqualTo(GoogleSqlToken.Day).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Interval).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Deletion).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Policy).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.OlderThan).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Percent).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Hidden).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Search).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Tokenlist).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Range).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Nulls).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Respect).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Excluded).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Conflict).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Do).Select(t => t.ToStringValue()))
		.Or(Token.EqualTo(GoogleSqlToken.Nothing).Select(t => t.ToStringValue()));

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls#def_window_frame
	private static TokenListParser<GoogleSqlToken, WindowFrame> FrameBound { get; } =
		// .Try() needed on alternatives that share a common prefix (UNBOUNDED, Number)
		(from _ in Token.EqualTo(GoogleSqlToken.Unbounded)
		 from __ in Token.EqualTo(GoogleSqlToken.Preceding)
		 select new WindowFrame(FrameBoundType.UnboundedPreceding, 0)).Try()
		.Or(from _ in Token.EqualTo(GoogleSqlToken.Unbounded)
			from __ in Token.EqualTo(GoogleSqlToken.Following)
			select new WindowFrame(FrameBoundType.UnboundedFollowing, 0))
		.Or(from _ in Token.EqualTo(GoogleSqlToken.Current)
			from __ in Token.EqualTo(GoogleSqlToken.Row)
			select new WindowFrame(FrameBoundType.CurrentRow, 0))
		.Or((from n in Token.EqualTo(GoogleSqlToken.Number)
			from _ in Token.EqualTo(GoogleSqlToken.Preceding)
			select new WindowFrame(FrameBoundType.OffsetPreceding, long.Parse(n.ToStringValue()))).Try())
		.Or(from n in Token.EqualTo(GoogleSqlToken.Number)
			from _ in Token.EqualTo(GoogleSqlToken.Following)
			select new WindowFrame(FrameBoundType.OffsetFollowing, long.Parse(n.ToStringValue())));

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls#def_window_frame
	// ROWS BETWEEN and RANGE BETWEEN both use the same frame bound syntax
	private static TokenListParser<GoogleSqlToken, WindowFrameClause> FrameClause { get; } =
		from frameType in Token.EqualTo(GoogleSqlToken.Rows).Value(true)
			.Or(Token.EqualTo(GoogleSqlToken.Range).Value(true))
		from __ in Token.EqualTo(GoogleSqlToken.Between)
		from start in FrameBound
		from ___ in Token.EqualTo(GoogleSqlToken.And)
		from end in FrameBound
		select new WindowFrameClause(start, end);

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
		// ROWS BETWEEN ... AND ... (parse and store for frame-aware evaluation)
		from frame in FrameClause.AsNullable().OptionalOrDefault()
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new WindowExpr(null!, partitionBy, orderBy, frame); // Function filled in by caller

	// Named argument parser: identifier => expression (e.g. dialect => 'words', min_ngrams => 2)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
	//   Many search functions accept named arguments like: SEARCH(tokens, query, dialect => 'words')
	private static TokenListParser<GoogleSqlToken, SqlExpression> NamedArgument { get; } =
		(from argName in AnyIdentifier
		 from _ in Token.EqualTo(GoogleSqlToken.FatArrow)
		 from value in ExpressionRef
		 select (SqlExpression)new NamedArgExpr(argName, value)).Try();

	// Function argument: either a named argument or a positional expression
	private static TokenListParser<GoogleSqlToken, SqlExpression> FunctionArgument { get; } =
		NamedArgument.Or(ExpressionRef);

	private static TokenListParser<GoogleSqlToken, SqlExpression> ColumnRefOrFunction { get; } =
		from name in AnyIdentifier
		from result in
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#safe_prefix
			//   "SAFE.func(...) or namespace.func(...) — compound function names like NET.HOST, SAFE.DIVIDE"
			(from dot in Token.EqualTo(GoogleSqlToken.Dot)
			 from name2 in AnyIdentifier
			 from open in Token.EqualTo(GoogleSqlToken.OpenParen)
			 from args in FunctionArgument.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
			 select (SqlExpression)new FunctionCallExpr(name + "." + name2, args.ToList())).Try()
			// Function call: name(args...)
			// Supports optional IGNORE/RESPECT NULLS and ORDER BY for aggregate functions like ARRAY_AGG, STRING_AGG
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
			.Or(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
			 from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
			 from args in FunctionArgument.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			 // IGNORE NULLS or RESPECT NULLS
			 // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
			 from nullHandling in (
				from ign in Token.EqualTo(GoogleSqlToken.Ignore)
				from _ in Token.EqualTo(GoogleSqlToken.Nulls)
				select (bool?)false
			 ).Or(
				from resp in Token.EqualTo(GoogleSqlToken.Respect)
				from _ in Token.EqualTo(GoogleSqlToken.Nulls)
				select (bool?)true
			 ).OptionalOrDefault((bool?)null)
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
			 from window in OverClause.AsNullable().OptionalOrDefault()
			 select window != null
				? (SqlExpression)new WindowExpr(new FunctionCallExpr(name, args.ToList(), distinct, orderBy, nullHandling), window.PartitionBy, window.OrderBy, window.Frame)
				: (SqlExpression)new FunctionCallExpr(name, args.ToList(), distinct, orderBy, nullHandling))
			// Qualified star: alias.* (Try needed so dot is backtracked when next token is not *)
			.Or((from dot in Token.EqualTo(GoogleSqlToken.Dot)
				from _ in Token.EqualTo(GoogleSqlToken.Star)
				select (SqlExpression)new ColumnRefExpr(name, "*")).Try())
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
			? (SqlExpression)new WindowExpr(result, window.PartitionBy, window.OrderBy, window.Frame)
			: result;

	private static TokenListParser<GoogleSqlToken, SqlExpression> AggregateKeywordFunction(GoogleSqlToken keyword, string name) =>
		from _ in Token.EqualTo(keyword)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
		from arg in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		from window in OverClause.AsNullable().OptionalOrDefault()
		select window != null
			? (SqlExpression)new WindowExpr(new FunctionCallExpr(name, new List<SqlExpression> { arg }, distinct), window.PartitionBy, window.OrderBy, window.Frame)
			: (SqlExpression)new FunctionCallExpr(name, new List<SqlExpression> { arg }, distinct);

	// CAST(expr AS type) — supports bare type names without length specifiers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	private static TokenListParser<GoogleSqlToken, TypeCode> CastTargetType { get; } =
		// ARRAY<element_type> — treat as TypeCode.Array
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#array_type
		(from _ in Token.EqualTo(GoogleSqlToken.Array)
		 from open in Token.EqualTo(GoogleSqlToken.LessThan)
		 from elemType in Parse.Ref(() => CastTargetType!)
		 from close in Token.EqualTo(GoogleSqlToken.GreaterThan)
		 select TypeCode.Array)
		.Or(Token.EqualTo(GoogleSqlToken.Int64Type).Value(TypeCode.Int64))
		.Or(Token.EqualTo(GoogleSqlToken.Float64Type).Value(TypeCode.Float64))
		.Or(Token.EqualTo(GoogleSqlToken.Float32Type).Value(TypeCode.Float32))
		.Or(Token.EqualTo(GoogleSqlToken.BoolType).Value(TypeCode.Bool))
		.Or(Token.EqualTo(GoogleSqlToken.StringType).Value(TypeCode.String))
		.Or(Token.EqualTo(GoogleSqlToken.BytesType).Value(TypeCode.Bytes))
		.Or(Token.EqualTo(GoogleSqlToken.TimestampType).Value(TypeCode.Timestamp))
		.Or(Token.EqualTo(GoogleSqlToken.DateType).Value(TypeCode.Date))
		.Or(Token.EqualTo(GoogleSqlToken.NumericType).Value(TypeCode.Numeric))
		.Or(Token.EqualTo(GoogleSqlToken.JsonType).Value(TypeCode.Json))
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
		.Or(Token.EqualTo(GoogleSqlToken.UuidType).Value((TypeCode)17));

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

	// ── Date/Time typed literals and special syntax ──

	// DATE 'value' — typed date literal
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#date_literals
	private static TokenListParser<GoogleSqlToken, SqlExpression> DateTypedLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.DateType)
		from str in Token.EqualTo(GoogleSqlToken.StringLiteral)
		select (SqlExpression)new FunctionCallExpr("DATE", new List<SqlExpression>
			{ new LiteralExpr(str.ToStringValue()[1..^1].Replace("''", "'")) });

	// TIMESTAMP 'value' — typed timestamp literal
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#timestamp_literals
	private static TokenListParser<GoogleSqlToken, SqlExpression> TimestampTypedLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.TimestampType)
		from str in Token.EqualTo(GoogleSqlToken.StringLiteral)
		select (SqlExpression)new FunctionCallExpr("TIMESTAMP", new List<SqlExpression>
			{ new LiteralExpr(str.ToStringValue()[1..^1].Replace("''", "'")) });

	// JSON 'value' — typed JSON literal
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#json_literals
	private static TokenListParser<GoogleSqlToken, SqlExpression> JsonTypedLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.JsonType)
		from str in Token.EqualTo(GoogleSqlToken.StringLiteral)
		select (SqlExpression)new LiteralExpr(str.ToStringValue()[1..^1].Replace("''", "'"));

	// NUMERIC 'value' — typed numeric literal (equivalent to CAST('value' AS NUMERIC))
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#numeric_literals
	private static TokenListParser<GoogleSqlToken, SqlExpression> NumericTypedLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.NumericType)
		from str in Token.EqualTo(GoogleSqlToken.StringLiteral)
		select (SqlExpression)new CastExpr(new LiteralExpr(str.ToStringValue()[1..^1].Replace("''", "'")), TypeCode.Numeric);

	// EXTRACT(part FROM expr)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	private static TokenListParser<GoogleSqlToken, SqlExpression> ExtractFunction { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Extract)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from part in AnyIdentifier
		from __ in Token.EqualTo(GoogleSqlToken.From)
		from expr in ExpressionRef
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr("EXTRACT", new List<SqlExpression>
			{ new LiteralExpr(part.ToUpperInvariant()), expr });

	// Helper: parse a bare date-part identifier as a string literal
	// Used in DATE_DIFF, DATE_TRUNC, TIMESTAMP_DIFF, TIMESTAMP_TRUNC
	private static TokenListParser<GoogleSqlToken, SqlExpression> DatePartKeyword { get; } =
		AnyIdentifier.Select(name => (SqlExpression)new LiteralExpr(name.ToUpperInvariant()));

	// DATE_ADD/DATE_SUB/TIMESTAMP_ADD/TIMESTAMP_SUB with INTERVAL syntax
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#adddate
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#subdate
	private static readonly HashSet<string> IntervalFunctionNames = new(StringComparer.OrdinalIgnoreCase)
		{ "DATE_ADD", "DATE_SUB", "TIMESTAMP_ADD", "TIMESTAMP_SUB", "ADDDATE", "SUBDATE" };

	private static TokenListParser<GoogleSqlToken, SqlExpression> IntervalFunction { get; } =
		from name in Token.EqualTo(GoogleSqlToken.Identifier)
			.Where(t => IntervalFunctionNames.Contains(t.ToStringValue()))
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from expr in ExpressionRef
		from c in Token.EqualTo(GoogleSqlToken.Comma)
		from __ in Token.EqualTo(GoogleSqlToken.Interval)
		from amount in ExpressionRef
		from part in DatePartKeyword
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr(name.ToStringValue().ToUpperInvariant(),
			new List<SqlExpression> { expr, amount, part });

	// Standalone INTERVAL literal expression: INTERVAL expr PART
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
	//   "INTERVAL int64_expression datetime_part"
	// Produces an internal __INTERVAL__ function call node evaluated in ExpressionEvaluator.
	private static TokenListParser<GoogleSqlToken, SqlExpression> IntervalLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Interval)
		from amount in ExpressionRef
		from part in DatePartKeyword
		select (SqlExpression)new FunctionCallExpr("__INTERVAL__",
			new List<SqlExpression> { amount, part });

	// DATE_DIFF/TIMESTAMP_DIFF(expr, expr, part)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	private static readonly HashSet<string> DiffFunctionNames = new(StringComparer.OrdinalIgnoreCase)
		{ "DATE_DIFF", "TIMESTAMP_DIFF" };

	private static TokenListParser<GoogleSqlToken, SqlExpression> DiffFunction { get; } =
		from name in Token.EqualTo(GoogleSqlToken.Identifier)
			.Where(t => DiffFunctionNames.Contains(t.ToStringValue()))
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from expr1 in ExpressionRef
		from c1 in Token.EqualTo(GoogleSqlToken.Comma)
		from expr2 in ExpressionRef
		from c2 in Token.EqualTo(GoogleSqlToken.Comma)
		from part in DatePartKeyword
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr(name.ToStringValue().ToUpperInvariant(),
			new List<SqlExpression> { expr1, expr2, part });

	// DATE_TRUNC/TIMESTAMP_TRUNC(expr, part)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	private static readonly HashSet<string> TruncFunctionNames = new(StringComparer.OrdinalIgnoreCase)
		{ "DATE_TRUNC", "TIMESTAMP_TRUNC" };

	private static TokenListParser<GoogleSqlToken, SqlExpression> TruncFunction { get; } =
		from name in Token.EqualTo(GoogleSqlToken.Identifier)
			.Where(t => TruncFunctionNames.Contains(t.ToStringValue()))
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from expr in ExpressionRef
		from c in Token.EqualTo(GoogleSqlToken.Comma)
		from part in DatePartKeyword
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new FunctionCallExpr(name.ToStringValue().ToUpperInvariant(),
			new List<SqlExpression> { expr, part });

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
			(from sub in Parse.Ref(() => QueryBodyParser!)
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
		from sub in Parse.Ref(() => QueryBodyParser!)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select (SqlExpression)new ExistsExpr(sub, false);

	// ARRAY(SELECT ...)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery_concepts
	private static TokenListParser<GoogleSqlToken, SqlExpression> ArraySubquery { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Array)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from sub in Parse.Ref(() => QueryBodyParser!)
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

	// Typed array literal: ARRAY<type>[expr, expr, ...]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#array-literals
	//   "ARRAY<INT64>[] is an empty array of INT64 type."
	private static TokenListParser<GoogleSqlToken, SqlExpression> TypedArrayLiteral { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Array)
		from open in Token.EqualTo(GoogleSqlToken.LessThan)
		from elemType in Parse.Ref(() => CastTargetType!)
		from close in Token.EqualTo(GoogleSqlToken.GreaterThan)
		from openBracket in Token.EqualTo(GoogleSqlToken.OpenBracket)
		from elements in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from closeBracket in Token.EqualTo(GoogleSqlToken.CloseBracket)
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
		ExtractFunction
		.Or(DateTypedLiteral.Try())
		.Or(TimestampTypedLiteral.Try())
		.Or(JsonTypedLiteral.Try())
		.Or(NumericTypedLiteral.Try())
		.Or(IntervalLiteral.Try())
		.Or(IntervalFunction.Try())
		.Or(DiffFunction.Try())
		.Or(TruncFunction.Try())
		.Or(CastFunction)
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
		.Or(TypedArrayLiteral.Try())
		.Or(ArraySubquery.Try())
		.Or(ArrayLiteral)
		.Or(CaseExpression)
		.Or(SequenceRef)
		.Or(BoolLiteral)
		.Or(NullLiteral)
		.Or(ByteLiteral)
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
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#struct_field_access_operator
				//   STRUCT(...).*  — dot star expands all struct fields
				//   STRUCT(...).field — dot field access on a struct
				.Or((from dot in Token.EqualTo(GoogleSqlToken.Dot)
					from _ in Token.EqualTo(GoogleSqlToken.Star)
					select (SqlExpression)new StructExpandExpr(atom)).Try())
				.Or(from dot in Token.EqualTo(GoogleSqlToken.Dot)
					from field in AnyIdentifier
					select (SqlExpression)new StructFieldAccessExpr(atom, field))
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
			 select (Func<SqlExpression, SqlExpression>)(l => new IsNullExpr(l, not))).Try()
			// IS [NOT] TRUE / IS [NOT] FALSE
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Is)
				from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from boolVal in Token.EqualTo(GoogleSqlToken.True).Value(true)
					.Or(Token.EqualTo(GoogleSqlToken.False).Value(false))
				select (Func<SqlExpression, SqlExpression>)(l =>
				{
					// IS TRUE → l = TRUE, IS NOT TRUE → NOT(l = TRUE)
					// IS FALSE → l = FALSE, IS NOT FALSE → NOT(l = FALSE)
					SqlExpression cmp = new BinaryExpr(l, BinaryOp.Equal, new LiteralExpr(boolVal));
					// Also handle NULL: IS TRUE on NULL = FALSE, IS NOT TRUE on NULL = TRUE
					SqlExpression isNullCheck = new IsNullExpr(l, false);
					SqlExpression result;
					if (boolVal && !not) // IS TRUE
						result = new BinaryExpr(new UnaryExpr(UnaryOp.Not, isNullCheck), BinaryOp.And, cmp);
					else if (!boolVal && !not) // IS FALSE
						result = new BinaryExpr(new UnaryExpr(UnaryOp.Not, isNullCheck), BinaryOp.And, cmp);
					else if (boolVal && not) // IS NOT TRUE → IS NULL OR IS FALSE
						result = new BinaryExpr(isNullCheck, BinaryOp.Or,
							new BinaryExpr(l, BinaryOp.Equal, new LiteralExpr(false)));
					else // IS NOT FALSE → IS NULL OR IS TRUE
						result = new BinaryExpr(isNullCheck, BinaryOp.Or,
							new BinaryExpr(l, BinaryOp.Equal, new LiteralExpr(true)));
					return result;
				}))
			// [NOT] IN UNNEST(array_expression)
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
			.Or((from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.In)
				from __ in Token.EqualTo(GoogleSqlToken.Unnest)
				from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				from arrExpr in ExpressionRef
				from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				select (Func<SqlExpression, SqlExpression>)(l =>
					new InUnnestExpr(l, arrExpr, not))).Try())
			// [NOT] IN (SELECT ...) or [NOT] IN (list)
			.Or((from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.In)
				from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				from inBody in
					(from sub in Parse.Ref(() => QueryBodyParser!)
					 select (object)sub).Try()
					.Or(from list in ExpressionRef.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
						select (object)list.ToList())
				from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				select (Func<SqlExpression, SqlExpression>)(l =>
					inBody is QueryBody subq
						? new InSubqueryExpr(l, subq, not)
						: new InExpr(l, (List<SqlExpression>)inBody, not))).Try())
			// [NOT] BETWEEN low AND high
			.Or((from not in Token.EqualTo(GoogleSqlToken.Not).Value(true).OptionalOrDefault(false)
				from _ in Token.EqualTo(GoogleSqlToken.Between)
				from low in Additive
				from __ in Token.EqualTo(GoogleSqlToken.And)
				from high in Additive
				select (Func<SqlExpression, SqlExpression>)(l => new BetweenExpr(l, low, high, not))).Try())
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

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#limit_and_offset_clause
	//   "LIMIT and OFFSET accept integer literal or parameter values."
	private static TokenListParser<GoogleSqlToken, SqlExpression> LimitValue { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Limit)
		from expr in Token.EqualTo(GoogleSqlToken.Number).Select(t => (SqlExpression)new LiteralExpr(long.Parse(t.ToStringValue())))
			.Or(Parameter)
		select expr;

	private static TokenListParser<GoogleSqlToken, SqlExpression> OffsetValue { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Offset)
		from expr in Token.EqualTo(GoogleSqlToken.Number).Select(t => (SqlExpression)new LiteralExpr(long.Parse(t.ToStringValue())))
			.Or(Parameter)
		select expr;

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
		from result in
			// UNNEST join: [CROSS] JOIN UNNEST(expr) [AS] alias [WITH OFFSET [AS] offset_alias]
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#unnest_operator
			(from _u in Token.EqualTo(GoogleSqlToken.Unnest)
			 from open in Token.EqualTo(GoogleSqlToken.OpenParen)
			 from arrExpr in ExpressionRef
			 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
			 from alias in (from __ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
				.Or(AnyIdentifier).AsNullable().OptionalOrDefault()
			 from withOff in (
				from _w in Token.EqualTo(GoogleSqlToken.With)
				from _o in Token.EqualTo(GoogleSqlToken.Offset)
				from offAlias in (from __ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
					.AsNullable().OptionalOrDefault()
				select (WithOffset: true, OffsetAlias: offAlias)
			 ).OptionalOrDefault((WithOffset: false, OffsetAlias: (string?)null))
			 select new JoinClause(joinType, "__unnest__", alias, null, null, null,
				arrExpr, withOff.WithOffset, withOff.OffsetAlias)).Try()
			// Subquery or table join
			.Or(from source in
				// Subquery join: JOIN (SELECT ...) alias ON ...
				(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				 from sub in Parse.Ref(() => QueryBodyParser!)
				 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				 from subAlias in (from __ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
					.Or(AnyIdentifier)
				 select (Table: subAlias, Alias: (string?)subAlias, Subquery: (QueryBody?)sub)).Try()
				// Regular table reference
				.Or(from table in AnyIdentifier
					from tableAlias in (from __ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
						.Or(AnyIdentifier)
						.AsNullable()
						.OptionalOrDefault()
					select (Table: table, Alias: tableAlias, Subquery: (QueryBody?)null))
			from condition in
				(from __ in Token.EqualTo(GoogleSqlToken.On) from expr in Expression
				 select (OnExpr: (SqlExpression?)expr, UsingCols: (List<string>?)null))
				.Or(from __ in Token.EqualTo(GoogleSqlToken.Using)
					from open in Token.EqualTo(GoogleSqlToken.OpenParen)
					from cols in AnyIdentifier.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
					from close in Token.EqualTo(GoogleSqlToken.CloseParen)
					select (OnExpr: (SqlExpression?)null, UsingCols: (List<string>?)cols.ToList()))
				.OptionalOrDefault((OnExpr: (SqlExpression?)null, UsingCols: (List<string>?)null))
			select new JoinClause(joinType, source.Table, source.Alias, condition.OnExpr, condition.UsingCols, source.Subquery))
		select result;

	public static TokenListParser<GoogleSqlToken, SelectStatement> SelectStatement { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Select)
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_as_struct
		//   "SELECT AS STRUCT produces a value table with a STRUCT row type."
		from asStruct in (from __ in Token.EqualTo(GoogleSqlToken.As)
			from ___ in Token.EqualTo(GoogleSqlToken.Struct)
			select true).Try().OptionalOrDefault(false)
		from distinct in Token.EqualTo(GoogleSqlToken.Distinct).Value(true).OptionalOrDefault(false)
		from columns in SelectColumnItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		from fromClause in (
			from __ in Token.EqualTo(GoogleSqlToken.From)
			from source in
				// Subquery in FROM: FROM (SELECT ...) [AS] alias
				(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
				 from sub in Parse.Ref(() => QueryBodyParser!)
				 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
				 from alias in (from ___ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
					.Or(AnyIdentifier)
					.AsNullable()
					.OptionalOrDefault()
				 from joins in JoinItem.Many()
				 select (FromClause)new SubqueryFromClause(sub, alias ?? "__subq__", joins.Length > 0 ? joins.ToList() : null)).Try()
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
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
					//   TABLESAMPLE { BERNOULLI | RESERVOIR } ( size { PERCENT | ROWS } )
					from tableSample in (
						from ____ in Token.EqualTo(GoogleSqlToken.Tablesample)
						from method in Token.EqualTo(GoogleSqlToken.Bernoulli).Value(TableSampleMethod.Bernoulli)
							.Or(Token.EqualTo(GoogleSqlToken.Reservoir).Value(TableSampleMethod.Reservoir))
						from open in Token.EqualTo(GoogleSqlToken.OpenParen)
						from size in Token.EqualTo(GoogleSqlToken.Number).Apply(Numerics.DecimalDouble)
						from unit in Token.EqualTo(GoogleSqlToken.Percent).Value(TableSampleUnit.Percent)
							.Or(Token.EqualTo(GoogleSqlToken.Rows).Value(TableSampleUnit.Rows))
						from close in Token.EqualTo(GoogleSqlToken.CloseParen)
						select new TableSampleClause(method, size, unit)
					).AsNullable().OptionalOrDefault()
					from joins in JoinItem.Many()
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#comma_cross_join
					//   "FROM table, UNNEST(array_col) AS alias" is an implicit CROSS JOIN with UNNEST
					//   "FROM table1, table2" is an implicit CROSS JOIN between tables
					from commaUnnests in (
						from comma in Token.EqualTo(GoogleSqlToken.Comma)
						from item in (
							// Comma-UNNEST
							from ____ in Token.EqualTo(GoogleSqlToken.Unnest)
							from open in Token.EqualTo(GoogleSqlToken.OpenParen)
							from arrExpr in ExpressionRef
							from close in Token.EqualTo(GoogleSqlToken.CloseParen)
							from unnAlias in (from _____ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
								.Or(AnyIdentifier)
								.AsNullable()
								.OptionalOrDefault()
							from withOffset in (
								from _____ in Token.EqualTo(GoogleSqlToken.With)
								from ______ in Token.EqualTo(GoogleSqlToken.Offset)
								from offsetAlias in (from _______ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
									.Or(AnyIdentifier)
									.AsNullable()
									.OptionalOrDefault()
								select (WithOffset: true, OffsetAlias: offsetAlias)
							).OptionalOrDefault((WithOffset: false, OffsetAlias: (string?)null))
							select new JoinClause(JoinType.Cross, "__unnest__", unnAlias, null,
								UnnestExpr: arrExpr, UnnestWithOffset: withOffset.WithOffset, UnnestOffsetAlias: withOffset.OffsetAlias)
						).Try().Or(
							// Comma-table: implicit CROSS JOIN
							from commaTable in AnyIdentifier
							from commaAlias in (from _____ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
								.Or(AnyIdentifier).Try()
								.AsNullable()
								.OptionalOrDefault()
							select new JoinClause(JoinType.Cross, commaTable, commaAlias, null)
						)
						select item
					).Many()
					select new FromClause(schema + table, alias,
						joins.Length > 0 || commaUnnests.Length > 0
							? joins.Concat(commaUnnests).ToList()
							: null,
						tableSample))
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
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#qualify_clause
		//   "QUALIFY filters the results of window functions."
		from qualify in (from __ in Token.EqualTo(GoogleSqlToken.Qualify) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		from orderBy in (
			from __ in Token.EqualTo(GoogleSqlToken.Order) from ___ in Token.EqualTo(GoogleSqlToken.By)
			from items in OrderByItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select items.ToList()
		).AsNullable().OptionalOrDefault()
		from limit in LimitValue.AsNullable().OptionalOrDefault()
		from offset in OffsetValue.AsNullable().OptionalOrDefault()
		select new SelectStatement(distinct, columns.ToList(), fromClause, whereExpr, groupBy, having, qualify, orderBy, limit, offset, asStruct);

	// ──────────────────────────────────────────
	// CTE + Set Operations → FullQuery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#with_clause
	// ──────────────────────────────────────────

	private static TokenListParser<GoogleSqlToken, CteDefinition> CteItem { get; } =
		from name in AnyIdentifier
		from _ in Token.EqualTo(GoogleSqlToken.As)
		from open in Token.EqualTo(GoogleSqlToken.OpenParen)
		from query in Parse.Ref(() => QueryBodyParser!)
		from close in Token.EqualTo(GoogleSqlToken.CloseParen)
		select new CteDefinition(name, query);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	private static TokenListParser<GoogleSqlToken, SetOperation> SetOperationItem { get; } =
		from opType in
			(from _ in Token.EqualTo(GoogleSqlToken.Union) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.UnionAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Union) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.UnionDistinct).Try()
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
			//   "UNION: The default behavior is DISTINCT."
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Union) select SetOperationType.UnionDistinct)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Intersect) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.IntersectAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Intersect) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.IntersectDistinct).Try()
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
			//   "INTERSECT: The default behavior is DISTINCT."
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Intersect) select SetOperationType.IntersectDistinct)
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Except) from __ in Token.EqualTo(GoogleSqlToken.All) select SetOperationType.ExceptAll).Try()
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Except) from __ in Token.EqualTo(GoogleSqlToken.Distinct) select SetOperationType.ExceptDistinct).Try()
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
			//   "EXCEPT: The default behavior is DISTINCT."
			.Or(from _ in Token.EqualTo(GoogleSqlToken.Except) select SetOperationType.ExceptDistinct)
		from right in SelectStatement
		select new SetOperation(opType, right);

	/// <summary>A SELECT with optional set operations (no CTEs). Used inside subqueries, CTEs, FROM clauses.</summary>
	public static TokenListParser<GoogleSqlToken, QueryBody> QueryBodyParser { get; } =
		from sel in SelectStatement
		from setOps in SetOperationItem.Many()
		select new QueryBody(sel, setOps.Length > 0 ? setOps.ToList() : null);

	public static TokenListParser<GoogleSqlToken, FullQuery> FullQueryParser { get; } =
		from ctes in (
			from _ in Token.EqualTo(GoogleSqlToken.With)
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#recursive_keyword
			from recursive in Token.EqualTo(GoogleSqlToken.Recursive).Value(true).OptionalOrDefault(false)
			from items in CteItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			select (items.ToList(), recursive)
		).OptionalOrDefault((null, false))
		from body in QueryBodyParser
		select new FullQuery(ctes.Item1, body, ctes.recursive);

	// ──────────────────────────────────────────
	// DML Statements
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#then_return
	//   "THEN RETURN [WITH ACTION [AS alias]] { select_all | expression [AS alias] } [, ...]"
	private static TokenListParser<GoogleSqlToken, ReturningClause> ReturningClause { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Then)
		from __ in Token.EqualTo(GoogleSqlToken.Return)
		from withAction in (
			from ___ in Token.EqualTo(GoogleSqlToken.With)
			from ____ in Token.EqualTo(GoogleSqlToken.Action)
			from alias in (from _____ in Token.EqualTo(GoogleSqlToken.As) from name in AnyIdentifier select name)
				.AsNullable()
				.OptionalOrDefault()
			select (HasAction: true, ActionAlias: alias)
		).OptionalOrDefault((HasAction: false, ActionAlias: (string?)null))
		from columns in SelectColumnItem.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
		select new ReturningClause(withAction.HasAction, withAction.ActionAlias, columns.ToList());

	// INSERT INTO table (col, ...) VALUES (val, ...), ...
	// INSERT [OR UPDATE | OR IGNORE] INTO table (col, ...) VALUES (...) | SELECT ...
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	private static TokenListParser<GoogleSqlToken, InsertStatement> InsertStatementBase { get; } =
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
			.Or(QueryBodyParser.Select(x => (object)x))
		select valuesOrSelect switch
		{
			List<List<SqlExpression>> vr => new InsertStatement(table, columns.ToList(), vr, mode),
			QueryBody qb => new InsertStatement(table, columns.ToList(), null, mode, qb),
			_ => throw new InvalidOperationException("Unexpected INSERT source type.")
		};

	public static TokenListParser<GoogleSqlToken, InsertStatement> InsertStatement { get; } =
		from baseInsert in InsertStatementBase
		from onConflict in OnConflictClauseParser!.AsNullable().OptionalOrDefault()
		from returning in ReturningClause.AsNullable().OptionalOrDefault()
		select baseInsert with { Returning = returning, OnConflict = onConflict };

	// ON CONFLICT [(columns)] DO NOTHING | DO UPDATE SET ... [WHERE ...]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
	private static TokenListParser<GoogleSqlToken, OnConflictClause> OnConflictClauseParser { get; } =
		from _on in Token.EqualTo(GoogleSqlToken.On)
		from _conflict in Token.EqualTo(GoogleSqlToken.Conflict)
		from target in
			// (column, ...)
			(from open in Token.EqualTo(GoogleSqlToken.OpenParen)
			 from cols in AnyIdentifier.ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
			 from close in Token.EqualTo(GoogleSqlToken.CloseParen)
			 select (Columns: (List<string>?)cols.ToList(), Constraint: (string?)null))
			// ON UNIQUE CONSTRAINT constraint_name
			.Or(from _onKw in Token.EqualTo(GoogleSqlToken.On)
				from _unique in Token.EqualTo(GoogleSqlToken.Unique)
				from _constraint in Token.EqualTo(GoogleSqlToken.Constraint)
				from name in AnyIdentifier
				select (Columns: (List<string>?)null, Constraint: (string?)name))
			.OptionalOrDefault((Columns: (List<string>?)null, Constraint: (string?)null))
		from action in
			(from _do in Token.EqualTo(GoogleSqlToken.Do)
			 from doAction in
				// DO NOTHING
				(from _nothing in Token.EqualTo(GoogleSqlToken.Nothing)
				 select (Action: OnConflictAction.DoNothing, Sets: (List<SetClause>?)null, Where: (SqlExpression?)null))
				// DO UPDATE SET col = expr, ... [WHERE ...]
				.Or(from _update in Token.EqualTo(GoogleSqlToken.Update)
					from _set in Token.EqualTo(GoogleSqlToken.Set)
					from sets in (
						from col in AnyIdentifier
						from _eq in Token.EqualTo(GoogleSqlToken.Equal)
						from val in Expression
						select new SetClause(col, val)
					).ManyDelimitedBy(Token.EqualTo(GoogleSqlToken.Comma))
					from whereExpr in (from _w in Token.EqualTo(GoogleSqlToken.Where) from expr in Expression select expr)
						.AsNullable().OptionalOrDefault()
					select (Action: OnConflictAction.DoUpdate, Sets: (List<SetClause>?)sets.ToList(), Where: (SqlExpression?)whereExpr))
			 select doAction)
		select new OnConflictClause(target.Columns, target.Constraint, action.Action, action.Sets, action.Where);

	// UPDATE table SET col = expr, ... WHERE ...
	private static TokenListParser<GoogleSqlToken, UpdateStatement> UpdateStatementBase { get; } =
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

	public static TokenListParser<GoogleSqlToken, UpdateStatement> UpdateStatement { get; } =
		from baseUpdate in UpdateStatementBase
		from returning in ReturningClause.AsNullable().OptionalOrDefault()
		select baseUpdate with { Returning = returning };

	// DELETE FROM table WHERE ...
	private static TokenListParser<GoogleSqlToken, DeleteStatement> DeleteStatementBase { get; } =
		from _ in Token.EqualTo(GoogleSqlToken.Delete)
		from __ in Token.EqualTo(GoogleSqlToken.From)
		from table in AnyIdentifier
		from whereExpr in (from ___ in Token.EqualTo(GoogleSqlToken.Where) from expr in Expression select expr)
			.AsNullable().OptionalOrDefault()
		select new DeleteStatement(table, whereExpr);

	public static TokenListParser<GoogleSqlToken, DeleteStatement> DeleteStatement { get; } =
		from baseDelete in DeleteStatementBase
		from returning in ReturningClause.AsNullable().OptionalOrDefault()
		select baseDelete with { Returning = returning };

	// ──────────────────────────────────────────
	// Top-level SQL dispatcher
	// ──────────────────────────────────────────

	public static TokenListParser<GoogleSqlToken, object> SqlStatement { get; } =
		FullQueryParser.Select(x => (object)x)
		.Or(InsertStatement.Select(x => (object)x))
		.Or(UpdateStatement.Select(x => (object)x))
		.Or(DeleteStatement.Select(x => (object)x));
}
