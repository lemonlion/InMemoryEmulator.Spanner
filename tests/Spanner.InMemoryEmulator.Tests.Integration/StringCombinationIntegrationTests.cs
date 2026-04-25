using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense string-function combination tests. Each [InlineData] is a separate test.
/// Tests additional edge cases, multi-function pipelines, and boundary conditions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringCombinationIntegrationTests : IntegrationTestBase
{
	public StringCombinationIntegrationTests(EmulatorSession session) : base(session) { }

	// Helper to evaluate a scalar expression
	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CONCAT edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT('')", "")]
	[InlineData("CONCAT('a')", "a")]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('a', 'b', 'c')", "abc")]
	[InlineData("CONCAT('a', 'b', 'c', 'd')", "abcd")]
	[InlineData("CONCAT('a', 'b', 'c', 'd', 'e')", "abcde")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('', '', '')", "")]
	[InlineData("CONCAT('a', '', 'b')", "ab")]
	[InlineData("CONCAT('  ', '  ')", "    ")]
	[InlineData("CONCAT('abc', 'def', 'ghi', 'jkl', 'mno', 'pqr', 'stu', 'vwx', 'yz')", "abcdefghijklmnopqrstuvwxyz")]
	[InlineData("CONCAT(CAST(1 AS STRING), CAST(2 AS STRING))", "12")]
	[InlineData("CONCAT(CAST(TRUE AS STRING))", "true")]
	public async Task Concat_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// LENGTH plus SUBSTR combinations
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH(' ')", 1L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('ab')", 2L)]
	[InlineData("LENGTH('abc')", 3L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("LENGTH(SUBSTR('abcdef', 2, 3))", 3L)]
	[InlineData("LENGTH(CONCAT('ab', 'cd'))", 4L)]
	[InlineData("LENGTH(UPPER('abc'))", 3L)]
	[InlineData("LENGTH(TRIM('  a  '))", 1L)]
	[InlineData("LENGTH(REPLACE('abcabc', 'b', ''))", 4L)]
	[InlineData("LENGTH(REVERSE('hello'))", 5L)]
	[InlineData("LENGTH(REPEAT('ab', 5))", 10L)]
	[InlineData("LENGTH(LPAD('x', 10, 'y'))", 10L)]
	[InlineData("LENGTH(RPAD('x', 10, 'y'))", 10L)]
	public async Task Length_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SUBSTR edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SUBSTR('abcdef', 1)", "abcdef")]
	[InlineData("SUBSTR('abcdef', 2)", "bcdef")]
	[InlineData("SUBSTR('abcdef', 6)", "f")]
	[InlineData("SUBSTR('abcdef', 1, 0)", "")]
	[InlineData("SUBSTR('abcdef', 1, 1)", "a")]
	[InlineData("SUBSTR('abcdef', 1, 3)", "abc")]
	[InlineData("SUBSTR('abcdef', 1, 6)", "abcdef")]
	[InlineData("SUBSTR('abcdef', 1, 100)", "abcdef")]
	[InlineData("SUBSTR('abcdef', 3, 2)", "cd")]
	[InlineData("SUBSTR('abcdef', -2)", "ef")]
	[InlineData("SUBSTR('abcdef', -2, 1)", "e")]
	[InlineData("SUBSTR('', 1)", "")]
	[InlineData("SUBSTR('a', 1, 0)", "")]
	[InlineData("SUBSTR('hello world', 7)", "world")]
	[InlineData("SUBSTR('hello world', 1, 5)", "hello")]
	[InlineData("SUBSTR(CONCAT('abc', 'def'), 2, 4)", "bcde")]
	[InlineData("SUBSTR(REVERSE('abcdef'), 1, 3)", "fed")]
	public async Task Substr_EdgeCases(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// REPLACE edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE('aaa', 'a', 'b')", "bbb")]
	[InlineData("REPLACE('abc', 'b', '')", "ac")]
	[InlineData("REPLACE('abc', 'd', 'x')", "abc")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('abc', '', 'x')", "abc")]
	[InlineData("REPLACE('aabbcc', 'bb', 'XX')", "aaXXcc")]
	[InlineData("REPLACE('hello world', 'world', 'there')", "hello there")]
	[InlineData("REPLACE('aaa', 'aa', 'b')", "ba")]
	[InlineData("REPLACE(UPPER('abc'), 'B', 'X')", "AXC")]
	[InlineData("REPLACE(REVERSE('abc'), 'c', 'z')", "zba")]
	public async Task Replace_EdgeCases(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// UPPER and LOWER chaining
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('a')", "A")]
	[InlineData("UPPER('abc')", "ABC")]
	[InlineData("UPPER('ABC')", "ABC")]
	[InlineData("UPPER('aBcDeF')", "ABCDEF")]
	[InlineData("UPPER('hello world')", "HELLO WORLD")]
	[InlineData("UPPER('123')", "123")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('A')", "a")]
	[InlineData("LOWER('ABC')", "abc")]
	[InlineData("LOWER('abc')", "abc")]
	[InlineData("LOWER('AbCdEf')", "abcdef")]
	[InlineData("LOWER('HELLO WORLD')", "hello world")]
	[InlineData("LOWER(UPPER('hello'))", "hello")]
	[InlineData("UPPER(LOWER('HELLO'))", "HELLO")]
	[InlineData("UPPER(CONCAT('ab', 'cd'))", "ABCD")]
	[InlineData("LOWER(CONCAT('AB', 'CD'))", "abcd")]
	[InlineData("LENGTH(UPPER('abc'))", 3L)]
	[InlineData("SUBSTR(UPPER('abcdef'), 1, 3)", "ABC")]
	public async Task UpperLower_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TRIM, LTRIM, RTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM(' ')", "")]
	[InlineData("TRIM('  ')", "")]
	[InlineData("TRIM('a')", "a")]
	[InlineData("TRIM(' a ')", "a")]
	[InlineData("TRIM('  abc  ')", "abc")]
	[InlineData("TRIM('  hello world  ')", "hello world")]
	[InlineData("LTRIM('')", "")]
	[InlineData("LTRIM(' abc')", "abc")]
	[InlineData("LTRIM('  abc  ')", "abc  ")]
	[InlineData("LTRIM('abc')", "abc")]
	[InlineData("RTRIM('')", "")]
	[InlineData("RTRIM('abc ')", "abc")]
	[InlineData("RTRIM('  abc  ')", "  abc")]
	[InlineData("RTRIM('abc')", "abc")]
	[InlineData("TRIM(CONCAT('  ', 'abc', '  '))", "abc")]
	[InlineData("LENGTH(TRIM('  abc  '))", 3L)]
	public async Task Trim_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// STRPOS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STRPOS('abc', 'a')", 1L)]
	[InlineData("STRPOS('abc', 'b')", 2L)]
	[InlineData("STRPOS('abc', 'c')", 3L)]
	[InlineData("STRPOS('abc', 'd')", 0L)]
	[InlineData("STRPOS('abc', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('abcabc', 'bc')", 2L)]
	[InlineData("STRPOS('hello world', 'world')", 7L)]
	[InlineData("STRPOS('hello world', 'xyz')", 0L)]
	[InlineData("STRPOS(UPPER('abc'), 'B')", 2L)]
	[InlineData("STRPOS(CONCAT('ab', 'cd'), 'bc')", 2L)]
	public async Task Strpos_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// STARTS_WITH and ENDS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STARTS_WITH('abc', 'a')", true)]
	[InlineData("STARTS_WITH('abc', 'ab')", true)]
	[InlineData("STARTS_WITH('abc', 'abc')", true)]
	[InlineData("STARTS_WITH('abc', 'b')", false)]
	[InlineData("STARTS_WITH('abc', '')", true)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("STARTS_WITH('abc', 'A')", false)]
	[InlineData("STARTS_WITH('hello world', 'hello')", true)]
	[InlineData("STARTS_WITH('hello world', 'world')", false)]
	[InlineData("ENDS_WITH('abc', 'c')", true)]
	[InlineData("ENDS_WITH('abc', 'bc')", true)]
	[InlineData("ENDS_WITH('abc', 'abc')", true)]
	[InlineData("ENDS_WITH('abc', 'a')", false)]
	[InlineData("ENDS_WITH('abc', '')", true)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('abc', 'C')", false)]
	[InlineData("ENDS_WITH('hello world', 'world')", true)]
	[InlineData("ENDS_WITH('hello world', 'hello')", false)]
	public async Task StartsEndsWith_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('ab')", "ba")]
	[InlineData("REVERSE('abc')", "cba")]
	[InlineData("REVERSE('hello')", "olleh")]
	[InlineData("REVERSE('12345')", "54321")]
	[InlineData("REVERSE(REVERSE('abc'))", "abc")]
	[InlineData("REVERSE(UPPER('abc'))", "CBA")]
	[InlineData("UPPER(REVERSE('abc'))", "CBA")]
	[InlineData("REVERSE(CONCAT('ab', 'cd'))", "dcba")]
	public async Task Reverse_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// REPEAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('a', 0)", "")]
	[InlineData("REPEAT('a', 1)", "a")]
	[InlineData("REPEAT('a', 3)", "aaa")]
	[InlineData("REPEAT('ab', 3)", "ababab")]
	[InlineData("REPEAT('abc', 2)", "abcabc")]
	[InlineData("REPEAT(' ', 3)", "   ")]
	[InlineData("LENGTH(REPEAT('ab', 10))", 20L)]
	[InlineData("REVERSE(REPEAT('ab', 3))", "bababa")]
	public async Task Repeat_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// LPAD and RPAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('abc', 5, 'x')", "xxabc")]
	[InlineData("LPAD('abc', 3, 'x')", "abc")]
	[InlineData("LPAD('abc', 1, 'x')", "a")]
	[InlineData("LPAD('abc', 0, 'x')", "")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("LPAD('abc', 6, 'xy')", "xyxabc")]
	[InlineData("LPAD('abc', 10, '0')", "0000000abc")]
	[InlineData("RPAD('abc', 5, 'x')", "abcxx")]
	[InlineData("RPAD('abc', 3, 'x')", "abc")]
	[InlineData("RPAD('abc', 1, 'x')", "a")]
	[InlineData("RPAD('abc', 0, 'x')", "")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('abc', 6, 'xy')", "abcxyx")]
	[InlineData("RPAD('abc', 10, '0')", "abc0000000")]
	[InlineData("LENGTH(LPAD('a', 10, 'x'))", 10L)]
	[InlineData("LENGTH(RPAD('a', 10, 'x'))", 10L)]
	public async Task LpadRpad_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SPLIT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,,b', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT(',a,b,', ','))", 4L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a|b|c', '|'))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('hello world', ' '))", 2L)]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// Empty delimiter splits into individual characters (no leading empty string).
	[InlineData("ARRAY_LENGTH(SPLIT('abc', ''))", 3L)]
	public async Task Split_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Complex multi-function pipelines
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(TRIM('  hello  '))", "HELLO")]
	[InlineData("LOWER(TRIM('  HELLO  '))", "hello")]
	[InlineData("REVERSE(UPPER('abc'))", "CBA")]
	[InlineData("LENGTH(TRIM(REPEAT(' ', 5)))", 0L)]
	[InlineData("SUBSTR(REVERSE('abcdef'), 1, 3)", "fed")]
	[InlineData("REPLACE(UPPER('hello'), 'L', 'r')", "HErrO")]
	[InlineData("CONCAT(UPPER('a'), LOWER('B'), REVERSE('cd'))", "Abdc")]
	[InlineData("STRPOS(UPPER('abcdef'), 'CD')", 3L)]
	[InlineData("STARTS_WITH(LOWER('ABC'), 'ab')", true)]
	[InlineData("ENDS_WITH(UPPER('hello'), 'LLO')", true)]
	[InlineData("LENGTH(REPLACE('aaaa', 'a', 'bb'))", 8L)]
	[InlineData("SUBSTR(LPAD('abc', 8, '0'), 1, 5)", "00000")]
	[InlineData("REVERSE(LPAD('x', 5, 'ab'))", "xbaba")]
	[InlineData("TRIM(CONCAT('  ', 'hello', '  '))", "hello")]
	[InlineData("UPPER(REVERSE(LOWER('ABC')))", "CBA")]
	[InlineData("LENGTH(CONCAT(REPEAT('a', 10), REPEAT('b', 10)))", 20L)]
	[InlineData("REPLACE(REPEAT('ab', 3), 'ba', 'X')", "aXXb")]
	[InlineData("SUBSTR(REPEAT('abc', 3), 4, 3)", "abc")]
	[InlineData("STRPOS(REPEAT('abc', 3), 'cab')", 3L)]
	[InlineData("STARTS_WITH(REPEAT('abc', 3), 'abcabc')", true)]
	[InlineData("ENDS_WITH(REPEAT('abc', 3), 'abcabc')", true)]
	public async Task Pipeline_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// String concatenation operator ||
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'a' || 'b'", "ab")]
	[InlineData("'a' || 'b' || 'c'", "abc")]
	[InlineData("'' || ''", "")]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("UPPER('a' || 'b')", "AB")]
	[InlineData("'a' || UPPER('b')", "aB")]
	[InlineData("REVERSE('a' || 'b' || 'c')", "cba")]
	[InlineData("LENGTH('abc' || 'def')", 6L)]
	public async Task StringConcat_Operator(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_CONTAINS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_CONTAINS('abc', 'a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'b')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'd')", false)]
	[InlineData("REGEXP_CONTAINS('abc', '^a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'c$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^abc$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '[a-z]+')", true)]
	[InlineData("REGEXP_CONTAINS('123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '[0-9]')", false)]
	[InlineData("REGEXP_CONTAINS('', '')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '.*')", true)]
	[InlineData("REGEXP_CONTAINS('hello world', 'hello')", true)]
	[InlineData("REGEXP_CONTAINS('hello world', '^hello world$')", true)]
	[InlineData("REGEXP_CONTAINS('aab', 'a+b')", true)]
	[InlineData("REGEXP_CONTAINS('b', 'a+b')", false)]
	[InlineData("REGEXP_CONTAINS('abc123', '[a-z]+[0-9]+')", true)]
	public async Task RegexpContains_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_EXTRACT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc123def456', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('hello world', '[a-z]+')", "hello")]
	[InlineData("REGEXP_EXTRACT('abc', '(b)')", "b")]
	[InlineData("REGEXP_EXTRACT('abc', '(a)(b)')", "a")]
	public async Task RegexpExtract_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("REGEXP_EXTRACT('xyz', '[0-9]+')")]
	public async Task RegexpExtract_NoMatch_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_REPLACE('abc', 'b', 'X')", "aXc")]
	[InlineData("REGEXP_REPLACE('aabbcc', 'b+', 'X')", "aaXcc")]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]+', '')", "abc")]
	[InlineData("REGEXP_REPLACE('hello world', 'world', 'there')", "hello there")]
	[InlineData("REGEXP_REPLACE('abc', '[a-z]', 'X')", "XXX")]
	[InlineData("REGEXP_REPLACE('abc', 'x', 'Y')", "abc")]
	[InlineData("REGEXP_REPLACE('', 'a', 'b')", "")]
	public async Task RegexpReplace_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// INITCAP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#initcap
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("INITCAP('')", "")]
	[InlineData("INITCAP('hello')", "Hello")]
	[InlineData("INITCAP('hello world')", "Hello World")]
	[InlineData("INITCAP('HELLO')", "Hello")]
	[InlineData("INITCAP('HELLO WORLD')", "Hello World")]
	[InlineData("INITCAP('aBC dEF')", "Abc Def")]
	[InlineData("INITCAP('hello-world')", "Hello-World")]
	[InlineData("INITCAP('hello_world')", "Hello_World")]
	[InlineData("INITCAP('  hello  world  ')", "  Hello  World  ")]
	[InlineData("INITCAP('a')", "A")]
	[InlineData("INITCAP('123abc')", "123Abc")]
	public async Task Initcap_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// ASCII and CHR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ascii
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ASCII('A')", 65L)]
	[InlineData("ASCII('a')", 97L)]
	[InlineData("ASCII('0')", 48L)]
	[InlineData("ASCII(' ')", 32L)]
	[InlineData("ASCII('Z')", 90L)]
	[InlineData("ASCII('z')", 122L)]
	[InlineData("ASCII('abc')", 97L)]
	public async Task Ascii_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CHR(65)", "A")]
	[InlineData("CHR(97)", "a")]
	[InlineData("CHR(48)", "0")]
	[InlineData("CHR(32)", " ")]
	[InlineData("CHR(90)", "Z")]
	[InlineData("CHR(122)", "z")]
	public async Task Chr_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CHR(ASCII('A'))", "A")]
	[InlineData("CHR(ASCII('z'))", "z")]
	[InlineData("ASCII(CHR(65))", 65L)]
	[InlineData("ASCII(CHR(97))", 97L)]
	public async Task AsciiChr_RoundTrip(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// BYTE_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	[InlineData("BYTE_LENGTH(' ')", 1L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	public async Task ByteLength_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TO_HEX and FROM_HEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0f')", "0f")]
	[InlineData("TO_HEX(b'\\xab\\xcd')", "abcd")]
	public async Task ToHex_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// LEFT and RIGHT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#left
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LEFT('hello', 0)", "")]
	[InlineData("LEFT('hello', 1)", "h")]
	[InlineData("LEFT('hello', 3)", "hel")]
	[InlineData("LEFT('hello', 5)", "hello")]
	[InlineData("LEFT('hello', 10)", "hello")]
	[InlineData("LEFT('', 3)", "")]
	[InlineData("RIGHT('hello', 0)", "")]
	[InlineData("RIGHT('hello', 1)", "o")]
	[InlineData("RIGHT('hello', 3)", "llo")]
	[InlineData("RIGHT('hello', 5)", "hello")]
	[InlineData("RIGHT('hello', 10)", "hello")]
	[InlineData("RIGHT('', 3)", "")]
	[InlineData("LEFT(UPPER('hello'), 3)", "HEL")]
	[InlineData("RIGHT(REVERSE('hello'), 3)", "leh")]
	public async Task LeftRight_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// NULL propagation through all string functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT(CAST(NULL AS STRING))")]
	[InlineData("LENGTH(CAST(NULL AS STRING))")]
	[InlineData("UPPER(CAST(NULL AS STRING))")]
	[InlineData("LOWER(CAST(NULL AS STRING))")]
	[InlineData("TRIM(CAST(NULL AS STRING))")]
	[InlineData("LTRIM(CAST(NULL AS STRING))")]
	[InlineData("RTRIM(CAST(NULL AS STRING))")]
	[InlineData("SUBSTR(CAST(NULL AS STRING), 1)")]
	[InlineData("REPLACE(CAST(NULL AS STRING), 'a', 'b')")]
	[InlineData("REPLACE('abc', CAST(NULL AS STRING), 'b')")]
	[InlineData("STRPOS(CAST(NULL AS STRING), 'a')")]
	[InlineData("STRPOS('abc', CAST(NULL AS STRING))")]
	[InlineData("STARTS_WITH(CAST(NULL AS STRING), 'a')")]
	[InlineData("ENDS_WITH(CAST(NULL AS STRING), 'a')")]
	[InlineData("REVERSE(CAST(NULL AS STRING))")]
	[InlineData("REPEAT(CAST(NULL AS STRING), 3)")]
	[InlineData("LPAD(CAST(NULL AS STRING), 5, 'x')")]
	[InlineData("RPAD(CAST(NULL AS STRING), 5, 'x')")]
	[InlineData("LEFT(CAST(NULL AS STRING), 3)")]
	[InlineData("RIGHT(CAST(NULL AS STRING), 3)")]
	[InlineData("REGEXP_CONTAINS(CAST(NULL AS STRING), 'a')")]
	[InlineData("REGEXP_EXTRACT(CAST(NULL AS STRING), '[a-z]+')")]
	[InlineData("REGEXP_REPLACE(CAST(NULL AS STRING), 'a', 'b')")]
	[InlineData("INITCAP(CAST(NULL AS STRING))")]
	[InlineData("ASCII(CAST(NULL AS STRING))")]
	[InlineData("BYTE_LENGTH(CAST(NULL AS STRING))")]
	public async Task StringFunction_NullInput_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Deep nesting (5+ levels)
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(LOWER(UPPER(LOWER(UPPER('hello')))))", "HELLO")]
	[InlineData("REVERSE(REVERSE(REVERSE(REVERSE('abcd'))))", "abcd")]
	[InlineData("TRIM(CONCAT('  ', TRIM(CONCAT('  ', 'abc', '  ')), '  '))", "abc")]
	[InlineData("LENGTH(CONCAT(REPEAT('a', 5), REPEAT('b', 5), REPEAT('c', 5)))", 15L)]
	[InlineData("SUBSTR(REPLACE(UPPER(REVERSE('abcdef')), 'E', 'X'), 1, 4)", "FXDC")]
	public async Task DeepNesting_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// FORMAT function for strings
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%d', -1)", "-1")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%d + %d = %d', 1, 2, 3)", "1 + 2 = 3")]
	[InlineData("FORMAT('%.2f', 3.14159)", "3.14")]
	[InlineData("FORMAT('%05d', 42)", "00042")]
	public async Task Format_StringCombinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);
}
