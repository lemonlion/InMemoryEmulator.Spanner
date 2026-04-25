using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for all string SQL functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionExtendedIntegrationTests : IntegrationTestBase
{
	public StringFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CONCAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('a', '')", "a")]
	[InlineData("CONCAT('', 'b')", "b")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('a', 'b', 'c', 'd', 'e')", "abcde")]
	[InlineData("CONCAT('Hello')", "Hello")]
	[InlineData("CONCAT('abc', 'def', 'ghi')", "abcdefghi")]
	[InlineData("CONCAT('1', '2', '3')", "123")]
	[InlineData("CONCAT(' ', ' ')", "  ")]
	[InlineData("CONCAT('a', 'b', 'c', 'd', 'e', 'f', 'g')", "abcdefg")]
	[InlineData("CONCAT('line1\\n', 'line2')", "line1\\nline2")]
	public async Task Concat_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CONCAT(NULL, 'a')")]
	[InlineData("CONCAT('a', NULL)")]
	[InlineData("CONCAT(NULL, NULL)")]
	[InlineData("CONCAT('a', NULL, 'b')")]
	public async Task Concat_WithNull_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LENGTH / CHAR_LENGTH / CHARACTER_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('hello')", 5L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("LENGTH('   ')", 3L)]
	[InlineData("LENGTH('abc123')", 6L)]
	[InlineData("CHAR_LENGTH('hello')", 5L)]
	[InlineData("CHARACTER_LENGTH('test')", 4L)]
	[InlineData("LENGTH('a b c')", 5L)]
	[InlineData("LENGTH('12345678901234567890')", 20L)]
	public async Task Length_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Length_Null_ReturnsNull()
		=> (await Eval("LENGTH(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// UPPER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#upper
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER('hello')", "HELLO")]
	[InlineData("UPPER('HELLO')", "HELLO")]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('Hello World')", "HELLO WORLD")]
	[InlineData("UPPER('abc123')", "ABC123")]
	[InlineData("UPPER('a')", "A")]
	[InlineData("UPPER('hello world foo bar')", "HELLO WORLD FOO BAR")]
	[InlineData("UPPER('test123test')", "TEST123TEST")]
	[InlineData("UPPER('MiXeD')", "MIXED")]
	[InlineData("UPPER('   spaces   ')", "   SPACES   ")]
	public async Task Upper_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Upper_Null_ReturnsNull()
		=> (await Eval("UPPER(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LOWER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lower
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LOWER('HELLO')", "hello")]
	[InlineData("LOWER('hello')", "hello")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('Hello World')", "hello world")]
	[InlineData("LOWER('ABC123')", "abc123")]
	[InlineData("LOWER('A')", "a")]
	[InlineData("LOWER('HELLO WORLD FOO BAR')", "hello world foo bar")]
	[InlineData("LOWER('MiXeD')", "mixed")]
	[InlineData("LOWER('   SPACES   ')", "   spaces   ")]
	[InlineData("LOWER('TEST123')", "test123")]
	public async Task Lower_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Lower_Null_ReturnsNull()
		=> (await Eval("LOWER(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// TRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRIM('  hello  ')", "hello")]
	[InlineData("TRIM('hello')", "hello")]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM('   ')", "")]
	[InlineData("TRIM('  a  ')", "a")]
	[InlineData("TRIM('\\thello\\t')", "\\thello\\t")]
	[InlineData("TRIM('  hello world  ')", "hello world")]
	[InlineData("TRIM(' x ')", "x")]
	[InlineData("TRIM('  multiple  words  ')", "multiple  words")]
	public async Task Trim_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Trim_Null_ReturnsNull()
		=> (await Eval("TRIM(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ltrim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LTRIM('  hello')", "hello")]
	[InlineData("LTRIM('hello')", "hello")]
	[InlineData("LTRIM('')", "")]
	[InlineData("LTRIM('   ')", "")]
	[InlineData("LTRIM('  hello  ')", "hello  ")]
	[InlineData("LTRIM(' a')", "a")]
	[InlineData("LTRIM('   multiple spaces')", "multiple spaces")]
	[InlineData("LTRIM('x')", "x")]
	public async Task Ltrim_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Ltrim_Null_ReturnsNull()
		=> (await Eval("LTRIM(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// RTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rtrim
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("RTRIM('hello  ')", "hello")]
	[InlineData("RTRIM('hello')", "hello")]
	[InlineData("RTRIM('')", "")]
	[InlineData("RTRIM('   ')", "")]
	[InlineData("RTRIM('  hello  ')", "  hello")]
	[InlineData("RTRIM('a ')", "a")]
	[InlineData("RTRIM('trailing spaces   ')", "trailing spaces")]
	[InlineData("RTRIM('x')", "x")]
	public async Task Rtrim_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Rtrim_Null_ReturnsNull()
		=> (await Eval("RTRIM(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// SUBSTR / SUBSTRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	// Position is 1-based.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SUBSTR('hello', 1)", "hello")]
	[InlineData("SUBSTR('hello', 2)", "ello")]
	[InlineData("SUBSTR('hello', 5)", "o")]
	[InlineData("SUBSTR('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello', 2, 3)", "ell")]
	[InlineData("SUBSTR('hello', 1, 0)", "")]
	[InlineData("SUBSTR('hello', 1, 5)", "hello")]
	[InlineData("SUBSTR('hello', 1, 10)", "hello")]
	[InlineData("SUBSTR('hello', 3, 2)", "ll")]
	[InlineData("SUBSTR('a', 1)", "a")]
	[InlineData("SUBSTR('a', 1, 1)", "a")]
	[InlineData("SUBSTR('abcdef', 4)", "def")]
	[InlineData("SUBSTR('abcdef', 2, 4)", "bcde")]
	[InlineData("SUBSTRING('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello world', 7)", "world")]
	[InlineData("SUBSTR('hello world', 1, 5)", "hello")]
	[InlineData("SUBSTR('hello', 6)", "")]
	public async Task Substr_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Substr_Null_ReturnsNull()
		=> (await Eval("SUBSTR(NULL, 1)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPLACE('hello', 'ell', 'ELL')", "hELLo")]
	[InlineData("REPLACE('hello', 'x', 'y')", "hello")]
	[InlineData("REPLACE('aaa', 'a', 'bb')", "bbbbbb")]
	[InlineData("REPLACE('hello', 'hello', '')", "")]
	[InlineData("REPLACE('hello', '', 'x')", "hello")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('abcabc', 'abc', 'X')", "XX")]
	[InlineData("REPLACE('hello world', ' ', '-')", "hello-world")]
	[InlineData("REPLACE('aabbcc', 'bb', 'BB')", "aaBBcc")]
	[InlineData("REPLACE('test', 'test', 'TEST')", "TEST")]
	[InlineData("REPLACE('abcdef', 'cd', '')", "abef")]
	[InlineData("REPLACE('xxx', 'x', 'yy')", "yyyyyy")]
	public async Task Replace_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Replace_Null_ReturnsNull()
		=> (await Eval("REPLACE(NULL, 'a', 'b')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REVERSE('abc')", "cba")]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('abcdef')", "fedcba")]
	[InlineData("REVERSE('racecar')", "racecar")]
	[InlineData("REVERSE('12345')", "54321")]
	[InlineData("REVERSE('hello world')", "dlrow olleh")]
	[InlineData("REVERSE('ab')", "ba")]
	[InlineData("REVERSE('  ab  ')", "  ba  ")]
	public async Task Reverse_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Reverse_Null_ReturnsNull()
		=> (await Eval("REVERSE(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// STRPOS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
	// Returns 1-based position, 0 if not found.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STRPOS('hello', 'h')", 1L)]
	[InlineData("STRPOS('hello', 'e')", 2L)]
	[InlineData("STRPOS('hello', 'llo')", 3L)]
	[InlineData("STRPOS('hello', 'o')", 5L)]
	[InlineData("STRPOS('hello', 'x')", 0L)]
	[InlineData("STRPOS('hello', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('aaa', 'a')", 1L)]
	[InlineData("STRPOS('hello', 'hello')", 1L)]
	[InlineData("STRPOS('hello', 'hello world')", 0L)]
	[InlineData("STRPOS('abcabc', 'abc')", 1L)]
	public async Task Strpos_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Strpos_Null_ReturnsNull()
		=> (await Eval("STRPOS(NULL, 'a')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// STARTS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("STARTS_WITH('hello', 'hel')", true)]
	[InlineData("STARTS_WITH('hello', 'h')", true)]
	[InlineData("STARTS_WITH('hello', 'hello')", true)]
	[InlineData("STARTS_WITH('hello', '')", true)]
	[InlineData("STARTS_WITH('hello', 'x')", false)]
	[InlineData("STARTS_WITH('hello', 'ello')", false)]
	[InlineData("STARTS_WITH('hello', 'Hello')", false)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("STARTS_WITH('abc', 'abc')", true)]
	[InlineData("STARTS_WITH('abc', 'abcd')", false)]
	public async Task StartsWith_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task StartsWith_Null_ReturnsNull()
		=> (await Eval("STARTS_WITH(NULL, 'a')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// ENDS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ends_with
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ENDS_WITH('hello', 'llo')", true)]
	[InlineData("ENDS_WITH('hello', 'o')", true)]
	[InlineData("ENDS_WITH('hello', 'hello')", true)]
	[InlineData("ENDS_WITH('hello', '')", true)]
	[InlineData("ENDS_WITH('hello', 'x')", false)]
	[InlineData("ENDS_WITH('hello', 'hel')", false)]
	[InlineData("ENDS_WITH('hello', 'LLO')", false)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('abc', 'abc')", true)]
	[InlineData("ENDS_WITH('abc', 'xabc')", false)]
	public async Task EndsWith_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task EndsWith_Null_ReturnsNull()
		=> (await Eval("ENDS_WITH(NULL, 'a')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LPAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('hi', 5, '-')", "---hi")]
	[InlineData("LPAD('hi', 2, '-')", "hi")]
	[InlineData("LPAD('hi', 1, '-')", "h")]
	[InlineData("LPAD('hi', 0, '-')", "")]
	[InlineData("LPAD('hi', 10, 'ab')", "ababababhi")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("LPAD('hello', 5, 'x')", "hello")]
	[InlineData("LPAD('hello', 8, '12')", "121hello")]
	[InlineData("LPAD('a', 5, 'xyz')", "xyzxa")]
	[InlineData("LPAD('test', 6, '*')", "**test")]
	public async Task Lpad_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Lpad_Null_ReturnsNull()
		=> (await Eval("LPAD(NULL, 5, 'x')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// RPAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("RPAD('hi', 5, '-')", "hi---")]
	[InlineData("RPAD('hi', 2, '-')", "hi")]
	[InlineData("RPAD('hi', 1, '-')", "h")]
	[InlineData("RPAD('hi', 0, '-')", "")]
	[InlineData("RPAD('hi', 10, 'ab')", "hiabababab")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('hello', 5, 'x')", "hello")]
	[InlineData("RPAD('hello', 8, '12')", "hello121")]
	[InlineData("RPAD('a', 5, 'xyz')", "axyzx")]
	[InlineData("RPAD('test', 6, '*')", "test**")]
	public async Task Rpad_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Rpad_Null_ReturnsNull()
		=> (await Eval("RPAD(NULL, 5, 'x')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REPEAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REPEAT('ab', 3)", "ababab")]
	[InlineData("REPEAT('x', 1)", "x")]
	[InlineData("REPEAT('x', 0)", "")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('hello', 2)", "hellohello")]
	[InlineData("REPEAT('a', 10)", "aaaaaaaaaa")]
	[InlineData("REPEAT('ab', 0)", "")]
	[InlineData("REPEAT('xyz', 1)", "xyz")]
	public async Task Repeat_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Repeat_Null_ReturnsNull()
		=> (await Eval("REPEAT(NULL, 3)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LEFT / RIGHT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#left
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LEFT('hello', 3)", "hel")]
	[InlineData("LEFT('hello', 0)", "")]
	[InlineData("LEFT('hello', 5)", "hello")]
	[InlineData("LEFT('hello', 10)", "hello")]
	[InlineData("LEFT('hello', 1)", "h")]
	[InlineData("LEFT('', 3)", "")]
	[InlineData("LEFT('a', 1)", "a")]
	[InlineData("LEFT('abcdef', 4)", "abcd")]
	public async Task Left_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("RIGHT('hello', 3)", "llo")]
	[InlineData("RIGHT('hello', 0)", "")]
	[InlineData("RIGHT('hello', 5)", "hello")]
	[InlineData("RIGHT('hello', 10)", "hello")]
	[InlineData("RIGHT('hello', 1)", "o")]
	[InlineData("RIGHT('', 3)", "")]
	[InlineData("RIGHT('a', 1)", "a")]
	[InlineData("RIGHT('abcdef', 4)", "cdef")]
	public async Task Right_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Left_Null_ReturnsNull() => (await Eval("LEFT(NULL, 3)")).Should().BeNull();
	[Fact]
	public async Task Right_Null_ReturnsNull() => (await Eval("RIGHT(NULL, 3)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// INITCAP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#initcap
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("INITCAP('hello world')", "Hello World")]
	[InlineData("INITCAP('HELLO WORLD')", "Hello World")]
	[InlineData("INITCAP('hello')", "Hello")]
	[InlineData("INITCAP('')", "")]
	[InlineData("INITCAP('a')", "A")]
	[InlineData("INITCAP('hello-world')", "Hello-World")]
	[InlineData("INITCAP('one two three')", "One Two Three")]
	[InlineData("INITCAP('already Capitalized')", "Already Capitalized")]
	public async Task Initcap_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Initcap_Null_ReturnsNull()
		=> (await Eval("INITCAP(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_CONTAINS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_CONTAINS('hello123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '[0-9]+')", false)]
	[InlineData("REGEXP_CONTAINS('abc', 'abc')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^abc$')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '^ab$')", false)]
	[InlineData("REGEXP_CONTAINS('hello world', 'world')", true)]
	[InlineData("REGEXP_CONTAINS('hello world', '^hello')", true)]
	[InlineData("REGEXP_CONTAINS('hello world', 'world$')", true)]
	[InlineData("REGEXP_CONTAINS('', '')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '.')", true)]
	[InlineData("REGEXP_CONTAINS('abc', '[xyz]')", false)]
	[InlineData("REGEXP_CONTAINS('abc', '[a-c]')", true)]
	[InlineData("REGEXP_CONTAINS('test@email.com', '@')", true)]
	[InlineData("REGEXP_CONTAINS('test', '(test|foo)')", true)]
	[InlineData("REGEXP_CONTAINS('foo', '(test|foo)')", true)]
	[InlineData("REGEXP_CONTAINS('bar', '(test|foo)')", false)]
	public async Task RegexpContains_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task RegexpContains_Null_ReturnsNull()
		=> (await Eval("REGEXP_CONTAINS(NULL, 'a')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_EXTRACT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_EXTRACT('hello123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc', 'abc')", "abc")]
	[InlineData("REGEXP_EXTRACT('hello world', '[a-z]+')", "hello")]
	[InlineData("REGEXP_EXTRACT('test123end', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('2024-01-15', '[0-9]{4}')", "2024")]
	[InlineData("REGEXP_EXTRACT('no match here', '[0-9]+')", null)]
	public async Task RegexpExtract_ReturnsExpected(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]+', 'X')", "abcX")]
	[InlineData("REGEXP_REPLACE('hello world', ' ', '-')", "hello-world")]
	[InlineData("REGEXP_REPLACE('aaa', 'a', 'b')", "bbb")]
	[InlineData("REGEXP_REPLACE('hello', '[aeiou]', '*')", "h*ll*")]
	[InlineData("REGEXP_REPLACE('abc', 'xyz', 'X')", "abc")]
	[InlineData("REGEXP_REPLACE('', 'a', 'b')", "")]
	[InlineData("REGEXP_REPLACE('test123test456', '[0-9]+', '#')", "test#test#")]
	[InlineData("REGEXP_REPLACE('a1b2c3', '[0-9]', '')", "abc")]
	public async Task RegexpReplace_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task RegexpReplace_Null_ReturnsNull()
		=> (await Eval("REGEXP_REPLACE(NULL, 'a', 'b')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// SPLIT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// Tested via ARRAY_TO_STRING(SPLIT(...)) to get scalar result.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_TO_STRING(SPLIT('a,b,c', ','), '|')", "a|b|c")]
	[InlineData("ARRAY_TO_STRING(SPLIT('hello', ','), '|')", "hello")]
	[InlineData("ARRAY_TO_STRING(SPLIT('a,,b', ','), '|')", "a||b")]
	[InlineData("ARRAY_TO_STRING(SPLIT('a-b-c', '-'), ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(SPLIT('one::two::three', '::'), '-')", "one-two-three")]
	[InlineData("ARRAY_TO_STRING(SPLIT('abc', ''), '|')", "a|b|c")]
	[InlineData("ARRAY_TO_STRING(SPLIT('x', 'x'), '|')", "|")]
	public async Task Split_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_TO_STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], '')", "abc")]
	[InlineData("ARRAY_TO_STRING(['a'], ',')", "a")]
	[InlineData("ARRAY_TO_STRING(['hello', 'world'], ' ')", "hello world")]
	[InlineData("ARRAY_TO_STRING(['1', '2', '3'], '-')", "1-2-3")]
	public async Task ArrayToString_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// ASCII
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ascii
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ASCII('A')", 65L)]
	[InlineData("ASCII('a')", 97L)]
	[InlineData("ASCII('0')", 48L)]
	[InlineData("ASCII(' ')", 32L)]
	[InlineData("ASCII('Z')", 90L)]
	[InlineData("ASCII('z')", 122L)]
	[InlineData("ASCII('!')", 33L)]
	public async Task Ascii_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CHR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#chr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CHR(65)", "A")]
	[InlineData("CHR(97)", "a")]
	[InlineData("CHR(48)", "0")]
	[InlineData("CHR(32)", " ")]
	[InlineData("CHR(90)", "Z")]
	[InlineData("CHR(122)", "z")]
	[InlineData("CHR(33)", "!")]
	public async Task Chr_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// BYTE_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	public async Task ByteLength_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TO_HEX / FROM_HEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	[InlineData("TO_HEX(b'abc')", "616263")]
	public async Task ToHex_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// INSTR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#instr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("INSTR('hello', 'l')", 3L)]
	[InlineData("INSTR('hello', 'x')", 0L)]
	[InlineData("INSTR('hello', 'h')", 1L)]
	[InlineData("INSTR('hello', 'o')", 5L)]
	[InlineData("INSTR('hello', 'hello')", 1L)]
	[InlineData("INSTR('abcabc', 'abc')", 1L)]
	[InlineData("INSTR('', 'a')", 0L)]
	public async Task Instr_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SOUNDEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#soundex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SOUNDEX('Robert')", "R163")]
	[InlineData("SOUNDEX('Rupert')", "R163")]
	[InlineData("SOUNDEX('Smith')", "S530")]
	[InlineData("SOUNDEX('Smythe')", "S530")]
	public async Task Soundex_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CONTAINS_SUBSTR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#contains_substr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONTAINS_SUBSTR('hello world', 'world')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'WORLD')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'xyz')", false)]
	[InlineData("CONTAINS_SUBSTR('hello', 'hello')", true)]
	[InlineData("CONTAINS_SUBSTR('hello', '')", true)]
	[InlineData("CONTAINS_SUBSTR('', 'a')", false)]
	[InlineData("CONTAINS_SUBSTR('Hello World', 'hello')", true)]
	[InlineData("CONTAINS_SUBSTR('ABC', 'abc')", true)]
	[InlineData("CONTAINS_SUBSTR('abc', 'ABC')", true)]
	public async Task ContainsSubstr_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TRANSLATE
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRANSLATE('hello', 'el', 'ip')", "hippo")]
	[InlineData("TRANSLATE('abc', 'abc', 'xyz')", "xyz")]
	[InlineData("TRANSLATE('hello', '', '')", "hello")]
	[InlineData("TRANSLATE('', 'a', 'b')", "")]
	public async Task Translate_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// String concatenation operator ||
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("'a' || 'b'", "ab")]
	[InlineData("'' || ''", "")]
	[InlineData("'x' || ''", "x")]
	[InlineData("'' || 'y'", "y")]
	[InlineData("'abc' || 'def' || 'ghi'", "abcdefghi")]
	[InlineData("'1' || '2' || '3' || '4'", "1234")]
	public async Task StringConcat_Operator_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("NULL || 'a'")]
	[InlineData("'a' || NULL")]
	[InlineData("NULL || NULL")]
	public async Task StringConcat_Operator_Null_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Nested / Combined string functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(CONCAT('hello', ' ', 'world'))", "HELLO WORLD")]
	[InlineData("LENGTH(CONCAT('a', 'bc', 'def'))", 6L)]
	[InlineData("REVERSE(UPPER('abc'))", "CBA")]
	[InlineData("LOWER(REVERSE('ABC'))", "cba")]
	[InlineData("SUBSTR(UPPER('hello'), 1, 3)", "HEL")]
	[InlineData("LENGTH(REPLACE('aaa', 'a', 'bb'))", 6L)]
	[InlineData("UPPER(LEFT('hello', 3))", "HEL")]
	[InlineData("LOWER(RIGHT('HELLO', 3))", "llo")]
	[InlineData("LENGTH(TRIM('  hi  '))", 2L)]
	[InlineData("REVERSE(REVERSE('test'))", "test")]
	[InlineData("UPPER(LOWER('HELLO'))", "HELLO")]
	[InlineData("CONCAT(LEFT('hello', 2), RIGHT('world', 3))", "herld")]
	[InlineData("SUBSTR(CONCAT('abc', 'def'), 2, 4)", "bcde")]
	[InlineData("REPLACE(UPPER('hello'), 'LL', 'rr')", "HErrO")]
	public async Task NestedStringFunctions_ReturnsExpected(string expr, object expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Extended NULL propagation for all string functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UPPER(NULL)")]
	[InlineData("LOWER(NULL)")]
	[InlineData("TRIM(NULL)")]
	[InlineData("LTRIM(NULL)")]
	[InlineData("RTRIM(NULL)")]
	[InlineData("SUBSTR(NULL, 1)")]
	[InlineData("REPLACE(NULL, 'a', 'b')")]
	[InlineData("REVERSE(NULL)")]
	[InlineData("STRPOS(NULL, 'a')")]
	[InlineData("STRPOS('a', NULL)")]
	[InlineData("STARTS_WITH(NULL, 'a')")]
	[InlineData("ENDS_WITH(NULL, 'a')")]
	[InlineData("LPAD(NULL, 5, 'x')")]
	[InlineData("RPAD(NULL, 5, 'x')")]
	[InlineData("REPEAT(NULL, 3)")]
	[InlineData("LEFT(NULL, 3)")]
	[InlineData("RIGHT(NULL, 3)")]
	[InlineData("INITCAP(NULL)")]
	[InlineData("REGEXP_CONTAINS(NULL, 'a')")]
	[InlineData("REGEXP_EXTRACT(NULL, 'a')")]
	[InlineData("REGEXP_REPLACE(NULL, 'a', 'b')")]
	[InlineData("LENGTH(NULL)")]
	[InlineData("BYTE_LENGTH(NULL)")]
	[InlineData("CONCAT(NULL, 'a')")]
	public async Task StringFunction_NullPropagation(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LPAD / RPAD with default padding (space)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LPAD('hi', 5)", "   hi")]
	[InlineData("RPAD('hi', 5)", "hi   ")]
	[InlineData("LPAD('hello', 5)", "hello")]
	[InlineData("RPAD('hello', 5)", "hello")]
	public async Task PadWithDefaultSpace_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Edge cases: very long strings, repeated operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Repeat_LargeCount_Works()
		=> ((string)(await Eval("REPEAT('a', 100)"))!).Length.Should().Be(100);

	[Fact]
	public async Task Concat_ManyArguments_Works()
	{
		var result = await Eval("CONCAT('a','b','c','d','e','f','g','h','i','j')");
		result.Should().Be("abcdefghij");
	}

	[Fact]
	public async Task Substr_BeyondLength_ReturnsEmpty()
		=> (await Eval("SUBSTR('hi', 10, 5)")).Should().Be("");

	[Fact]
	public async Task Replace_NoOccurrence_ReturnsOriginal()
		=> (await Eval("REPLACE('hello', 'xyz', 'abc')")).Should().Be("hello");

	[Fact]
	public async Task Replace_EmptySource_ReturnsEmpty()
		=> (await Eval("REPLACE('', 'a', 'b')")).Should().Be("");

	[Fact]
	public async Task Strpos_EmptySource_ReturnsZero()
		=> (await Eval("STRPOS('', 'a')")).Should().Be(0L);

	[Fact]
	public async Task Length_SingleChar_ReturnsOne()
		=> (await Eval("LENGTH('x')")).Should().Be(1L);
}
