using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;
using Span = Superpower.Parsers.Span;

namespace Spanner.InMemoryEmulator.Parsing;

/// <summary>
/// Superpower tokenizer for GoogleSQL. Converts raw SQL text into a stream of
/// <see cref="GoogleSqlToken"/> tokens.
/// </summary>
internal static class GoogleSqlTokenizer
{
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical
	//   Defines the lexical structure of GoogleSQL.

	private static readonly Dictionary<string, GoogleSqlToken> Keywords = new(StringComparer.OrdinalIgnoreCase)
	{
		["SELECT"] = GoogleSqlToken.Select,
		["FROM"] = GoogleSqlToken.From,
		["WHERE"] = GoogleSqlToken.Where,
		["AND"] = GoogleSqlToken.And,
		["OR"] = GoogleSqlToken.Or,
		["NOT"] = GoogleSqlToken.Not,
		["AS"] = GoogleSqlToken.As,
		["IN"] = GoogleSqlToken.In,
		["BETWEEN"] = GoogleSqlToken.Between,
		["LIKE"] = GoogleSqlToken.Like,
		["ORDER"] = GoogleSqlToken.Order,
		["BY"] = GoogleSqlToken.By,
		["ASC"] = GoogleSqlToken.Asc,
		["DESC"] = GoogleSqlToken.Desc,
		["LIMIT"] = GoogleSqlToken.Limit,
		["OFFSET"] = GoogleSqlToken.Offset,
		["GROUP"] = GoogleSqlToken.Group,
		["HAVING"] = GoogleSqlToken.Having,
		["INSERT"] = GoogleSqlToken.Insert,
		["INTO"] = GoogleSqlToken.Into,
		["VALUES"] = GoogleSqlToken.Values,
		["UPDATE"] = GoogleSqlToken.Update,
		["SET"] = GoogleSqlToken.Set,
		["DELETE"] = GoogleSqlToken.Delete,
		["CREATE"] = GoogleSqlToken.Create,
		["DROP"] = GoogleSqlToken.Drop,
		["ALTER"] = GoogleSqlToken.Alter,
		["TABLE"] = GoogleSqlToken.Table,
		["INDEX"] = GoogleSqlToken.Index,
		["VIEW"] = GoogleSqlToken.View,
		["PRIMARY"] = GoogleSqlToken.Primary,
		["KEY"] = GoogleSqlToken.Key,
		["NULL"] = GoogleSqlToken.Null,
		["UNIQUE"] = GoogleSqlToken.Unique,
		["INTERLEAVE"] = GoogleSqlToken.Interleave,
		["PARENT"] = GoogleSqlToken.Parent,
		["ARRAY"] = GoogleSqlToken.Array,
		["STRUCT"] = GoogleSqlToken.Struct,
		["TRUE"] = GoogleSqlToken.True,
		["FALSE"] = GoogleSqlToken.False,
		["IS"] = GoogleSqlToken.Is,
		["JOIN"] = GoogleSqlToken.Join,
		["INNER"] = GoogleSqlToken.Inner,
		["LEFT"] = GoogleSqlToken.Left,
		["RIGHT"] = GoogleSqlToken.Right,
		["CROSS"] = GoogleSqlToken.Cross,
		["FULL"] = GoogleSqlToken.Full,
		["OUTER"] = GoogleSqlToken.Outer,
		["ON"] = GoogleSqlToken.On,
		["UNION"] = GoogleSqlToken.Union,
		["ALL"] = GoogleSqlToken.All,
		["EXCEPT"] = GoogleSqlToken.Except,
		["INTERSECT"] = GoogleSqlToken.Intersect,
		["EXISTS"] = GoogleSqlToken.Exists,
		["CASE"] = GoogleSqlToken.Case,
		["WHEN"] = GoogleSqlToken.When,
		["THEN"] = GoogleSqlToken.Then,
		["ELSE"] = GoogleSqlToken.Else,
		["END"] = GoogleSqlToken.End,
		["CAST"] = GoogleSqlToken.Cast,
		["SAFE_CAST"] = GoogleSqlToken.SafeCast,
		["EXTRACT"] = GoogleSqlToken.Extract,
		["IF"] = GoogleSqlToken.If,
		["COALESCE"] = GoogleSqlToken.Coalesce,
		["NULLIF"] = GoogleSqlToken.Nullif,
		["IFNULL"] = GoogleSqlToken.Ifnull,
		["UNNEST"] = GoogleSqlToken.Unnest,
		["WITH"] = GoogleSqlToken.With,
		["DISTINCT"] = GoogleSqlToken.Distinct,
		["IGNORE"] = GoogleSqlToken.Ignore,
		["CHECK"] = GoogleSqlToken.Check,
		["CONSTRAINT"] = GoogleSqlToken.Constraint,
		["FOREIGN"] = GoogleSqlToken.Foreign,
		["REFERENCES"] = GoogleSqlToken.References,
		["ENFORCED"] = GoogleSqlToken.Enforced,
		["SEQUENCE"] = GoogleSqlToken.Sequence,
		["COUNT"] = GoogleSqlToken.Count,
		["SUM"] = GoogleSqlToken.Sum,
		["AVG"] = GoogleSqlToken.Avg,
		["MIN"] = GoogleSqlToken.Min,
		["MAX"] = GoogleSqlToken.Max,
		["INT64"] = GoogleSqlToken.Int64Type,
		["FLOAT64"] = GoogleSqlToken.Float64Type,
		["FLOAT32"] = GoogleSqlToken.Float32Type,
		["BOOL"] = GoogleSqlToken.BoolType,
		["STRING"] = GoogleSqlToken.StringType,
		["BYTES"] = GoogleSqlToken.BytesType,
		["DATE"] = GoogleSqlToken.DateType,
		["TIMESTAMP"] = GoogleSqlToken.TimestampType,
		["NUMERIC"] = GoogleSqlToken.NumericType,
		["JSON"] = GoogleSqlToken.JsonType,
		["COLUMN"] = GoogleSqlToken.Column,
		["ADD"] = GoogleSqlToken.Add,
		["CASCADE"] = GoogleSqlToken.Cascade,
		["NO"] = GoogleSqlToken.No,
		["ACTION"] = GoogleSqlToken.Action,
		["OPTIONS"] = GoogleSqlToken.Options,
		["STORED"] = GoogleSqlToken.Stored,
		["GENERATED"] = GoogleSqlToken.Generated,
		["ALWAYS"] = GoogleSqlToken.Always,
		["DEFAULT"] = GoogleSqlToken.Default,
		["STORING"] = GoogleSqlToken.Storing,
		["NULL_FILTERED"] = GoogleSqlToken.NullFiltered,
		["REPLACE"] = GoogleSqlToken.Replace,
		["OVER"] = GoogleSqlToken.Over,
		["PARTITION"] = GoogleSqlToken.Partition,
		["ROWS"] = GoogleSqlToken.Rows,
		["RANGE"] = GoogleSqlToken.Range,
		["PRECEDING"] = GoogleSqlToken.Preceding,
		["FOLLOWING"] = GoogleSqlToken.Following,
		["UNBOUNDED"] = GoogleSqlToken.Unbounded,
		["CURRENT"] = GoogleSqlToken.Current,
	};

	// Note: Instance MUST be declared AFTER all parser properties to ensure
	// static initialization order is correct.

	private static Tokenizer<GoogleSqlToken> BuildTokenizer()
	{
		return new TokenizerBuilder<GoogleSqlToken>()
			// Whitespace — must be ignored explicitly
			.Ignore(Span.WhiteSpace)

			// Multi-char operators (must come before single-char)
			.Match(Span.EqualTo("!="), GoogleSqlToken.NotEqual)
			.Match(Span.EqualTo("<>"), GoogleSqlToken.LessGreater)
			.Match(Span.EqualTo("<="), GoogleSqlToken.LessThanOrEqual)
			.Match(Span.EqualTo(">="), GoogleSqlToken.GreaterThanOrEqual)
			.Match(Span.EqualTo("||"), GoogleSqlToken.DoublePipe)
			.Match(Span.EqualTo("&&"), GoogleSqlToken.DoubleAmpersand)

			// Single-char operators
			.Match(Character.EqualTo('+'), GoogleSqlToken.Plus)
			.Match(Character.EqualTo('-'), GoogleSqlToken.Minus)
			.Match(Character.EqualTo('*'), GoogleSqlToken.Star)
			.Match(Character.EqualTo('/'), GoogleSqlToken.Divide)
			.Match(Character.EqualTo('%'), GoogleSqlToken.Modulo)
			.Match(Character.EqualTo('='), GoogleSqlToken.Equal)
			.Match(Character.EqualTo('<'), GoogleSqlToken.LessThan)
			.Match(Character.EqualTo('>'), GoogleSqlToken.GreaterThan)

			// Punctuation
			.Match(Character.EqualTo('('), GoogleSqlToken.OpenParen)
			.Match(Character.EqualTo(')'), GoogleSqlToken.CloseParen)
			.Match(Character.EqualTo(','), GoogleSqlToken.Comma)
			.Match(Character.EqualTo('.'), GoogleSqlToken.Dot)
			.Match(Character.EqualTo(';'), GoogleSqlToken.Semicolon)
			.Match(Character.EqualTo('['), GoogleSqlToken.OpenBracket)
			.Match(Character.EqualTo(']'), GoogleSqlToken.CloseBracket)

			// Parameters: @name
			.Match(ParameterToken, GoogleSqlToken.Parameter)

			// String literals: 'text' (with '' escape)
			.Match(StringLiteralToken, GoogleSqlToken.StringLiteral)

			// Quoted identifiers: `name`
			.Match(QuotedIdentifierToken, GoogleSqlToken.QuotedIdentifier)

			// Numbers: integers and decimals
			.Match(NumberToken, GoogleSqlToken.Number)

			// Identifiers and keywords
			.Match(IdentifierOrKeyword, GoogleSqlToken.Identifier)

			.Build();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
	//   "Quoted strings can contain escaped single quotes ('')."
	private static TextParser<string> StringLiteralToken { get; } =
		from open in Character.EqualTo('\'')
		from content in Span.EqualTo("''").Value('\'').Try()
			.Or(Character.Except('\''))
			.Many()
		from close in Character.EqualTo('\'')
		select new string(content);

	// Backtick-quoted identifiers
	private static TextParser<string> QuotedIdentifierToken { get; } =
		from open in Character.EqualTo('`')
		from content in Character.Except('`').Many()
		from close in Character.EqualTo('`')
		select new string(content);

	// Parameters: @paramName
	private static TextParser<string> ParameterToken { get; } =
		from at in Character.EqualTo('@')
		from name in Character.LetterOrDigit.Or(Character.EqualTo('_')).AtLeastOnce()
		select "@" + new string(name);

	// Numbers: integer or decimal
	private static TextParser<TextSpan> NumberToken { get; } =
		Numerics.Decimal;

	// Identifiers (including those containing underscores) — keywords detected post-match
	private static TextParser<string> IdentifierOrKeyword { get; } =
		from first in Character.Letter.Or(Character.EqualTo('_'))
		from rest in Character.LetterOrDigit.Or(Character.EqualTo('_')).Many()
		select first + new string(rest);

	// Must be after all parser properties to ensure correct static initialization order
	public static Tokenizer<GoogleSqlToken> Instance { get; } = BuildTokenizer();

	/// <summary>
	/// Tokenizes SQL and resolves identifier tokens that match known keywords.
	/// </summary>
	public static TokenList<GoogleSqlToken> Tokenize(string sql)
	{
		var tokens = Instance.Tokenize(sql);
		return ResolveKeywords(tokens);
	}

	private static TokenList<GoogleSqlToken> ResolveKeywords(TokenList<GoogleSqlToken> tokens)
	{
		var resolved = new List<Token<GoogleSqlToken>>();
		foreach (var token in tokens)
		{
			if (token.Kind == GoogleSqlToken.Identifier)
			{
				var text = token.ToStringValue();
				if (Keywords.TryGetValue(text, out var keyword))
				{
					resolved.Add(new Token<GoogleSqlToken>(keyword, token.Span));
				}
				else
				{
					resolved.Add(token);
				}
			}
			else
			{
				resolved.Add(token);
			}
		}
		return new TokenList<GoogleSqlToken>(resolved.ToArray());
	}
}
