using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive tests for core string functions with broad coverage of edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionCoreIntegrationTests : IntegrationTestBase
{
	public StringFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task<List<Dictionary<string, object?>>> Q(string sql)
		=> await QueryAsync(sql);

	// ─── LENGTH / CHAR_LENGTH / CHARACTER_LENGTH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length

	[Theory]
	[InlineData("LENGTH('')", 0L)]
	[InlineData("LENGTH('a')", 1L)]
	[InlineData("LENGTH('hello')", 5L)]
	[InlineData("LENGTH('  ')", 2L)]
	[InlineData("CHAR_LENGTH('abc')", 3L)]
	[InlineData("CHARACTER_LENGTH('test')", 4L)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Length_ReturnsCorrectValues(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Length_Null_ReturnsNull()
	{
		var result = await Eval("LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("LENGTH('日本語')", 3L)]
	[InlineData("LENGTH('🎉')", 2L)]  // Surrogate pair counts as 2 in UTF-16
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Length_Unicode_ReturnsCharCount(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── BYTE_LENGTH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length

	[Theory]
	[InlineData("BYTE_LENGTH('')", 0L)]
	[InlineData("BYTE_LENGTH('a')", 1L)]
	[InlineData("BYTE_LENGTH('abc')", 3L)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task ByteLength_AsciiStrings(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task ByteLength_Null_ReturnsNull()
	{
		var result = await Eval("BYTE_LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── LOWER / UPPER ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lower

	[Theory]
	[InlineData("LOWER('HELLO')", "hello")]
	[InlineData("LOWER('hello')", "hello")]
	[InlineData("LOWER('')", "")]
	[InlineData("LOWER('ABC123')", "abc123")]
	[InlineData("LOWER('MiXeD')", "mixed")]
	[InlineData("UPPER('hello')", "HELLO")]
	[InlineData("UPPER('HELLO')", "HELLO")]
	[InlineData("UPPER('')", "")]
	[InlineData("UPPER('abc123')", "ABC123")]
	[InlineData("UPPER('MiXeD')", "MIXED")]
	[InlineData("LCASE('HELLO')", "hello")]
	[InlineData("UCASE('hello')", "HELLO")]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task LowerUpper_ReturnsCorrectValues(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Lower_Null_ReturnsNull()
	{
		var result = await Eval("LOWER(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Upper_Null_ReturnsNull()
	{
		var result = await Eval("UPPER(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── TRIM / LTRIM / RTRIM ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim

	[Theory]
	[InlineData("TRIM('  hello  ')", "hello")]
	[InlineData("TRIM('hello')", "hello")]
	[InlineData("TRIM('')", "")]
	[InlineData("TRIM('   ')", "")]
	[InlineData("LTRIM('  hello  ')", "hello  ")]
	[InlineData("LTRIM('hello')", "hello")]
	[InlineData("LTRIM('')", "")]
	[InlineData("RTRIM('  hello  ')", "  hello")]
	[InlineData("RTRIM('hello')", "hello")]
	[InlineData("RTRIM('')", "")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Trim_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("TRIM('xxxhelloxxx', 'x')", "hello")]
	[InlineData("TRIM('abcba', 'ab')", "c")]
	[InlineData("LTRIM('xxxhello', 'x')", "hello")]
	[InlineData("RTRIM('helloyyy', 'y')", "hello")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Trim_WithCharacters(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Trim_Null_ReturnsNull()
	{
		var result = await Eval("TRIM(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── CONCAT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat

	[Theory]
	[InlineData("CONCAT('a', 'b')", "ab")]
	[InlineData("CONCAT('', 'b')", "b")]
	[InlineData("CONCAT('a', '')", "a")]
	[InlineData("CONCAT('', '')", "")]
	[InlineData("CONCAT('hello', ' ', 'world')", "hello world")]
	[InlineData("CONCAT('a', 'b', 'c', 'd', 'e')", "abcde")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Concat_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Concat_WithNull_ReturnsNull()
	{
		// Ref: Cloud Spanner docs: CONCAT returns NULL if any argument is NULL
		var result = await Eval("CONCAT('hello', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── STARTS_WITH / ENDS_WITH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with

	[Theory]
	[InlineData("STARTS_WITH('hello', 'he')", true)]
	[InlineData("STARTS_WITH('hello', 'lo')", false)]
	[InlineData("STARTS_WITH('hello', '')", true)]
	[InlineData("STARTS_WITH('hello', 'hello')", true)]
	[InlineData("STARTS_WITH('hello', 'Hello')", false)]
	[InlineData("STARTS_WITH('', '')", true)]
	[InlineData("STARTS_WITH('', 'a')", false)]
	[InlineData("ENDS_WITH('hello', 'lo')", true)]
	[InlineData("ENDS_WITH('hello', 'he')", false)]
	[InlineData("ENDS_WITH('hello', '')", true)]
	[InlineData("ENDS_WITH('hello', 'hello')", true)]
	[InlineData("ENDS_WITH('hello', 'Hello')", false)]
	[InlineData("ENDS_WITH('', '')", true)]
	[InlineData("ENDS_WITH('', 'a')", false)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StartsWith_EndsWith(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SUBSTR / SUBSTRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr

	[Theory]
	[InlineData("SUBSTR('hello', 1)", "hello")]
	[InlineData("SUBSTR('hello', 2)", "ello")]
	[InlineData("SUBSTR('hello', 1, 3)", "hel")]
	[InlineData("SUBSTR('hello', 2, 3)", "ell")]
	[InlineData("SUBSTR('hello', 0, 3)", "hel")]
	[InlineData("SUBSTR('hello', -1, 3)", "o")]  // -1 = last char, length 3 but only 1 char left
	[InlineData("SUBSTR('hello', 6)", "")]
	[InlineData("SUBSTR('', 1)", "")]
	[InlineData("SUBSTRING('hello', 1, 3)", "hel")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_Null_ReturnsNull()
	{
		var result = await Eval("SUBSTR(CAST(NULL AS STRING), 1)");
		result.Should().BeNull();
	}

	// ─── REPLACE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace

	[Theory]
	[InlineData("REPLACE('hello world', 'world', 'there')", "hello there")]
	[InlineData("REPLACE('aaa', 'a', 'bb')", "bbbbbb")]
	[InlineData("REPLACE('hello', 'xyz', 'abc')", "hello")]
	[InlineData("REPLACE('', 'a', 'b')", "")]
	[InlineData("REPLACE('hello', '', 'x')", "hello")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Replace_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Replace_Null_ReturnsNull()
	{
		var result = await Eval("REPLACE(CAST(NULL AS STRING), 'a', 'b')");
		result.Should().BeNull();
	}

	// ─── REVERSE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse

	[Theory]
	[InlineData("REVERSE('hello')", "olleh")]
	[InlineData("REVERSE('a')", "a")]
	[InlineData("REVERSE('')", "")]
	[InlineData("REVERSE('abcde')", "edcba")]
	[InlineData("REVERSE('racecar')", "racecar")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Reverse_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRPOS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos

	[Theory]
	[InlineData("STRPOS('hello', 'l')", 3L)]
	[InlineData("STRPOS('hello', 'll')", 3L)]
	[InlineData("STRPOS('hello', 'x')", 0L)]
	[InlineData("STRPOS('hello', '')", 1L)]
	[InlineData("STRPOS('hello', 'hello')", 1L)]
	[InlineData("STRPOS('hello', 'o')", 5L)]
	[InlineData("STRPOS('', '')", 1L)]
	[InlineData("STRPOS('', 'a')", 0L)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StrPos_BasicCases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SPLIT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Split_CommaDelimiter()
	{
		var rows = await Q("SELECT val FROM UNNEST(SPLIT('a,b,c', ',')) AS val");
		rows.Select(r => (string)r["val"]!).Should().Equal("a", "b", "c");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Split_EmptyString()
	{
		var rows = await Q("SELECT val FROM UNNEST(SPLIT('', ',')) AS val");
		rows.Select(r => (string)r["val"]!).Should().Equal("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Split_NoDelimiterFound()
	{
		var rows = await Q("SELECT val FROM UNNEST(SPLIT('hello', ',')) AS val");
		rows.Select(r => (string)r["val"]!).Should().Equal("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Split_DefaultDelimiter()
	{
		var rows = await Q("SELECT val FROM UNNEST(SPLIT('a,b,c')) AS val");
		rows.Select(r => (string)r["val"]!).Should().Equal("a", "b", "c");
	}

	// ─── LPAD / RPAD ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad

	[Theory]
	[InlineData("LPAD('hi', 5)", "   hi")]
	[InlineData("LPAD('hi', 5, '-')", "---hi")]
	[InlineData("LPAD('hi', 2)", "hi")]
	[InlineData("LPAD('hi', 1)", "h")]
	[InlineData("LPAD('hi', 0)", "")]
	[InlineData("RPAD('hi', 5)", "hi   ")]
	[InlineData("RPAD('hi', 5, '-')", "hi---")]
	[InlineData("RPAD('hi', 2)", "hi")]
	[InlineData("RPAD('hi', 1)", "h")]
	[InlineData("RPAD('hi', 0)", "")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Pad_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REPEAT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat

	[Theory]
	[InlineData("REPEAT('ha', 3)", "hahaha")]
	[InlineData("REPEAT('x', 0)", "")]
	[InlineData("REPEAT('x', 1)", "x")]
	[InlineData("REPEAT('', 5)", "")]
	[InlineData("REPEAT('ab', 2)", "abab")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Repeat_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Repeat_Null_ReturnsNull()
	{
		var result = await Eval("REPEAT(CAST(NULL AS STRING), 3)");
		result.Should().BeNull();
	}

	// ─── REGEXP_CONTAINS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_contains

	[Theory]
	[InlineData("REGEXP_CONTAINS('hello', 'ell')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^h')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'o$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '^hello$')", true)]
	[InlineData("REGEXP_CONTAINS('hello', 'xyz')", false)]
	[InlineData("REGEXP_CONTAINS('hello123', '[0-9]+')", true)]
	[InlineData("REGEXP_CONTAINS('hello', '[0-9]+')", false)]
	[InlineData("REGEXP_CONTAINS('', '')", true)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task RegexpContains_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REGEXP_EXTRACT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract

	[Theory]
	[InlineData("REGEXP_EXTRACT('hello123world', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('hello', '[0-9]+')", null)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task RegexpExtract_Cases(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null) result.Should().BeNull();
		else result.Should().Be(expected);
	}

	// ─── REGEXP_REPLACE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace

	[Theory]
	[InlineData("REGEXP_REPLACE('hello123world', '[0-9]+', 'X')", "helloXworld")]
	[InlineData("REGEXP_REPLACE('hello', '[0-9]+', 'X')", "hello")]
	[InlineData("REGEXP_REPLACE('aaa', 'a', 'bb')", "bbbbbb")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task RegexpReplace_Cases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── REGEXP_EXTRACT_ALL ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract_all

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RegexpExtractAll_ExtractsAllMatches()
	{
		var rows = await Q("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('ab12cd34ef', '[0-9]+')) AS val");
		rows.Select(r => (string)r["val"]!).Should().Equal("12", "34");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task RegexpExtractAll_NoMatch_ReturnsEmptyArray()
	{
		var rows = await Q("SELECT val FROM UNNEST(REGEXP_EXTRACT_ALL('hello', '[0-9]+')) AS val");
		rows.Should().BeEmpty();
	}

	// ─── SOUNDEX ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#soundex

	[Theory]
	[InlineData("SOUNDEX('Robert')", "R163")]
	[InlineData("SOUNDEX('Rupert')", "R163")]
	[InlineData("SOUNDEX('Ashcraft')", "A261")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Soundex_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NORMALIZE / NORMALIZE_AND_CASEFOLD ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#normalize

	[Theory]
	[InlineData("NORMALIZE('hello')", "hello")]
	[InlineData("NORMALIZE('hello', NFC)", "hello")]
	[InlineData("NORMALIZE('hello', NFD)", "hello")]
	[InlineData("NORMALIZE('hello', NFKC)", "hello")]
	[InlineData("NORMALIZE('hello', NFKD)", "hello")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Normalize_AsciiStrings(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("NORMALIZE_AND_CASEFOLD('Hello')", "hello")]
	[InlineData("NORMALIZE_AND_CASEFOLD('HELLO', NFC)", "hello")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task NormalizeAndCasefold_FoldsCase(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── TO_CODE_POINTS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_code_points

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ToCodePoints_AsciiString()
	{
		var rows = await Q("SELECT cp FROM UNNEST(TO_CODE_POINTS('ABC')) AS cp");
		rows.Select(r => (long)r["cp"]!).Should().Equal(65L, 66L, 67L);
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task ToCodePoints_EmptyString()
	{
		var rows = await Q("SELECT cp FROM UNNEST(TO_CODE_POINTS('')) AS cp");
		rows.Should().BeEmpty();
	}

	// ─── CODE_POINTS_TO_STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_string

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task CodePointsToString_BasicCase()
	{
		var result = await Eval("CODE_POINTS_TO_STRING([72, 101, 108, 108, 111])");
		result.Should().Be("Hello");
	}

	// ─── REGEXP_INSTR ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_instr

	[Theory]
	[InlineData("REGEXP_INSTR('hello123world', '[0-9]+')", 6L)]
	[InlineData("REGEXP_INSTR('hello', '[0-9]+')", 0L)]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RegexpInstr_ReturnsPosition(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── OCTET_LENGTH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#octet_length

	[Theory]
	[InlineData("OCTET_LENGTH('')", 0L)]
	[InlineData("OCTET_LENGTH('a')", 1L)]
	[InlineData("OCTET_LENGTH('abc')", 3L)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task OctetLength_AsciiStrings(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Complex string expressions ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task NestedStringFunctions_Work()
	{
		var result = await Eval("UPPER(REVERSE(LOWER('Hello')))");
		result.Should().Be("OLLEH");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task ConcatWithSubstr_Work()
	{
		var result = await Eval("CONCAT(SUBSTR('Hello', 1, 3), UPPER('world'))");
		result.Should().Be("HelWORLD");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Replace_ChainedOperations()
	{
		var result = await Eval("REPLACE(REPLACE('hello world', 'hello', 'hi'), 'world', 'there')");
		result.Should().Be("hi there");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Trim_Combined_WithLowerUpper()
	{
		var result = await Eval("UPPER(TRIM('  hello  '))");
		result.Should().Be("HELLO");
	}

	// ─── String comparison in WHERE clause ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StringFunction_InWhereClause()
	{
		var table = "StrWhere1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie" });

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE LENGTH(Name) > 3 ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Charlie");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StartsWithInWhere()
	{
		var table = "StrWhere2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Anna" });

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE STARTS_WITH(Name, 'A') ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Anna");
	}

	// ─── FORMAT function ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string

	[Theory]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%d + %d = %d', 1, 2, 3)", "1 + 2 = 3")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Format_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── String concatenation operator ───

	[Theory]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("'' || 'a'", "a")]
	[InlineData("'a' || ''", "a")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task ConcatOperator_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── LIKE operator with strings ───

	[Theory]
	[InlineData("'hello' LIKE 'hello'", true)]
	[InlineData("'hello' LIKE 'hell%'", true)]
	[InlineData("'hello' LIKE '%ello'", true)]
	[InlineData("'hello' LIKE '%ell%'", true)]
	[InlineData("'hello' LIKE 'h_llo'", true)]
	[InlineData("'hello' LIKE 'h___o'", true)]
	[InlineData("'hello' LIKE 'world'", false)]
	[InlineData("'hello' LIKE 'HELLO'", false)]
	[InlineData("'' LIKE ''", true)]
	[InlineData("'' LIKE '%'", true)]
	[InlineData("'hello' NOT LIKE 'world'", true)]
	[InlineData("'hello' NOT LIKE 'hello'", false)]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Like_PatternMatching(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SAFE_CONVERT_BYTES_TO_STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#safe_convert_bytes_to_string

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task SafeConvertBytesToString_ValidUtf8()
	{
		var result = await Eval("SAFE_CONVERT_BYTES_TO_STRING(b'hello')");
		result.Should().Be("hello");
	}

	// ─── SPLIT_SUBSTR ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_BasicCase()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split_substr
		//   start_split=2, count=1 → returns the 2nd split ("b")
		var result = await Eval("SPLIT_SUBSTR('a,b,c,d', ',', 2, 1)");
		result.Should().Be("b");
	}

	// ─── Strings used in GROUP BY ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StringFunction_InGroupBy()
	{
		var table = "StrGroup1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX), Value INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "Fruit", ["Value"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "fruit", ["Value"] = 20L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "FRUIT", ["Value"] = 30L });

		var rows = await QueryAsync($"SELECT LOWER(Category) AS cat, SUM(Value) AS total FROM {table} GROUP BY LOWER(Category)");
		rows.Should().HaveCount(1);
		rows[0]["total"].Should().Be(60L);
	}

	// ─── Strings used in ORDER BY ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task StringFunction_InOrderBy()
	{
		var table = "StrOrder1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Charlie" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "alice" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Bob" });

		var rows = await QueryAsync($"SELECT Name FROM {table} ORDER BY LOWER(Name)");
		rows.Select(r => (string)r["Name"]!).Should().Equal("alice", "Bob", "Charlie");
	}
}
