using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Additional edge-case tests for expressions, function combinations, and SQL features
/// to increase test coverage breadth. Tests cover multiple function categories with
/// many parameter combinations via [Theory]+[InlineData].
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ExpressionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public ExpressionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ROUND with various decimal places
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	//   "Rounds halfway cases away from zero."
	[InlineData("ROUND(1.5)", 2.0)]
	[InlineData("ROUND(2.5)", 3.0)]  // Away from zero
	[InlineData("ROUND(3.5)", 4.0)]
	[InlineData("ROUND(4.5)", 5.0)]  // Away from zero
	[InlineData("ROUND(1.4)", 1.0)]
	[InlineData("ROUND(1.6)", 2.0)]
	[InlineData("ROUND(-1.5)", -2.0)]
	[InlineData("ROUND(-2.5)", -3.0)]  // Away from zero
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(1.234, 2)", 1.23)]
	[InlineData("ROUND(1.235, 2)", 1.24)]
	[InlineData("ROUND(1.999, 2)", 2.0)]
	[InlineData("ROUND(1.001, 2)", 1.0)]
	[InlineData("ROUND(123.456, 1)", 123.5)]
	[InlineData("ROUND(123.456, 0)", 123.0)]
	[InlineData("ROUND(123.456, -1)", 120.0)]
	[InlineData("ROUND(123.456, -2)", 100.0)]
	[InlineData("ROUND(150.0, -2)", 200.0)]
	[InlineData("ROUND(-3.14, 1)", -3.1)]
	[InlineData("ROUND(0.5)", 1.0)]  // Away from zero
	public async Task Round_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// TRUNC with various decimal places
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUNC(1.9)", 1.0)]
	[InlineData("TRUNC(1.1)", 1.0)]
	[InlineData("TRUNC(-1.9)", -1.0)]
	[InlineData("TRUNC(-1.1)", -1.0)]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(1.999, 2)", 1.99)]
	[InlineData("TRUNC(1.999, 1)", 1.9)]
	[InlineData("TRUNC(1.999, 0)", 1.0)]
	[InlineData("TRUNC(123.456, -1)", 120.0)]
	[InlineData("TRUNC(199.0, -2)", 100.0)]
	[InlineData("TRUNC(-1.999, 2)", -1.99)]
	[InlineData("TRUNC(-1.999, 1)", -1.9)]
	public async Task Trunc_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// CEIL / FLOOR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ceil
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CEIL(1.1)", 2.0)]
	[InlineData("CEIL(1.9)", 2.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(-1.1)", -1.0)]
	[InlineData("CEIL(-1.9)", -1.0)]
	[InlineData("CEIL(-1.0)", -1.0)]
	[InlineData("CEIL(0.001)", 1.0)]
	[InlineData("CEIL(-0.001)", 0.0)]
	[InlineData("CEILING(2.3)", 3.0)]
	[InlineData("CEILING(-2.3)", -2.0)]
	public async Task Ceil_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("FLOOR(1.1)", 1.0)]
	[InlineData("FLOOR(1.9)", 1.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(-1.1)", -2.0)]
	[InlineData("FLOOR(-1.9)", -2.0)]
	[InlineData("FLOOR(-1.0)", -1.0)]
	[InlineData("FLOOR(0.999)", 0.0)]
	[InlineData("FLOOR(-0.001)", -1.0)]
	public async Task Floor_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// SQRT, POW, EXP, LN, LOG, LOG10
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SQRT(0.0)", 0.0)]
	[InlineData("SQRT(1.0)", 1.0)]
	[InlineData("SQRT(4.0)", 2.0)]
	[InlineData("SQRT(9.0)", 3.0)]
	[InlineData("SQRT(16.0)", 4.0)]
	[InlineData("SQRT(100.0)", 10.0)]
	[InlineData("SQRT(2.0)", 1.4142135623730951)]
	[InlineData("SQRT(0.25)", 0.5)]
	public async Task Sqrt_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("POW(2, 0)", 1.0)]
	[InlineData("POW(2, 1)", 2.0)]
	[InlineData("POW(2, 2)", 4.0)]
	[InlineData("POW(2, 3)", 8.0)]
	[InlineData("POW(2, 10)", 1024.0)]
	[InlineData("POW(3, 3)", 27.0)]
	[InlineData("POW(10, 0)", 1.0)]
	[InlineData("POW(10, 1)", 10.0)]
	[InlineData("POW(10, 2)", 100.0)]
	[InlineData("POW(0, 0)", 1.0)]
	[InlineData("POW(0, 1)", 0.0)]
	[InlineData("POW(-2, 2)", 4.0)]
	[InlineData("POW(-2, 3)", -8.0)]
	[InlineData("POWER(2, 4)", 16.0)]
	[InlineData("POW(0.5, 2)", 0.25)]
	public async Task Pow_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("EXP(0.0)", 1.0)]
	[InlineData("EXP(1.0)", 2.718281828459045)]
	[InlineData("EXP(2.0)", 7.38905609893065)]
	[InlineData("EXP(-1.0)", 0.36787944117144233)]
	public async Task Exp_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("LN(1.0)", 0.0)]
	[InlineData("LN(2.718281828459045)", 1.0)]
	[InlineData("LN(10.0)", 2.302585092994046)]
	[InlineData("LN(100.0)", 4.605170185988092)]
	public async Task Ln_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-8);
	}

	[Theory]
	[InlineData("LOG10(1.0)", 0.0)]
	[InlineData("LOG10(10.0)", 1.0)]
	[InlineData("LOG10(100.0)", 2.0)]
	[InlineData("LOG10(1000.0)", 3.0)]
	[InlineData("LOG10(0.1)", -1.0)]
	public async Task Log10_Detailed(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// String edge cases: CONCAT variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT('a')", "a")]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('a', 'b', 'c')", "abc")]
	[InlineData("CONCAT('a', 'b', 'c', 'd')", "abcd")]
	[InlineData("CONCAT('a', 'b', 'c', 'd', 'e')", "abcde")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('', '', '')", "")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H')", "ABCDEFGH")]
	public async Task Concat_ManyArgs(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// SUBSTR edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SUBSTR('hello', 1)", "hello")]
	[InlineData("SUBSTR('hello', 2)", "ello")]
	[InlineData("SUBSTR('hello', 5)", "o")]
	[InlineData("SUBSTR('hello', 6)", "")]
	[InlineData("SUBSTR('hello', 1, 1)", "h")]
	[InlineData("SUBSTR('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello', 1, 5)", "hello")]
	[InlineData("SUBSTR('hello', 1, 10)", "hello")]
	[InlineData("SUBSTR('hello', 2, 3)", "ell")]
	[InlineData("SUBSTR('hello', -1)", "o")]
	[InlineData("SUBSTR('hello', -2)", "lo")]
	[InlineData("SUBSTR('hello', -5)", "hello")]
	[InlineData("SUBSTR('', 1)", "")]
	[InlineData("SUBSTR('a', 1)", "a")]
	[InlineData("SUBSTR('a', 1, 0)", "")]
	public async Task Substr_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// REPLACE edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE('hello world', 'world', 'earth')", "hello earth")]
	[InlineData("REPLACE('aaa', 'a', 'bb')", "bbbbbb")]
	[InlineData("REPLACE('abc', 'x', 'y')", "abc")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('abc', '', 'x')", "abc")]
	[InlineData("REPLACE('abc', 'abc', '')", "")]
	[InlineData("REPLACE('aaa', 'aa', 'b')", "ba")]
	[InlineData("REPLACE('hello', 'l', '')", "heo")]
	[InlineData("REPLACE('abcabc', 'abc', 'X')", "XX")]
	public async Task Replace_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// LPAD / RPAD edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('abc', 5)", "  abc")]
	[InlineData("LPAD('abc', 5, 'x')", "xxabc")]
	[InlineData("LPAD('abc', 3)", "abc")]
	[InlineData("LPAD('abc', 2)", "ab")]
	[InlineData("LPAD('abc', 1)", "a")]
	[InlineData("LPAD('abc', 0)", "")]
	[InlineData("LPAD('', 3)", "   ")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("LPAD('abc', 7, 'xy')", "xyxyabc")]
	public async Task Lpad_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("RPAD('abc', 5)", "abc  ")]
	[InlineData("RPAD('abc', 5, 'x')", "abcxx")]
	[InlineData("RPAD('abc', 3)", "abc")]
	[InlineData("RPAD('abc', 2)", "ab")]
	[InlineData("RPAD('abc', 1)", "a")]
	[InlineData("RPAD('abc', 0)", "")]
	[InlineData("RPAD('', 3)", "   ")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('abc', 7, 'xy')", "abcxyxy")]
	public async Task Rpad_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TRIM / LTRIM / RTRIM edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRIM('  hello  ')", "hello")]
	[InlineData("TRIM('hello')", "hello")]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM('   ')", "")]
	[InlineData("TRIM('  hello')", "hello")]
	[InlineData("TRIM('hello  ')", "hello")]
	[InlineData("LTRIM('  hello  ')", "hello  ")]
	[InlineData("LTRIM('hello')", "hello")]
	[InlineData("LTRIM('')", "")]
	[InlineData("LTRIM('   ')", "")]
	[InlineData("RTRIM('  hello  ')", "  hello")]
	[InlineData("RTRIM('hello')", "hello")]
	[InlineData("RTRIM('')", "")]
	[InlineData("RTRIM('   ')", "")]
	public async Task Trim_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// UPPER / LOWER edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#upper
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER('hello')", "HELLO")]
	[InlineData("UPPER('HELLO')", "HELLO")]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('123')", "123")]
	[InlineData("UPPER('Hello World')", "HELLO WORLD")]
	[InlineData("UPPER('abc123def')", "ABC123DEF")]
	[InlineData("LOWER('HELLO')", "hello")]
	[InlineData("LOWER('hello')", "hello")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('123')", "123")]
	[InlineData("LOWER('Hello World')", "hello world")]
	[InlineData("LOWER('ABC123DEF')", "abc123def")]
	public async Task UpperLower_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// LENGTH / STRPOS / STARTS_WITH / ENDS_WITH edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('hello')", 5L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("CHAR_LENGTH('')", 0L)]
	[InlineData("CHAR_LENGTH('abc')", 3L)]
	[InlineData("CHARACTER_LENGTH('abcde')", 5L)]
	public async Task Length_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("STRPOS('hello', 'h')", 1L)]
	[InlineData("STRPOS('hello', 'e')", 2L)]
	[InlineData("STRPOS('hello', 'o')", 5L)]
	[InlineData("STRPOS('hello', 'x')", 0L)]
	[InlineData("STRPOS('hello', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('hello', 'ell')", 2L)]
	[InlineData("STRPOS('hello', 'hello')", 1L)]
	[InlineData("STRPOS('aaa', 'aa')", 1L)]
	public async Task Strpos_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("STARTS_WITH('hello', 'hel')", true)]
	[InlineData("STARTS_WITH('hello', 'hello')", true)]
	[InlineData("STARTS_WITH('hello', '')", true)]
	[InlineData("STARTS_WITH('hello', 'xyz')", false)]
	[InlineData("STARTS_WITH('hello', 'Hello')", false)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("STARTS_WITH('a', 'ab')", false)]
	public async Task StartsWith_EdgeCases(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ENDS_WITH('hello', 'llo')", true)]
	[InlineData("ENDS_WITH('hello', 'hello')", true)]
	[InlineData("ENDS_WITH('hello', '')", true)]
	[InlineData("ENDS_WITH('hello', 'xyz')", false)]
	[InlineData("ENDS_WITH('hello', 'LLO')", false)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('a', 'ba')", false)]
	public async Task EndsWith_EdgeCases(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// REVERSE edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REVERSE('hello')", "olleh")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('ab')", "ba")]
	[InlineData("REVERSE('abcde')", "edcba")]
	[InlineData("REVERSE('racecar')", "racecar")]
	[InlineData("REVERSE('12345')", "54321")]
	public async Task Reverse_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// REPEAT edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPEAT('a', 0)", "")]
	[InlineData("REPEAT('a', 1)", "a")]
	[InlineData("REPEAT('a', 3)", "aaa")]
	[InlineData("REPEAT('ab', 3)", "ababab")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('x', 10)", "xxxxxxxxxx")]
	[InlineData("REPEAT('abc', 2)", "abcabc")]
	public async Task Repeat_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// LEFT / RIGHT edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#left
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LEFT('hello', 0)", "")]
	[InlineData("LEFT('hello', 1)", "h")]
	[InlineData("LEFT('hello', 3)", "hel")]
	[InlineData("LEFT('hello', 5)", "hello")]
	[InlineData("LEFT('hello', 10)", "hello")]
	[InlineData("LEFT('', 5)", "")]
	public async Task Left_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("RIGHT('hello', 0)", "")]
	[InlineData("RIGHT('hello', 1)", "o")]
	[InlineData("RIGHT('hello', 3)", "llo")]
	[InlineData("RIGHT('hello', 5)", "hello")]
	[InlineData("RIGHT('hello', 10)", "hello")]
	[InlineData("RIGHT('', 5)", "")]
	public async Task Right_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP functions edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_CONTAINS('hello', 'h')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^h')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'o$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^hello$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'x')", false)]
	[InlineData("REGEXP_CONTAINS('hello123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '[0-9]+')", false)]
	[InlineData("REGEXP_CONTAINS('', '')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '.')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'a.c')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'a..c')", false)]
	public async Task RegexpContains_EdgeCases(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("REGEXP_EXTRACT('hello123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc def', '[a-z]+')", "abc")]
	[InlineData("REGEXP_EXTRACT('2024-01-15', '[0-9]{4}')", "2024")]
	public async Task RegexpExtract_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task RegexpExtract_NoMatch_ReturnsNull()
	{
		(await Eval("REGEXP_EXTRACT('hello', '[0-9]+')")).Should().BeNull();
	}

	[Theory]
	[InlineData("REGEXP_REPLACE('hello world', 'world', 'earth')", "hello earth")]
	[InlineData("REGEXP_REPLACE('abc123def', '[0-9]+', 'NUM')", "abcNUMdef")]
	[InlineData("REGEXP_REPLACE('aaa', 'a', 'b')", "bbb")]
	[InlineData("REGEXP_REPLACE('hello', 'x', 'y')", "hello")]
	public async Task RegexpReplace_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// INITCAP edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#initcap
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("INITCAP('hello world')", "Hello World")]
	[InlineData("INITCAP('HELLO WORLD')", "Hello World")]
	[InlineData("INITCAP('hello')", "Hello")]
	[InlineData("INITCAP('')", "")]
	[InlineData("INITCAP('a')", "A")]
	[InlineData("INITCAP('hello-world')", "Hello-World")]
	[InlineData("INITCAP('foo bar baz')", "Foo Bar Baz")]
	public async Task Initcap_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// ASCII / CHR edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ascii
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ASCII('A')", 65L)]
	[InlineData("ASCII('a')", 97L)]
	[InlineData("ASCII('0')", 48L)]
	[InlineData("ASCII('Z')", 90L)]
	[InlineData("ASCII(' ')", 32L)]
	public async Task Ascii_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CHR(65)", "A")]
	[InlineData("CHR(97)", "a")]
	[InlineData("CHR(48)", "0")]
	[InlineData("CHR(90)", "Z")]
	[InlineData("CHR(32)", " ")]
	public async Task Chr_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// BYTE_LENGTH edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	public async Task ByteLength_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TO_HEX / FROM_HEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	public async Task ToHex_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested / combined function calls
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(LOWER('HeLLo'))", "HELLO")]
	[InlineData("LOWER(UPPER('HeLLo'))", "hello")]
	[InlineData("REVERSE(REVERSE('hello'))", "hello")]
	[InlineData("TRIM(CONCAT('  ', 'hello', '  '))", "hello")]
	[InlineData("LENGTH(CONCAT('a', 'b', 'c'))", 3L)]
	[InlineData("SUBSTR(UPPER('hello'), 1, 3)", "HEL")]
	[InlineData("REPLACE(LOWER('HELLO'), 'hello', 'world')", "world")]
	[InlineData("UPPER(SUBSTR('hello world', 1, 5))", "HELLO")]
	[InlineData("LENGTH(REPEAT('ab', 5))", 10L)]
	[InlineData("STRPOS(UPPER('hello'), 'EL')", 2L)]
	public async Task NestedFunctions(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST chains and conversions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(42 AS STRING)", "42")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(true AS STRING)", "true")]
	[InlineData("CAST(false AS STRING)", "false")]
	[InlineData("CAST(3.14 AS STRING)", "3.14")]
	public async Task CastToString(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('0' AS INT64)", 0L)]
	public async Task CastToInt(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('0.0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	public async Task CastToFloat(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	public async Task CastToBool(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	// Chain: INT -> STRING -> INT
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(-1 AS STRING) AS INT64)", -1L)]
	// Chain: FLOAT -> STRING -> FLOAT
	// Chain: BOOL -> STRING -> BOOL
	[InlineData("CAST(CAST(true AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(false AS STRING) AS BOOL)", false)]
	public async Task CastChain(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// FORMAT function edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%d', -1)", "-1")]
	[InlineData("FORMAT('%d', 0)", "0")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%s', '')", "")]
	[InlineData("FORMAT('%s %s', 'hello', 'world')", "hello world")]
	[InlineData("FORMAT('%d + %d = %d', 1, 2, 3)", "1 + 2 = 3")]
	public async Task Format_EdgeCases(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Date/timestamp edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-01-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-15')", 1L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-15')", 15L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2000-12-31')", 2000L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-25')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-02-29')", 29L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-01')", 2L)]   // Monday = 2
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)] // 2024 is leap year
	public async Task Extract_DateEdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	// Ref: default timezone is America/Los_Angeles
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-15T14:30:00Z')", 6L)]       // 14-8=6 (Jan UTC-8)
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-15T14:30:00Z')", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-15T14:30:45Z')", 45L)]
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-01-15T00:00:00Z')", 2024L)]   // LA: Jan 14 16:00, still 2024
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-06-15T00:00:00Z')", 6L)]     // LA: Jun 14 17:00, still June
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-01-31T00:00:00Z')", 30L)]      // LA: Jan 30 16:00
	public async Task Extract_TimestampEdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD / TIMESTAMP_SUB detailed
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: default timezone is America/Los_Angeles (Jan=UTC-8, Mar=UTC-8)
	[InlineData("EXTRACT(DAY FROM TIMESTAMP_ADD(TIMESTAMP '2024-01-15T00:00:00Z', INTERVAL 1 DAY))", 15L)]    // Result: Jan 16 00:00Z → LA: Jan 15 16:00
	[InlineData("EXTRACT(DAY FROM TIMESTAMP_ADD(TIMESTAMP '2024-01-31T00:00:00Z', INTERVAL 1 DAY))", 31L)]   // Result: Feb 1 00:00Z → LA: Jan 31 16:00
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP_ADD(TIMESTAMP '2024-01-31T00:00:00Z', INTERVAL 1 DAY))", 1L)]  // LA: Jan 31 16:00 → month=1
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP_ADD(TIMESTAMP '2024-01-15T23:00:00Z', INTERVAL 2 HOUR))", 17L)] // Result: Jan 16 01:00Z → LA: Jan 15 17:00
	[InlineData("EXTRACT(DAY FROM TIMESTAMP_SUB(TIMESTAMP '2024-01-15T00:00:00Z', INTERVAL 1 DAY))", 13L)]   // Result: Jan 14 00:00Z → LA: Jan 13 16:00
	[InlineData("EXTRACT(DAY FROM TIMESTAMP_SUB(TIMESTAMP '2024-03-01T00:00:00Z', INTERVAL 1 DAY))", 28L)]   // Result: Feb 29 00:00Z → LA: Feb 28 16:00
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY))", 12L)] // Result: Dec 31 00:00Z → LA: Dec 30 16:00
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY))", 2023L)] // LA: Dec 30 → 2023
	public async Task TimestampAddSub_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_ADD / DATE_SUB detailed
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(DAY FROM DATE_ADD(DATE '2024-01-15', INTERVAL 1 DAY))", 16L)]
	[InlineData("EXTRACT(DAY FROM DATE_ADD(DATE '2024-01-31', INTERVAL 1 DAY))", 1L)]
	[InlineData("EXTRACT(MONTH FROM DATE_ADD(DATE '2024-01-31', INTERVAL 1 DAY))", 2L)]
	[InlineData("EXTRACT(DAY FROM DATE_SUB(DATE '2024-01-15', INTERVAL 1 DAY))", 14L)]
	[InlineData("EXTRACT(DAY FROM DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY))", 29L)]
	[InlineData("EXTRACT(MONTH FROM DATE_ADD(DATE '2024-01-15', INTERVAL 1 MONTH))", 2L)]
	[InlineData("EXTRACT(YEAR FROM DATE_ADD(DATE '2024-01-15', INTERVAL 1 YEAR))", 2025L)]
	[InlineData("EXTRACT(YEAR FROM DATE_SUB(DATE '2024-01-15', INTERVAL 1 YEAR))", 2023L)]
	public async Task DateAddSub_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_DIFF detailed
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T01:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', HOUR)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:01:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MINUTE)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-02T00:00:00Z', DAY)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-02-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 31L)]
	public async Task TimestampDiff_EdgeCases(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Complex combined expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(GREATEST(1, 2, 3) AS STRING)", "3")]
	[InlineData("LENGTH(REPEAT('ab', CAST(LEAST(3, 5, 7) AS INT64)))", 6L)]
	[InlineData("ABS(SIGN(-5) * 10)", 10L)]
	[InlineData("COALESCE(CAST(NULL AS STRING), LOWER('HELLO'))", "hello")]
	[InlineData("IF(LENGTH('hello') > 3, UPPER('yes'), LOWER('NO'))", "YES")]
	[InlineData("SUBSTR(CONCAT('hello', ' ', 'world'), 7)", "world")]
	[InlineData("REPLACE(UPPER('hello world'), 'WORLD', LOWER('EARTH'))", "HELLO earth")]
	public async Task ComplexCombined(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}
}
