using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Dense tests for every string function with multiple input variations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionDenseIntegrationTests : IntegrationTestBase
{
	public StringFunctionDenseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CONCAT variations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CONCAT('')", "")]
	[InlineData("CONCAT('a')", "a")]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('a', 'b', 'c')", "abc")]
	[InlineData("CONCAT('a', 'b', 'c', 'd')", "abcd")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('', 'a', '')", "a")]
	[InlineData("CONCAT(' ', ' ')", "  ")]
	[InlineData("CONCAT('abc', 'def', 'ghi', 'jkl', 'mno')", "abcdefghijklmno")]
	public async Task Concat_String(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UPPER / LOWER comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('a')", "A")]
	[InlineData("UPPER('abc')", "ABC")]
	[InlineData("UPPER('ABC')", "ABC")]
	[InlineData("UPPER('aBcDeF')", "ABCDEF")]
	[InlineData("UPPER('hello world')", "HELLO WORLD")]
	[InlineData("UPPER('123')", "123")]
	[InlineData("UPPER('abc123def')", "ABC123DEF")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('A')", "a")]
	[InlineData("LOWER('ABC')", "abc")]
	[InlineData("LOWER('abc')", "abc")]
	[InlineData("LOWER('AbCdEf')", "abcdef")]
	[InlineData("LOWER('HELLO WORLD')", "hello world")]
	[InlineData("LOWER('123')", "123")]
	[InlineData("LOWER('ABC123DEF')", "abc123def")]
	public async Task UpperLower(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TRIM / LTRIM / RTRIM
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM(' ')", "")]
	[InlineData("TRIM('  ')", "")]
	[InlineData("TRIM('abc')", "abc")]
	[InlineData("TRIM(' abc')", "abc")]
	[InlineData("TRIM('abc ')", "abc")]
	[InlineData("TRIM(' abc ')", "abc")]
	[InlineData("TRIM('  abc  ')", "abc")]
	[InlineData("LTRIM('')", "")]
	[InlineData("LTRIM(' ')", "")]
	[InlineData("LTRIM('  abc  ')", "abc  ")]
	[InlineData("LTRIM('abc')", "abc")]
	[InlineData("RTRIM('')", "")]
	[InlineData("RTRIM(' ')", "")]
	[InlineData("RTRIM('  abc  ')", "  abc")]
	[InlineData("RTRIM('abc')", "abc")]
	[InlineData("TRIM('xxabcxx', 'x')", "abc")]
	[InlineData("LTRIM('xxabc', 'x')", "abc")]
	[InlineData("RTRIM('abcxx', 'x')", "abc")]
	[InlineData("TRIM('aba', 'a')", "b")]
	public async Task TrimVariants(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SUBSTR comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SUBSTR('abcdef', 1)", "abcdef")]
	[InlineData("SUBSTR('abcdef', 2)", "bcdef")]
	[InlineData("SUBSTR('abcdef', 6)", "f")]
	[InlineData("SUBSTR('abcdef', 1, 1)", "a")]
	[InlineData("SUBSTR('abcdef', 1, 3)", "abc")]
	[InlineData("SUBSTR('abcdef', 2, 3)", "bcd")]
	[InlineData("SUBSTR('abcdef', 4, 3)", "def")]
	[InlineData("SUBSTR('abcdef', 1, 6)", "abcdef")]
	[InlineData("SUBSTR('abcdef', 1, 0)", "")]
	[InlineData("SUBSTR('abcdef', -1)", "f")]
	[InlineData("SUBSTR('abcdef', -2)", "ef")]
	[InlineData("SUBSTR('abcdef', -3)", "def")]
	[InlineData("SUBSTR('abcdef', -6)", "abcdef")]
	[InlineData("SUBSTR('abcdef', -2, 1)", "e")]
	[InlineData("SUBSTR('abcdef', -3, 2)", "de")]
	[InlineData("SUBSTR('', 1)", "")]
	[InlineData("SUBSTR('a', 1, 1)", "a")]
	public async Task Substr(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REPLACE comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("REPLACE('abc', 'b', 'x')", "axc")]
	[InlineData("REPLACE('abc', 'a', 'x')", "xbc")]
	[InlineData("REPLACE('abc', 'c', 'x')", "abx")]
	[InlineData("REPLACE('aaaa', 'a', 'b')", "bbbb")]
	[InlineData("REPLACE('abc', 'abc', 'xyz')", "xyz")]
	[InlineData("REPLACE('abc', 'x', 'y')", "abc")]
	[InlineData("REPLACE('abc', '', 'x')", "abc")]
	[InlineData("REPLACE('aaa', 'a', '')", "")]
	[InlineData("REPLACE('hello world', 'world', 'earth')", "hello earth")]
	[InlineData("REPLACE('abc', 'b', 'xy')", "axyc")]
	public async Task Replace_String(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REVERSE/REPEAT comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('ab')", "ba")]
	[InlineData("REVERSE('abc')", "cba")]
	[InlineData("REVERSE('abcd')", "dcba")]
	[InlineData("REVERSE('hello')", "olleh")]
	[InlineData("REVERSE('racecar')", "racecar")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('a', 0)", "")]
	[InlineData("REPEAT('a', 1)", "a")]
	[InlineData("REPEAT('a', 5)", "aaaaa")]
	[InlineData("REPEAT('ab', 3)", "ababab")]
	[InlineData("REPEAT('abc', 2)", "abcabc")]
	[InlineData("REPEAT('x', 10)", "xxxxxxxxxx")]
	public async Task ReverseRepeat(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LPAD / RPAD comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LPAD('abc', 5, 'x')", "xxabc")]
	[InlineData("LPAD('abc', 3, 'x')", "abc")]
	[InlineData("LPAD('abc', 6, 'xy')", "xyxabc")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("LPAD('a', 1, 'x')", "a")]
	[InlineData("LPAD('abc', 10, '0')", "0000000abc")]
	[InlineData("RPAD('abc', 5, 'x')", "abcxx")]
	[InlineData("RPAD('abc', 3, 'x')", "abc")]
	[InlineData("RPAD('abc', 6, 'xy')", "abcxyx")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('a', 1, 'x')", "a")]
	[InlineData("RPAD('abc', 10, '0')", "abc0000000")]
	public async Task LpadRpad(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// STRPOS comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("STRPOS('abc', 'a')", 1L)]
	[InlineData("STRPOS('abc', 'b')", 2L)]
	[InlineData("STRPOS('abc', 'c')", 3L)]
	[InlineData("STRPOS('abc', 'ab')", 1L)]
	[InlineData("STRPOS('abc', 'bc')", 2L)]
	[InlineData("STRPOS('abc', 'abc')", 1L)]
	[InlineData("STRPOS('abc', 'x')", 0L)]
	[InlineData("STRPOS('abc', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('aaa', 'a')", 1L)]
	[InlineData("STRPOS('hello world', 'world')", 7L)]
	[InlineData("STRPOS('hello world', 'o')", 5L)]
	public async Task Strpos(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// STARTS_WITH / ENDS_WITH comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('abc', '')", true)]
	[InlineData("STARTS_WITH('abc', 'a')", true)]
	[InlineData("STARTS_WITH('abc', 'ab')", true)]
	[InlineData("STARTS_WITH('abc', 'abc')", true)]
	[InlineData("STARTS_WITH('abc', 'b')", false)]
	[InlineData("STARTS_WITH('abc', 'x')", false)]
	[InlineData("STARTS_WITH('abc', 'abcd')", false)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('abc', '')", true)]
	[InlineData("ENDS_WITH('abc', 'c')", true)]
	[InlineData("ENDS_WITH('abc', 'bc')", true)]
	[InlineData("ENDS_WITH('abc', 'abc')", true)]
	[InlineData("ENDS_WITH('abc', 'b')", false)]
	[InlineData("ENDS_WITH('abc', 'x')", false)]
	[InlineData("ENDS_WITH('abc', 'zabc')", false)]
	public async Task StartsWithEndsWith(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LENGTH / BYTE_LENGTH / CHAR_LENGTH
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('ab')", 2L)]
	[InlineData("LENGTH('abc')", 3L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("LENGTH('   ')", 3L)]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	[InlineData("CHAR_LENGTH('')", 0L)]
	[InlineData("CHAR_LENGTH('a')", 1L)]
	[InlineData("CHAR_LENGTH('abc')", 3L)]
	public async Task LengthFunctions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REGEXP functions comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("REGEXP_CONTAINS('abc', 'a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'b')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'c')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'abc')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'd')", false)]
	[InlineData("REGEXP_CONTAINS('abc', '^a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'c$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^abc$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '[a-z]+')", true)]
	[InlineData("REGEXP_CONTAINS('123', '[a-z]+')", false)]
	[InlineData("REGEXP_CONTAINS('abc123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('abc123', '^[0-9]+$')", false)]
	[InlineData("REGEXP_CONTAINS('', '')", true)]
	public async Task RegexpContains(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc123', '[a-z]+')", "abc")]
	[InlineData("REGEXP_EXTRACT('hello world', '[a-z]+')", "hello")]
	[InlineData("REGEXP_EXTRACT('abc', 'abc')", "abc")]
	[InlineData("REGEXP_EXTRACT('abc123def456', '[0-9]+')", "123")]
	public async Task RegexpExtract(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc', 'x')")]
	public async Task RegexpExtract_NoMatch_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("REGEXP_REPLACE('abc', 'b', 'X')", "aXc")]
	[InlineData("REGEXP_REPLACE('aabbcc', 'b+', 'X')", "aaXcc")]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]', '#')", "abc###")]
	[InlineData("REGEXP_REPLACE('abc', '[a-z]', 'X')", "XXX")]
	[InlineData("REGEXP_REPLACE('abc', 'x', 'X')", "abc")]
	[InlineData("REGEXP_REPLACE('hello world', 'world', 'earth')", "hello earth")]
	public async Task RegexpReplace(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// || operator
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("'' || ''", "")]
	[InlineData("'a' || ''", "a")]
	[InlineData("'' || 'a'", "a")]
	[InlineData("'abc' || 'def'", "abcdef")]
	[InlineData("'a' || 'b' || 'c'", "abc")]
	[InlineData("'a' || 'b' || 'c' || 'd'", "abcd")]
	[InlineData("UPPER('a') || LOWER('B')", "Ab")]
	public async Task ConcatOperator(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SPLIT comprehensive
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b', ','))", 2L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c,d,e', ','))", 5L)]
	[InlineData("ARRAY_LENGTH(SPLIT(',a,', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('', ','))", 1L)]
	public async Task SplitLength(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT for strings
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("FORMAT('%d', 0)", "0")]
	[InlineData("FORMAT('%d', 1)", "1")]
	[InlineData("FORMAT('%d', -1)", "-1")]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%d', 100)", "100")]
	[InlineData("FORMAT('%s', '')", "")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	public async Task FormatFunction(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);
}
