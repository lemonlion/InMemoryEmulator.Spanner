using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended string function tests covering NULL propagation, unicode handling,
/// complex patterns, and edge cases not already in StringExhaustiveIntegrationTests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionExtendedIntegrationTests : IntegrationTestBase
{
	public StringFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── STARTS_WITH (extended) ──────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STARTS_WITH('hello world', 'hello w')", true)]
	[InlineData("STARTS_WITH('abc', 'abcd')", false)]
	[InlineData("STARTS_WITH('abc', 'ABC')", false)]
	[InlineData("STARTS_WITH('  hello', '  ')", true)]
	[InlineData("STARTS_WITH('a', 'a')", true)]
	[InlineData("STARTS_WITH('hello', 'hello world')", false)]
	[InlineData("STARTS_WITH('abc123', 'abc')", true)]
	[InlineData("STARTS_WITH('abc123', '123')", false)]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task StartsWith_Extended(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task StartsWith_NullValue_ReturnsNull()
	{
		var result = await Eval("STARTS_WITH(CAST(NULL AS STRING), 'x')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task StartsWith_NullPrefix_ReturnsNull()
	{
		var result = await Eval("STARTS_WITH('hello', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task StartsWith_BothNull_ReturnsNull()
	{
		var result = await Eval("STARTS_WITH(CAST(NULL AS STRING), CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── ENDS_WITH (extended) ────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ends_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ENDS_WITH('hello world', 'o world')", true)]
	[InlineData("ENDS_WITH('abc', 'xabc')", false)]
	[InlineData("ENDS_WITH('abc', 'ABC')", false)]
	[InlineData("ENDS_WITH('hello  ', '  ')", true)]
	[InlineData("ENDS_WITH('a', 'a')", true)]
	[InlineData("ENDS_WITH('hello', 'hello world')", false)]
	[InlineData("ENDS_WITH('abc123', '123')", true)]
	[InlineData("ENDS_WITH('abc123', 'abc')", false)]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task EndsWith_Extended(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task EndsWith_NullValue_ReturnsNull()
	{
		var result = await Eval("ENDS_WITH(CAST(NULL AS STRING), 'x')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task EndsWith_NullSuffix_ReturnsNull()
	{
		var result = await Eval("ENDS_WITH('hello', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task EndsWith_BothNull_ReturnsNull()
	{
		var result = await Eval("ENDS_WITH(CAST(NULL AS STRING), CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REGEXP_CONTAINS (extended) ──────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_CONTAINS('hello', 'h(e|a)llo')", true)]
	[InlineData("REGEXP_CONTAINS('hallo', 'h(e|a)llo')", true)]
	[InlineData("REGEXP_CONTAINS('hullo', 'h(e|a)llo')", false)]
	[InlineData("REGEXP_CONTAINS('aab', 'a{2}b')", true)]
	[InlineData("REGEXP_CONTAINS('ab', 'a{2}b')", false)]
	[InlineData("REGEXP_CONTAINS('123-456-7890', '[0-9]{3}-[0-9]{3}-[0-9]{4}')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^abc$')", true)]
	[InlineData("REGEXP_CONTAINS('abc def', '^abc$')", false)]
	[InlineData("REGEXP_CONTAINS('', '^$')", true)]
	[InlineData("REGEXP_CONTAINS('test', 'te?st')", true)]
	[InlineData("REGEXP_CONTAINS('tst', 'te?st')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'a|b|c')", true)]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpContains_Extended(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpContains_NullValue_ReturnsNull()
	{
		var result = await Eval("REGEXP_CONTAINS(CAST(NULL AS STRING), 'x')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpContains_NullPattern_ReturnsNull()
	{
		var result = await Eval("REGEXP_CONTAINS('hello', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REGEXP_EXTRACT (extended) ───────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('hello world', '(world)')", "world")]
	[InlineData("REGEXP_EXTRACT('no_digits_here', '[0-9]+')", null)]
	[InlineData("REGEXP_EXTRACT('', '[0-9]+')", null)]
	[InlineData("REGEXP_EXTRACT('abc', '(.)')", "a")]
	[InlineData("REGEXP_EXTRACT('hello', '(h.*o)')", "hello")]
	[InlineData("REGEXP_EXTRACT('aabbb', 'a(b+)')", "bbb")]
	[InlineData("REGEXP_EXTRACT('test123', '([a-z]+)')", "test")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtract_Extended(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtract_NullInput_ReturnsNull()
	{
		var result = await Eval("REGEXP_EXTRACT(CAST(NULL AS STRING), '[0-9]+')");
		result.Should().BeNull();
	}

	// ─── REGEXP_EXTRACT with position and occurrence ─────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	//   REGEXP_EXTRACT(value, regexp[, position[, occurrence]])

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 1, 1)", "123")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 1, 2)", "456")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 5, 1)", "123")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 5, 2)", "456")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 8, 1)", "456")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 8, 2)", null)]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 100)", null)]
	[InlineData("REGEXP_EXTRACT('hello world hello', '[a-z]+', 1, 2)", "world")]
	[InlineData("REGEXP_EXTRACT('hello world hello', '[a-z]+', 1, 3)", "hello")]
	[InlineData("REGEXP_EXTRACT('hello world hello', '([a-z]+)', 1, 2)", "world")]
	[InlineData("REGEXP_EXTRACT('aabbb', 'a(b+)', 1, 1)", "bbb")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 1)", "123")]
	[InlineData("REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 8)", "456")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtract_PositionAndOccurrence(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtract_NullPosition_ReturnsNull()
	{
		var result = await Eval("REGEXP_EXTRACT('abc123', '[0-9]+', CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtract_NullOccurrence_ReturnsNull()
	{
		var result = await Eval("REGEXP_EXTRACT('abc123', '[0-9]+', 1, CAST(NULL AS INT64))");
		result.Should().BeNull();
	}



	// ═══════════════════════════════════════════════════════════════
	// ─── REGEXP_EXTRACT_ALL ──────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract_all
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_MultipleMatches()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('abc123def456ghi789', '[0-9]+')) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("123");
		rows[1]["val"].Should().Be("456");
		rows[2]["val"].Should().Be("789");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_NoMatch_ReturnsEmptyArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('hello', '[0-9]+')) AS val");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_EmptyString_ReturnsEmptyArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('', '[a-z]+')) AS val");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_NullInput_ReturnsNull()
	{
		var result = await Eval("REGEXP_EXTRACT_ALL(CAST(NULL AS STRING), '[0-9]+')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_WithCaptureGroup()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('a1b2c3', '([a-z])')) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("a");
		rows[1]["val"].Should().Be("b");
		rows[2]["val"].Should().Be("c");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_SingleMatch()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('abc', 'abc')) AS val");
		rows.Should().HaveCount(1);
		rows[0]["val"].Should().Be("abc");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_OverlappingPatternFindsAll()
	{
		// RE2 doesn't do overlapping matches; this finds non-overlapping matches
		var rows = await QueryAsync("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('aaaa', 'aa')) AS val");
		rows.Should().HaveCount(2);
		rows[0]["val"].Should().Be("aa");
		rows[1]["val"].Should().Be("aa");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REGEXP_REPLACE (extended) ───────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_REPLACE('hello world hello', 'hello', 'hi')", "hi world hi")]
	[InlineData("REGEXP_REPLACE('abc', '[a-z]', 'X')", "XXX")]
	[InlineData("REGEXP_REPLACE('abc def', ' +', '-')", "abc-def")]
	[InlineData("REGEXP_REPLACE('hello', 'xyz', 'q')", "hello")]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]', '#')", "abc###")]
	[InlineData("REGEXP_REPLACE('', '[a-z]', 'X')", "")]
	[InlineData("REGEXP_REPLACE('test123test456', '[0-9]+', '#')", "test#test#")]
	[InlineData("REGEXP_REPLACE('a1b2c3', '[0-9]', '')", "abc")]
	[InlineData("REGEXP_REPLACE('aabbcc', '(.)\\1', 'X')", "XXX")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpReplace_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpReplace_NullInput_ReturnsNull()
	{
		var result = await Eval("REGEXP_REPLACE(CAST(NULL AS STRING), '[0-9]+', 'X')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpReplace_NullPattern_ReturnsNull()
	{
		var result = await Eval("REGEXP_REPLACE('hello', CAST(NULL AS STRING), 'X')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpReplace_NullReplacement_ReturnsNull()
	{
		var result = await Eval("REGEXP_REPLACE('hello', '[a-z]', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REPLACE (extended) ──────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE('hello hello hello', 'hello', 'hi')", "hi hi hi")]
	[InlineData("REPLACE('abcdef', 'cd', 'XXXX')", "abXXXXef")]
	[InlineData("REPLACE('abcdef', 'cd', '')", "abef")]
	[InlineData("REPLACE('aaa', 'a', 'aa')", "aaaaaa")]
	[InlineData("REPLACE('abc', 'abc', '')", "")]
	[InlineData("REPLACE('abc', 'x', 'y')", "abc")]
	[InlineData("REPLACE('hello', '', 'x')", "hello")]
	[InlineData("REPLACE('', '', '')", "")]
	[InlineData("REPLACE('hello world', ' ', '-')", "hello-world")]
	[InlineData("REPLACE('xxxx', 'xx', 'y')", "yy")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Replace_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Replace_NullInput_ReturnsNull()
	{
		var result = await Eval("REPLACE(CAST(NULL AS STRING), 'a', 'b')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Replace_NullSearch_ReturnsNull()
	{
		var result = await Eval("REPLACE('hello', CAST(NULL AS STRING), 'b')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Replace_NullReplacement_ReturnsNull()
	{
		var result = await Eval("REPLACE('hello', 'l', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REPEAT (extended) ───────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPEAT('abc', 2)", "abcabc")]
	[InlineData("REPEAT('x', 10)", "xxxxxxxxxx")]
	[InlineData("REPEAT('ab', 0)", "")]
	[InlineData("REPEAT('', 100)", "")]
	[InlineData("REPEAT('a', 1)", "a")]
	[InlineData("REPEAT('hello ', 3)", "hello hello hello ")]
	[InlineData("REPEAT('xy', 4)", "xyxyxyxy")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Repeat_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Repeat_NullString_ReturnsNull()
	{
		var result = await Eval("REPEAT(CAST(NULL AS STRING), 3)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Repeat_NullCount_ReturnsNull()
	{
		var result = await Eval("REPEAT('hello', CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Repeat_NegativeCount_ReturnsEmpty()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
		// If repetitions <= 0, the function returns an empty value.
		var result = await Eval("REPEAT('hello', -1)");
		result.Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Repeat_LargeCount_Works()
	{
		var result = (string)(await Eval("REPEAT('a', 200)"))!;
		result.Length.Should().Be(200);
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── REVERSE (extended) ──────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REVERSE('abcdef')", "fedcba")]
	[InlineData("REVERSE('racecar')", "racecar")]
	[InlineData("REVERSE('a b c')", "c b a")]
	[InlineData("REVERSE('  ')", "  ")]
	[InlineData("REVERSE('A')", "A")]
	[InlineData("REVERSE('aabb')", "bbaa")]
	[InlineData("REVERSE('!@#$')", "$#@!")]
	[InlineData("REVERSE('12345')", "54321")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Reverse_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Reverse_NullInput_ReturnsNull()
	{
		var result = await Eval("REVERSE(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Reverse_EmptyString_ReturnsEmpty()
	{
		var result = await Eval("REVERSE('')");
		result.Should().Be("");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── LPAD (extended) ─────────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('hi', 5, 'ab')", "abahi")]
	[InlineData("LPAD('hello', 10, '123')", "12312hello")]
	[InlineData("LPAD('hello', 5, 'x')", "hello")]
	[InlineData("LPAD('hello', 3, 'x')", "hel")]
	[InlineData("LPAD('hello', 0, 'x')", "")]
	[InlineData("LPAD('', 5, 'x')", "xxxxx")]
	[InlineData("LPAD('a', 4, 'xyz')", "xyza")]
	[InlineData("LPAD('hello', 10, '*')", "*****hello")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Lpad_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("LPAD('hi', 5)", "   hi")]
	[InlineData("LPAD('hello', 10)", "     hello")]
	[InlineData("LPAD('abc', 3)", "abc")]
	[InlineData("LPAD('abc', 1)", "a")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Lpad_DefaultPad_UsesSpaces(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Lpad_NullInput_ReturnsNull()
	{
		var result = await Eval("LPAD(CAST(NULL AS STRING), 5, 'x')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Lpad_NullLength_ReturnsNull()
	{
		var result = await Eval("LPAD('hello', CAST(NULL AS INT64), 'x')");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── RPAD (extended) ─────────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("RPAD('hi', 5, 'ab')", "hiaba")]
	[InlineData("RPAD('hello', 10, '123')", "hello12312")]
	[InlineData("RPAD('hello', 5, 'x')", "hello")]
	[InlineData("RPAD('hello', 3, 'x')", "hel")]
	[InlineData("RPAD('hello', 0, 'x')", "")]
	[InlineData("RPAD('', 5, 'x')", "xxxxx")]
	[InlineData("RPAD('a', 4, 'xyz')", "axyz")]
	[InlineData("RPAD('hello', 10, '*')", "hello*****")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Rpad_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("RPAD('hi', 5)", "hi   ")]
	[InlineData("RPAD('hello', 10)", "hello     ")]
	[InlineData("RPAD('abc', 3)", "abc")]
	[InlineData("RPAD('abc', 1)", "a")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Rpad_DefaultPad_UsesSpaces(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Rpad_NullInput_ReturnsNull()
	{
		var result = await Eval("RPAD(CAST(NULL AS STRING), 5, 'x')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Rpad_NullLength_ReturnsNull()
	{
		var result = await Eval("RPAD('hello', CAST(NULL AS INT64), 'x')");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── FORMAT (extended) ───────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%s and %s', 'hello', 'world')", "hello and world")]
	[InlineData("FORMAT('%d items', 42)", "42 items")]
	[InlineData("FORMAT('%05d', 7)", "00007")]
	[InlineData("FORMAT('%.3f', 3.14159)", "3.142")]
	[InlineData("FORMAT('%d + %d = %d', 2, 3, 5)", "2 + 3 = 5")]
	[InlineData("FORMAT('%%escaped%%')", "%escaped%")]
	[InlineData("FORMAT('%s', '')", "")]
	[InlineData("FORMAT('%d', 0)", "0")]
	[InlineData("FORMAT('%d', -1)", "-1")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_NullStringArg_ReturnsNullText()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		// FORMAT produces the literal text "NULL" for NULL arguments.
		var result = await Eval("FORMAT('%s', CAST(NULL AS STRING))");
		result.Should().Be("NULL");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── FORMAT %t (canonical representation) ────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	//   %t — produces a printable string representing the value using its canonical format.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%t', 42)", "42")]
	[InlineData("FORMAT('%t', -1)", "-1")]
	[InlineData("FORMAT('%t', 3.14)", "3.14")]
	[InlineData("FORMAT('%t', true)", "true")]
	[InlineData("FORMAT('%t', false)", "false")]
	[InlineData("FORMAT('%t', 'hello')", "hello")]
	[InlineData("FORMAT('%t', DATE '2024-01-15')", "2024-01-15")]
	[InlineData("FORMAT('%t', TIMESTAMP '2024-01-15T10:30:00Z')", "2024-01-15T10:30:00Z")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_PercentT_CanonicalRepresentation(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_PercentT_NullValue_ReturnsNullText()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		// NULL arguments produce the literal text "NULL"
		var result = await Eval("FORMAT('%t', CAST(NULL AS STRING))");
		result.Should().Be("NULL");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── FORMAT %T (SQL literal representation) ──────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	//   %T — produces a string that is a valid GoogleSQL literal.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%T', 42)", "42")]
	[InlineData("FORMAT('%T', -1)", "-1")]
	[InlineData("FORMAT('%T', 3.14)", "3.14")]
	[InlineData("FORMAT('%T', true)", "TRUE")]
	[InlineData("FORMAT('%T', false)", "FALSE")]
	[InlineData("FORMAT('%T', 'hello')", "\"hello\"")]
	[InlineData("FORMAT('%T', DATE '2024-01-15')", "DATE \"2024-01-15\"")]
	[InlineData("FORMAT('%T', TIMESTAMP '2024-01-15T10:30:00Z')", "TIMESTAMP \"2024-01-15T10:30:00Z\"")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_PercentUpperT_SqlLiteralRepresentation(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_PercentUpperT_NullValue_ReturnsNullText()
	{
		var result = await Eval("FORMAT('%T', CAST(NULL AS STRING))");
		result.Should().Be("NULL");
	}



	// ═══════════════════════════════════════════════════════════════
	// ─── SPLIT (extended) ────────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_ElementAccess_FirstElement()
	{
		var rows = await QueryAsync("SELECT SPLIT('a,b,c', ',')[OFFSET(0)] AS R");
		rows[0]["R"].Should().Be("a");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_ElementAccess_LastElement()
	{
		var rows = await QueryAsync("SELECT SPLIT('a,b,c', ',')[OFFSET(2)] AS R");
		rows[0]["R"].Should().Be("c");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_DefaultDelimiter_Comma()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
		// Default delimiter is comma when not specified.
		var result = await Eval("ARRAY_LENGTH(SPLIT('a,b,c'))");
		result.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_EmptyElements()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT('a,,b', ',')) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("a");
		rows[1]["val"].Should().Be("");
		rows[2]["val"].Should().Be("b");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_LeadingTrailingDelimiters()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT(',a,b,', ',')) AS val");
		rows.Should().HaveCount(4);
		rows[0]["val"].Should().Be("");
		rows[1]["val"].Should().Be("a");
		rows[2]["val"].Should().Be("b");
		rows[3]["val"].Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_MultiCharDelimiter()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT('a::b::c', '::')) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("a");
		rows[1]["val"].Should().Be("b");
		rows[2]["val"].Should().Be("c");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_EmptyString_ReturnsSingleEmptyElement()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT('', ',')) AS val");
		rows.Should().HaveCount(1);
		rows[0]["val"].Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_NullInput_ReturnsNull()
	{
		var result = await Eval("SPLIT(CAST(NULL AS STRING), ',')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_NoDelimiterFound_ReturnsSingleElement()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT('hello', ',')) AS val");
		rows.Should().HaveCount(1);
		rows[0]["val"].Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_EmptyDelimiter_ReturnsSingleChars()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
		// With empty delimiter, splits into individual characters.
		var rows = await QueryAsync("SELECT val FROM UNNEST(SPLIT('abc', '')) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("a");
		rows[1]["val"].Should().Be("b");
		rows[2]["val"].Should().Be("c");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── CHAR_LENGTH (extended) ──────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#char_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CHAR_LENGTH('')", 0L)]
	[InlineData("CHAR_LENGTH('a')", 1L)]
	[InlineData("CHAR_LENGTH('hello world')", 11L)]
	[InlineData("CHAR_LENGTH('  spaces  ')", 10L)]
	[InlineData("CHAR_LENGTH('1234567890')", 10L)]
	[InlineData("CHARACTER_LENGTH('abc')", 3L)]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task CharLength_Extended(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task CharLength_NullInput_ReturnsNull()
	{
		var result = await Eval("CHAR_LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── BYTE_LENGTH (extended) ──────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('hello world')", 11L)]
	[InlineData("BYTE_LENGTH('1234567890')", 10L)]
	[InlineData("BYTE_LENGTH('  ')", 2L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ByteLength_Extended(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ByteLength_NullInput_ReturnsNull()
	{
		var result = await Eval("BYTE_LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── SAFE_CONVERT_BYTES_TO_STRING ────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#safe_convert_bytes_to_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CONVERT_BYTES_TO_STRING(b'hello')", "hello")]
	[InlineData("SAFE_CONVERT_BYTES_TO_STRING(b'')", "")]
	[InlineData("SAFE_CONVERT_BYTES_TO_STRING(b'abc 123')", "abc 123")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task SafeConvertBytesToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task SafeConvertBytesToString_NullInput_ReturnsNull()
	{
		var result = await Eval("SAFE_CONVERT_BYTES_TO_STRING(CAST(NULL AS BYTES))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── TO_BASE64 / FROM_BASE64 ────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_base64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_base64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_BASE64(b'hello')", "aGVsbG8=")]
	[InlineData("TO_BASE64(b'')", "")]
	[InlineData("TO_BASE64(b'abc')", "YWJj")]
	[InlineData("TO_BASE64(b'Hello World')", "SGVsbG8gV29ybGQ=")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToBase64(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToBase64_NullInput_ReturnsNull()
	{
		var result = await Eval("TO_BASE64(CAST(NULL AS BYTES))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task FromBase64_RoundTrip()
	{
		var result = await Eval("SAFE_CONVERT_BYTES_TO_STRING(FROM_BASE64('aGVsbG8='))");
		result.Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task FromBase64_EmptyString()
	{
		var result = await Eval("SAFE_CONVERT_BYTES_TO_STRING(FROM_BASE64(''))");
		result.Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task FromBase64_NullInput_ReturnsNull()
	{
		var result = await Eval("FROM_BASE64(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── TO_CODE_POINTS / CODE_POINTS_TO_STRING ─────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_code_points
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_string
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToCodePoints_Basic()
	{
		var rows = await QueryAsync("SELECT cp FROM UNNEST(TO_CODE_POINTS('ABC')) AS cp");
		rows.Should().HaveCount(3);
		rows[0]["cp"].Should().Be(65L);
		rows[1]["cp"].Should().Be(66L);
		rows[2]["cp"].Should().Be(67L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToCodePoints_EmptyString()
	{
		var rows = await QueryAsync("SELECT cp FROM UNNEST(TO_CODE_POINTS('')) AS cp");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToCodePoints_NullInput_ReturnsNull()
	{
		var result = await Eval("TO_CODE_POINTS(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task CodePointsToString_Basic()
	{
		var result = await Eval("CODE_POINTS_TO_STRING([65, 66, 67])");
		result.Should().Be("ABC");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task CodePointsToString_NullInput_ReturnsNull()
	{
		var result = await Eval("CODE_POINTS_TO_STRING(CAST(NULL AS ARRAY<INT64>))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task CodePointsToString_RoundTrip()
	{
		var result = await Eval("CODE_POINTS_TO_STRING(TO_CODE_POINTS('hello'))");
		result.Should().Be("hello");
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── TO_HEX / FROM_HEX ──────────────────────────────────────
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	[InlineData("TO_HEX(b'abc')", "616263")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToHex_Extended(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToHex_NullInput_ReturnsNull()
	{
		var result = await Eval("TO_HEX(CAST(NULL AS BYTES))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task FromHex_RoundTrip()
	{
		var result = await Eval("TO_HEX(FROM_HEX('616263'))");
		result.Should().Be("616263");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task FromHex_NullInput_ReturnsNull()
	{
		var result = await Eval("FROM_HEX(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── NULL propagation (cross-function) ───────────────────────
	// All string functions should return NULL when given NULL input.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(CAST(NULL AS STRING))")]
	[InlineData("LOWER(CAST(NULL AS STRING))")]
	[InlineData("TRIM(CAST(NULL AS STRING))")]
	[InlineData("LTRIM(CAST(NULL AS STRING))")]
	[InlineData("RTRIM(CAST(NULL AS STRING))")]
	[InlineData("SUBSTR(CAST(NULL AS STRING), 1)")]
	[InlineData("LENGTH(CAST(NULL AS STRING))")]
	[InlineData("STRPOS(CAST(NULL AS STRING), 'a')")]
	[InlineData("CONCAT(CAST(NULL AS STRING), 'a')")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task NullPropagation_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ─── Nested / combined string functions ──────────────────────
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE(REVERSE('dlrow'), 'w', 'W')", "World")]
	[InlineData("LPAD(CAST(42 AS STRING), 5, '0')", "00042")]
	[InlineData("CHAR_LENGTH(REPEAT('ab', 5))", 10L)]
	[InlineData("STARTS_WITH(UPPER('hello'), 'HE')", true)]
	[InlineData("ENDS_WITH(LOWER('HELLO'), 'lo')", true)]
	[InlineData("REGEXP_CONTAINS(CONCAT('abc', '123'), '[0-9]+')", true)]
	[InlineData("BYTE_LENGTH(REPEAT('x', 10))", 10L)]
	[InlineData("REVERSE(REVERSE('hello'))", "hello")]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task NestedCombined_Functions(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Split_Then_ArrayLength()
	{
		var result = await Eval("ARRAY_LENGTH(SPLIT('one::two::three', '::'))");
		result.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task RegexpExtractAll_Then_ArrayLength()
	{
		var result = await Eval("ARRAY_LENGTH(REGEXP_EXTRACT_ALL('a1b2c3d4', '[0-9]'))");
		result.Should().Be(4L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task ToCodePoints_Then_ArrayLength()
	{
		var result = await Eval("ARRAY_LENGTH(TO_CODE_POINTS('hello'))");
		result.Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Concat_Reverse_Upper_Combined()
	{
		var result = await Eval("UPPER(REVERSE(CONCAT('a', 'b', 'c')))");
		result.Should().Be("CBA");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Format_With_Repeat()
	{
		var result = await Eval("FORMAT('%s: %s', 'separator', REPEAT('-', 5))");
		result.Should().Be("separator: -----");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunctionExtended")]
	public async Task Replace_Then_Split_Count()
	{
		var result = await Eval("ARRAY_LENGTH(SPLIT(REPLACE('a.b.c', '.', ','), ','))");
		result.Should().Be(3L);
	}
}
