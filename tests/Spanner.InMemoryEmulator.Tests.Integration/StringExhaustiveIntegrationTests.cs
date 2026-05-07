using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive string function tests covering all Spanner string functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringExhaustiveIntegrationTests : IntegrationTestBase
{
	public StringExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── CONCAT ───
	[Theory]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('a', 'b', 'c')", "abc")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('a', '')", "a")]
	[InlineData("CONCAT('', 'b')", "b")]
	[InlineData("CONCAT('abc', 'def', 'ghi')", "abcdefghi")]
	[InlineData("CONCAT('123', '456')", "123456")]
	[InlineData("CONCAT('A', 'B', 'C', 'D')", "ABCD")]
	[InlineData("CONCAT('x')", "x")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Concat(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── LENGTH ───
	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('hello')", 5L)]
	[InlineData("LENGTH('hello world')", 11L)]
	[InlineData("LENGTH('  ')", 2L)]
	[InlineData("LENGTH('abc')", 3L)]
	[InlineData("LENGTH('1234567890')", 10L)]
	[InlineData("CHAR_LENGTH('hello')", 5L)]
	[InlineData("CHARACTER_LENGTH('hello')", 5L)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Length(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── UPPER / LOWER ───
	[Theory]
	[InlineData("UPPER('hello')", "HELLO")]
	[InlineData("UPPER('Hello World')", "HELLO WORLD")]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('ABC')", "ABC")]
	[InlineData("UPPER('123')", "123")]
	[InlineData("UPPER('aBcDeF')", "ABCDEF")]
	[InlineData("LOWER('HELLO')", "hello")]
	[InlineData("LOWER('Hello World')", "hello world")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('abc')", "abc")]
	[InlineData("LOWER('123')", "123")]
	[InlineData("LOWER('AbCdEf')", "abcdef")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task UpperLower(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── TRIM / LTRIM / RTRIM ───
	[Theory]
	[InlineData("TRIM('  hello  ')", "hello")]
	[InlineData("TRIM('hello')", "hello")]
	[InlineData("TRIM('   ')", "")]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM('  a b  ')", "a b")]
	[InlineData("LTRIM('  hello  ')", "hello  ")]
	[InlineData("LTRIM('hello')", "hello")]
	[InlineData("LTRIM('   ')", "")]
	[InlineData("LTRIM('')", "")]
	[InlineData("RTRIM('  hello  ')", "  hello")]
	[InlineData("RTRIM('hello')", "hello")]
	[InlineData("RTRIM('   ')", "")]
	[InlineData("RTRIM('')", "")]
	[InlineData("TRIM('xxxhelloxxx', 'x')", "hello")]
	[InlineData("LTRIM('xxxhello', 'x')", "hello")]
	[InlineData("RTRIM('helloxxx', 'x')", "hello")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Trim(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SUBSTR ───
	[Theory]
	[InlineData("SUBSTR('hello', 1)", "hello")]
	[InlineData("SUBSTR('hello', 2)", "ello")]
	[InlineData("SUBSTR('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello', 2, 3)", "ell")]
	[InlineData("SUBSTR('hello', 5)", "o")]
	[InlineData("SUBSTR('hello', 6)", "")]
	[InlineData("SUBSTR('hello', 1, 0)", "")]
	[InlineData("SUBSTR('hello', 1, 5)", "hello")]
	[InlineData("SUBSTR('hello', 1, 10)", "hello")]
	[InlineData("SUBSTR('hello', -1, 1)", "o")]
	[InlineData("SUBSTR('hello', -2, 2)", "lo")]
	[InlineData("SUBSTR('abcdef', 3, 2)", "cd")]
	[InlineData("SUBSTR('abcdef', 1, 1)", "a")]
	[InlineData("SUBSTR('abcdef', 6, 1)", "f")]
	[InlineData("SUBSTR('hello world', 7)", "world")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Substr(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STARTS_WITH / ENDS_WITH ───
	[Theory]
	[InlineData("STARTS_WITH('hello', 'he')", true)]
	[InlineData("STARTS_WITH('hello', 'Hello')", false)]
	[InlineData("STARTS_WITH('hello', 'hello')", true)]
	[InlineData("STARTS_WITH('hello', '')", true)]
	[InlineData("STARTS_WITH('hello', 'xyz')", false)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('hello', 'lo')", true)]
	[InlineData("ENDS_WITH('hello', 'Hello')", false)]
	[InlineData("ENDS_WITH('hello', 'hello')", true)]
	[InlineData("ENDS_WITH('hello', '')", true)]
	[InlineData("ENDS_WITH('hello', 'xyz')", false)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task StartsEndsWith(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REPLACE ───
	[Theory]
	[InlineData("REPLACE('hello world', 'world', 'there')", "hello there")]
	[InlineData("REPLACE('aaa', 'a', 'b')", "bbb")]
	[InlineData("REPLACE('hello', 'xyz', 'q')", "hello")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('abcabc', 'abc', 'x')", "xx")]
	[InlineData("REPLACE('hello', '', 'x')", "hello")]
	[InlineData("REPLACE('aabbcc', 'bb', '')", "aacc")]
	[InlineData("REPLACE('hello world hello', 'hello', 'hi')", "hi world hi")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Replace(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REVERSE ───
	[Theory]
	[InlineData("REVERSE('hello')", "olleh")]
	[InlineData("REVERSE('abc')", "cba")]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('ab')", "ba")]
	[InlineData("REVERSE('12345')", "54321")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Reverse(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REPEAT ───
	[Theory]
	[InlineData("REPEAT('a', 0)", "")]
	[InlineData("REPEAT('a', 1)", "a")]
	[InlineData("REPEAT('a', 3)", "aaa")]
	[InlineData("REPEAT('ab', 3)", "ababab")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('x', 5)", "xxxxx")]
	[InlineData("REPEAT('hi ', 2)", "hi hi ")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Repeat(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── LPAD / RPAD ───
	[Theory]
	[InlineData("LPAD('hello', 10, '*')", "*****hello")]
	[InlineData("LPAD('hello', 5, '*')", "hello")]
	[InlineData("LPAD('hello', 3, '*')", "hel")]
	[InlineData("LPAD('hi', 5, 'ab')", "abahi")]
	[InlineData("LPAD('', 3, 'x')", "xxx")]
	[InlineData("RPAD('hello', 10, '*')", "hello*****")]
	[InlineData("RPAD('hello', 5, '*')", "hello")]
	[InlineData("RPAD('hello', 3, '*')", "hel")]
	[InlineData("RPAD('hi', 5, 'ab')", "hiaba")]
	[InlineData("RPAD('', 3, 'x')", "xxx")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task LpadRpad(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRPOS / INSTR ───
	[Theory]
	[InlineData("STRPOS('hello world', 'world')", 7L)]
	[InlineData("STRPOS('hello world', 'hello')", 1L)]
	[InlineData("STRPOS('hello world', 'xyz')", 0L)]
	[InlineData("STRPOS('hello world', '')", 1L)]
	[InlineData("STRPOS('abcabc', 'bc')", 2L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[InlineData("STRPOS('aaaa', 'a')", 1L)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task StrposInstr(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REGEXP_CONTAINS ───
	[Theory]
	[InlineData("REGEXP_CONTAINS('hello', 'ell')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^hel')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'lo$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^hello$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'xyz')", false)]
	[InlineData("REGEXP_CONTAINS('hello123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '[0-9]+')", false)]
	[InlineData("REGEXP_CONTAINS('abc', '.')", true)]
	[InlineData("REGEXP_CONTAINS('', '.*')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'a.c')", true)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task RegexpContains(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REGEXP_EXTRACT ───
	[Theory]
	[InlineData("REGEXP_EXTRACT('hello123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('abc def', '[a-z]+')", "abc")]
	[InlineData("REGEXP_EXTRACT('foo bar baz', 'bar')", "bar")]
	[InlineData("REGEXP_EXTRACT('hello', '[0-9]+')", null)]
	[InlineData("REGEXP_EXTRACT('test@example.com', '@(.+)', 1)", "example.com")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RegexpExtract(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	// ─── REGEXP_REPLACE ───
	[Theory]
	[InlineData("REGEXP_REPLACE('hello123', '[0-9]+', 'NUM')", "helloNUM")]
	[InlineData("REGEXP_REPLACE('abc def', ' ', '-')", "abc-def")]
	[InlineData("REGEXP_REPLACE('aaa', 'a', 'b')", "bbb")]
	[InlineData("REGEXP_REPLACE('hello', 'xyz', 'q')", "hello")]
	[InlineData("REGEXP_REPLACE('foo bar foo', 'foo', 'baz')", "baz bar baz")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task RegexpReplace(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SPLIT ───
	[Theory]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('hello', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,,b', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT(',a,b,', ','))", 4L)]
	[InlineData("ARRAY_LENGTH(SPLIT('', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a|b|c', '|'))", 3L)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Split_Length(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── FORMAT (string) ───
	[Theory]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%d + %d = %d', 1, 2, 3)", "1 + 2 = 3")]
	[InlineData("FORMAT('%.2f', 3.14159)", "3.14")]
	[InlineData("FORMAT('%05d', 42)", "00042")]
	[InlineData("FORMAT('%%')", "%")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task Format(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SOUNDEX ───
	[Theory]
	[InlineData("SOUNDEX('Robert')", "R163")]
	[InlineData("SOUNDEX('Rupert')", "R163")]
	[InlineData("SOUNDEX('Ashcraft')", "A261")]
	[InlineData("SOUNDEX('Tymczak')", "T522")]
	[InlineData("SOUNDEX('A')", "A000")]
	[InlineData("SOUNDEX('')", "")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Soundex(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── BYTE_LENGTH ───
	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task ByteLength(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── LIKE operator ───
	[Theory]
	[InlineData("'hello' LIKE 'hello'", true)]
	[InlineData("'hello' LIKE 'hel%'", true)]
	[InlineData("'hello' LIKE '%llo'", true)]
	[InlineData("'hello' LIKE '%ell%'", true)]
	[InlineData("'hello' LIKE 'h_llo'", true)]
	[InlineData("'hello' LIKE 'h%o'", true)]
	[InlineData("'hello' LIKE 'xyz'", false)]
	[InlineData("'hello' LIKE '%'", true)]
	[InlineData("'hello' LIKE '_____'", true)]
	[InlineData("'hello' LIKE '____'", false)]
	[InlineData("'' LIKE ''", true)]
	[InlineData("'' LIKE '%'", true)]
	[InlineData("'abc' NOT LIKE 'abc'", false)]
	[InlineData("'abc' NOT LIKE 'xyz'", true)]
	[InlineData("'abc' NOT LIKE 'a%'", false)]
	[InlineData("'abc' NOT LIKE 'x%'", true)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task LikeOperator(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── String concatenation with || ───
	[Theory]
	[InlineData("'a' || 'b'", "ab")]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("'' || ''", "")]
	[InlineData("'x' || ''", "x")]
	[InlineData("'' || 'y'", "y")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task ConcatOperator(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── TO_HEX / FROM_HEX ───
	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task ToHex(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── COALESCE with strings ───
	[Theory]
	[InlineData("COALESCE(NULL, 'hello')", "hello")]
	[InlineData("COALESCE('hello', 'world')", "hello")]
	[InlineData("COALESCE(NULL, NULL, 'third')", "third")]
	[InlineData("COALESCE('first', NULL, 'third')", "first")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task CoalesceStrings(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── IFNULL with strings ───
	[Theory]
	[InlineData("IFNULL(NULL, 'default')", "default")]
	[InlineData("IFNULL('value', 'default')", "value")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task IfnullStrings(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NULLIF with strings ───
	[Theory]
	[InlineData("NULLIF('a', 'a')", null)]
	[InlineData("NULLIF('a', 'b')", "a")]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task NullifStrings(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	// ─── Nested string functions ───
	[Theory]
	[InlineData("UPPER(CONCAT('hello', ' ', 'world'))", "HELLO WORLD")]
	[InlineData("LOWER(UPPER('Hello'))", "hello")]
	[InlineData("LENGTH(CONCAT('a', 'bc'))", 3L)]
	[InlineData("REVERSE(UPPER('hello'))", "OLLEH")]
	[InlineData("TRIM(CONCAT('  ', 'hello', '  '))", "hello")]
	[InlineData("SUBSTR(REVERSE('abcde'), 1, 3)", "edc")]
	[InlineData("REPLACE(UPPER('hello world'), 'WORLD', 'THERE')", "HELLO THERE")]
	[InlineData("LENGTH(REPEAT('ab', 5))", 10L)]
	[Trait(TestTraits.Category, "StringExhaustive")]
	public async Task NestedFunctions(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}
}
