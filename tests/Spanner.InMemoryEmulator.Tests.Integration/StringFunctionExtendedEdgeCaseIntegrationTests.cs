using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended string function edge cases: REPLACE, REGEXP_EXTRACT/REPLACE, FORMAT,
/// encoding functions, STARTS_WITH, ENDS_WITH, STRPOS, BYTE_LENGTH, etc.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionExtendedEdgeCaseIntegrationTests : IntegrationTestBase
{
	public StringFunctionExtendedEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE('abc', 'b', 'X')", "aXc")]
	[InlineData("REPLACE('aaa', 'a', 'bb')", "bbbbbb")]
	[InlineData("REPLACE('abc', 'x', 'Y')", "abc")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('abc', '', 'X')", "abc")]
	[InlineData("REPLACE('abc', 'abc', '')", "")]
	[InlineData("REPLACE('abcabc', 'abc', 'X')", "XX")]
	public async Task Replace_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REPLACE(NULL, 'a', 'b')")]
	[InlineData("REPLACE('abc', NULL, 'b')")]
	[InlineData("REPLACE('abc', 'a', NULL)")]
	public async Task Replace_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// STARTS_WITH / ENDS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STARTS_WITH('abc', 'a')", true)]
	[InlineData("STARTS_WITH('abc', 'ab')", true)]
	[InlineData("STARTS_WITH('abc', 'abc')", true)]
	[InlineData("STARTS_WITH('abc', 'b')", false)]
	[InlineData("STARTS_WITH('abc', 'abcd')", false)]
	[InlineData("STARTS_WITH('abc', '')", true)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('abc', 'c')", true)]
	[InlineData("ENDS_WITH('abc', 'bc')", true)]
	[InlineData("ENDS_WITH('abc', 'abc')", true)]
	[InlineData("ENDS_WITH('abc', 'a')", false)]
	[InlineData("ENDS_WITH('abc', '')", true)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	public async Task StartsWithEndsWith_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("STARTS_WITH(NULL, 'a')")]
	[InlineData("STARTS_WITH('abc', NULL)")]
	[InlineData("ENDS_WITH(NULL, 'a')")]
	[InlineData("ENDS_WITH('abc', NULL)")]
	public async Task StartsWithEndsWith_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// STRPOS (1-based position, 0 if not found)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STRPOS('abc', 'a')", 1L)]
	[InlineData("STRPOS('abc', 'b')", 2L)]
	[InlineData("STRPOS('abc', 'c')", 3L)]
	[InlineData("STRPOS('abc', 'abc')", 1L)]
	[InlineData("STRPOS('abc', 'x')", 0L)]
	[InlineData("STRPOS('abc', '')", 1L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('abcabc', 'bc')", 2L)]
	[InlineData("STRPOS('aaa', 'aa')", 1L)]
	public async Task Strpos_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("STRPOS(NULL, 'a')")]
	[InlineData("STRPOS('abc', NULL)")]
	public async Task Strpos_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// UPPER / LOWER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#upper
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER('abc')", "ABC")]
	[InlineData("UPPER('ABC')", "ABC")]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('aBc')", "ABC")]
	[InlineData("UPPER('123')", "123")]
	[InlineData("LOWER('ABC')", "abc")]
	[InlineData("LOWER('abc')", "abc")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('AbC')", "abc")]
	[InlineData("LOWER('123')", "123")]
	public async Task UpperLower_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("UPPER(NULL)")]
	[InlineData("LOWER(NULL)")]
	public async Task UpperLower_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TRIM / LTRIM / RTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRIM('  abc  ')", "abc")]
	[InlineData("TRIM('abc')", "abc")]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM('   ')", "")]
	[InlineData("LTRIM('  abc  ')", "abc  ")]
	[InlineData("LTRIM('abc')", "abc")]
	[InlineData("RTRIM('  abc  ')", "  abc")]
	[InlineData("RTRIM('abc')", "abc")]
	public async Task Trim_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRIM(NULL)")]
	[InlineData("LTRIM(NULL)")]
	[InlineData("RTRIM(NULL)")]
	public async Task Trim_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// LENGTH / CHAR_LENGTH / CHARACTER_LENGTH / BYTE_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('abc')", 3L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("CHAR_LENGTH('abc')", 3L)]
	[InlineData("CHARACTER_LENGTH('abc')", 3L)]
	public async Task Length_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("LENGTH(NULL)")]
	[InlineData("CHAR_LENGTH(NULL)")]
	[InlineData("CHARACTER_LENGTH(NULL)")]
	public async Task Length_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// CONCAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('a', 'b', 'c')", "abc")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	public async Task Concat_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CONCAT(NULL, 'b')")]
	[InlineData("CONCAT('a', NULL)")]
	[InlineData("CONCAT(NULL, NULL)")]
	public async Task Concat_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REVERSE('abc')", "cba")]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('abba')", "abba")]
	[InlineData("REVERSE('hello')", "olleh")]
	public async Task Reverse_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task Reverse_Null_ReturnsNull()
	{
		(await Eval("REVERSE(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REPEAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPEAT('abc', 0)", "")]
	[InlineData("REPEAT('abc', 1)", "abc")]
	[InlineData("REPEAT('abc', 3)", "abcabcabc")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('x', 5)", "xxxxx")]
	public async Task Repeat_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REPEAT(NULL, 3)")]
	[InlineData("REPEAT('abc', NULL)")]
	public async Task Repeat_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_CONTAINS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_CONTAINS('abc', 'a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'abc')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'c$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'x')", false)]
	[InlineData("REGEXP_CONTAINS('abc', '^abc$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '.*')", true)]
	[InlineData("REGEXP_CONTAINS('', '.*')", true)]
	[InlineData("REGEXP_CONTAINS('123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '[0-9]+')", false)]
	public async Task RegexpContains_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REGEXP_CONTAINS(NULL, 'a')")]
	[InlineData("REGEXP_CONTAINS('abc', NULL)")]
	public async Task RegexpContains_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_EXTRACT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc123def456', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('hello world', '(\\w+)')", "hello")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RegexpExtract_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REGEXP_EXTRACT(NULL, '[0-9]+')")]
	[InlineData("REGEXP_EXTRACT('abc', NULL)")]
	public async Task RegexpExtract_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Fact]
	public async Task RegexpExtract_NoMatch_ReturnsNull()
	{
		(await Eval("REGEXP_EXTRACT('abc', '[0-9]+')")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]+', 'X')", "abcX")]
	[InlineData("REGEXP_REPLACE('abc', '[0-9]+', 'X')", "abc")]
	[InlineData("REGEXP_REPLACE('aAbBcC', '[A-Z]', '_')", "a_b_c_")]
	public async Task RegexpReplace_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REGEXP_REPLACE(NULL, 'a', 'b')")]
	[InlineData("REGEXP_REPLACE('abc', NULL, 'b')")]
	[InlineData("REGEXP_REPLACE('abc', 'a', NULL)")]
	public async Task RegexpReplace_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TO_HEX / FROM_HEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'')", "")]
	public async Task ToHex_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ToHex_Null_ReturnsNull()
	{
		(await Eval("TO_HEX(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SUBSTR additional edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SUBSTR('hello', 1)", "hello")]
	[InlineData("SUBSTR('hello', 2)", "ello")]
	[InlineData("SUBSTR('hello', 5)", "o")]
	[InlineData("SUBSTR('hello', 6)", "")]
	[InlineData("SUBSTR('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello', 2, 2)", "el")]
	[InlineData("SUBSTR('hello', 1, 0)", "")]
	[InlineData("SUBSTR('hello', 1, 100)", "hello")]
	[InlineData("SUBSTR('hello', -4)", "ello")]
	[InlineData("SUBSTR('hello', -5)", "hello")]
	[InlineData("SUBSTR('hello', -6)", "hello")]
	[InlineData("SUBSTR('hello', -1)", "o")]
	public async Task Substr_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SUBSTR(NULL, 1)")]
	[InlineData("SUBSTR(NULL, 1, 2)")]
	public async Task Substr_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// LPAD / RPAD additional edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('abc', 5, 'x')", "xxabc")]
	[InlineData("LPAD('abc', 3, 'x')", "abc")]
	[InlineData("LPAD('abc', 1, 'x')", "a")]
	[InlineData("LPAD('abc', 0, 'x')", "")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('abc', 5, 'x')", "abcxx")]
	[InlineData("RPAD('abc', 3, 'x')", "abc")]
	[InlineData("RPAD('abc', 1, 'x')", "a")]
	[InlineData("RPAD('abc', 0, 'x')", "")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	public async Task LpadRpad_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("LPAD(NULL, 5, 'x')")]
	[InlineData("LPAD('abc', NULL, 'x')")]
	[InlineData("LPAD('abc', 5, NULL)")]
	[InlineData("RPAD(NULL, 5, 'x')")]
	[InlineData("RPAD('abc', NULL, 'x')")]
	[InlineData("RPAD('abc', 5, NULL)")]
	public async Task LpadRpad_Null_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SPLIT additional edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Split_EmptyString_ReturnsArrayWithEmptyString()
	{
		var result = await Eval("ARRAY_LENGTH(SPLIT('', ','))");
		result.Should().Be(1L);
	}

	[Fact]
	public async Task Split_NoDelimiter_ReturnsSingleElement()
	{
		var result = await Eval("ARRAY_LENGTH(SPLIT('abc', 'x'))");
		result.Should().Be(1L);
	}

	[Fact]
	public async Task Split_MultipleDelimiters()
	{
		var result = await Eval("ARRAY_LENGTH(SPLIT('a,b,c', ','))");
		result.Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FORMAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%d', -1)", "-1")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%d + %d = %d', 1, 2, 3)", "1 + 2 = 3")]
	public async Task Format_BasicValues(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Format_NullArg_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		//   "The function generally produces a NULL value if a NULL argument is present."
		//   Only %t and %T produce literal 'NULL' text for NULL args.
		(await Eval("FORMAT('%s', NULL)")).Should().BeNull();
	}
}
