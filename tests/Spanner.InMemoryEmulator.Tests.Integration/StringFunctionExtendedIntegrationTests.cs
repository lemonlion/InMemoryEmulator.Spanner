using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CONCAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Concat_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CONCAT(NULL, 'a')")]
	[InlineData("CONCAT('a', NULL)")]
	[InlineData("CONCAT(NULL, NULL)")]
	[InlineData("CONCAT('a', NULL, 'b')")]
	public async Task Concat_WithNull_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LENGTH / CHAR_LENGTH / CHARACTER_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UPPER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#upper
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LOWER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lower
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Trim_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Trim_Null_ReturnsNull()
		=> (await Eval("TRIM(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ltrim
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// RTRIM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rtrim
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SUBSTR / SUBSTRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	// Position is 1-based.
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// STRPOS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
	// Returns 1-based position, 0 if not found.
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// STARTS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ENDS_WITH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#ends_with
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LPAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// RPAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rpad
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REPEAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	[Theory]
	[InlineData("REGEXP_EXTRACT('no match here', '[0-9]+')", null)]
	public async Task RegexpExtract_ReturnsExpected(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REGEXP_REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SPLIT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// Tested via ARRAY_TO_STRING(SPLIT(...)) to get scalar result.
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ARRAY_TO_STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], '')", "abc")]
	[InlineData("ARRAY_TO_STRING(['a'], ',')", "a")]
	[InlineData("ARRAY_TO_STRING(['hello', 'world'], ' ')", "hello world")]
	[InlineData("ARRAY_TO_STRING(['1', '2', '3'], '-')", "1-2-3")]
	public async Task ArrayToString_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// BYTE_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	public async Task ByteLength_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TO_HEX / FROM_HEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	[InlineData("TO_HEX(b'abc')", "616263")]
	public async Task ToHex_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SOUNDEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#soundex
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
	//   Performs a normalized, case-insensitive substring search.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONTAINS_SUBSTR('hello world', 'hello')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'HELLO')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'World')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'llo wo')", true)]
	[InlineData("CONTAINS_SUBSTR('hello world', 'xyz')", false)]
	[InlineData("CONTAINS_SUBSTR('hello world', '')", true)]
	[InlineData("CONTAINS_SUBSTR('', '')", true)]
	[InlineData("CONTAINS_SUBSTR('', 'a')", false)]
	[InlineData("CONTAINS_SUBSTR('ABC', 'abc')", true)]
	[InlineData("CONTAINS_SUBSTR('abc', 'ABC')", true)]
	public async Task ContainsSubstr_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task ContainsSubstr_NullInput_ReturnsNull()
		=> (await Eval("CONTAINS_SUBSTR(NULL, 'a')")).Should().BeNull();

	[Fact]
	public async Task ContainsSubstr_NullSearch_ReturnsNull()
		=> (await Eval("CONTAINS_SUBSTR('hello', NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// String concatenation operator ||
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LPAD / RPAD with default padding (space)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LPAD('hi', 5)", "   hi")]
	[InlineData("RPAD('hi', 5)", "hi   ")]
	[InlineData("LPAD('hello', 5)", "hello")]
	[InlineData("RPAD('hello', 5)", "hello")]
	public async Task PadWithDefaultSpace_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Edge cases: very long strings, repeated operations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
