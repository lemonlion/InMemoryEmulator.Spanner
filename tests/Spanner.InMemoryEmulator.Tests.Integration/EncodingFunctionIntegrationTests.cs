using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for encoding/decoding functions: TO_HEX, FROM_HEX,
/// CODE_POINTS_TO_STRING, TO_CODE_POINTS, CODE_POINTS_TO_BYTES, SOUNDEX, ASCII,
/// UNICODE, OCTET_LENGTH, BYTE_LENGTH, and SAFE_CONVERT_BYTES_TO_STRING edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class EncodingFunctionIntegrationTests : IntegrationTestBase
{
	public EncodingFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// TO_HEX — converts BYTES to hexadecimal string
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_hex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0a')", "0a")]
	[InlineData("TO_HEX(b'\\xab\\xcd')", "abcd")]
	[InlineData("TO_HEX(b'')", "")]
	[InlineData("TO_HEX(b'\\x01\\x02\\x03')", "010203")]
	public async Task ToHex_Values(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task ToHex_Null_IsNull()
	{
		var result = await Eval("TO_HEX(CAST(NULL AS BYTES))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// FROM_HEX — converts hexadecimal string to BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_hex
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task FromHex_RoundTrip()
	{
		// TO_HEX(FROM_HEX('abcd')) should be 'abcd'
		var result = (string)(await Eval("TO_HEX(FROM_HEX('abcd'))"))!;
		result.Should().Be("abcd");
	}

	[Fact]
	public async Task FromHex_Empty()
	{
		var result = (string)(await Eval("TO_HEX(FROM_HEX(''))"))!;
		result.Should().Be("");
	}

	[Fact]
	public async Task FromHex_Null_IsNull()
	{
		var result = await Eval("FROM_HEX(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("TO_HEX(FROM_HEX('00'))", "00")]
	[InlineData("TO_HEX(FROM_HEX('ff'))", "ff")]
	[InlineData("TO_HEX(FROM_HEX('FF'))", "ff")]
	[InlineData("TO_HEX(FROM_HEX('0123456789abcdef'))", "0123456789abcdef")]
	public async Task FromHex_ToHex_RoundTrips(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CODE_POINTS_TO_STRING / TO_CODE_POINTS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_string
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_code_points
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CODE_POINTS_TO_STRING([65])", "A")]
	[InlineData("CODE_POINTS_TO_STRING([72, 101, 108, 108, 111])", "Hello")]
	[InlineData("CODE_POINTS_TO_STRING([97, 98, 99])", "abc")]
	[InlineData("CODE_POINTS_TO_STRING([])", "")]
	[InlineData("CODE_POINTS_TO_STRING([48])", "0")]
	[InlineData("CODE_POINTS_TO_STRING([32])", " ")]
	public async Task CodePointsToString_Values(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task CodePointsToString_Null_IsNull()
	{
		var result = await Eval("CODE_POINTS_TO_STRING(CAST(NULL AS ARRAY<INT64>))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task ToCodePoints_BasicString()
	{
		var rows = await QueryAsync("SELECT cp FROM UNNEST(TO_CODE_POINTS('ABC')) AS cp ORDER BY cp");
		rows.Select(r => r["cp"]).Should().BeEquivalentTo(new object[] { 65L, 66L, 67L });
	}

	[Fact]
	public async Task ToCodePoints_EmptyString()
	{
		var rows = await QueryAsync("SELECT cp FROM UNNEST(TO_CODE_POINTS('')) AS cp");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task ToCodePoints_Null_IsNull()
	{
		var result = await Eval("TO_CODE_POINTS(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task CodePoints_RoundTrip()
	{
		var result = (string)(await Eval("CODE_POINTS_TO_STRING(TO_CODE_POINTS('Hello World'))"))!;
		result.Should().Be("Hello World");
	}

	// ═══════════════════════════════════════════════════════════════
	// CODE_POINTS_TO_BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_bytes
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TO_HEX(CODE_POINTS_TO_BYTES([0]))", "00")]
	[InlineData("TO_HEX(CODE_POINTS_TO_BYTES([255]))", "ff")]
	[InlineData("TO_HEX(CODE_POINTS_TO_BYTES([1, 2, 3]))", "010203")]
	[InlineData("TO_HEX(CODE_POINTS_TO_BYTES([]))", "")]
	public async Task CodePointsToBytes_Values(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SOUNDEX — phonetic algorithm for English names
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#soundex
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SOUNDEX('Robert')", "R163")]
	[InlineData("SOUNDEX('Rupert')", "R163")]
	[InlineData("SOUNDEX('smith')", "s530")]
	[InlineData("SOUNDEX('smythe')", "s530")]
	[InlineData("SOUNDEX('')", "")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Soundex_Values(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Soundex_SimilarNames_Match()
	{
		var s1 = (string)(await Eval("SOUNDEX('Robert')"))!;
		var s2 = (string)(await Eval("SOUNDEX('Rupert')"))!;
		s1.Should().Be(s2);
	}

	[Fact]
	public async Task Soundex_DifferentNames_Differ()
	{
		var s1 = (string)(await Eval("SOUNDEX('Robert')"))!;
		var s2 = (string)(await Eval("SOUNDEX('John')"))!;
		s1.Should().NotBe(s2);
	}

	[Fact]
	public async Task Soundex_Null_IsNull()
	{
		var result = await Eval("SOUNDEX(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// OCTET_LENGTH — byte-level length
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#octet_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("OCTET_LENGTH('')", 0L)]
	[InlineData("OCTET_LENGTH('a')", 1L)]
	[InlineData("OCTET_LENGTH('abc')", 3L)]
	[InlineData("OCTET_LENGTH('hello')", 5L)]
	public async Task OctetLength_AsciiStrings(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task OctetLength_Null_IsNull()
	{
		var result = await Eval("OCTET_LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// BYTE_LENGTH — length of BYTES or STRING in bytes
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BYTE_LENGTH(b'')", 0L)]
	[InlineData("BYTE_LENGTH(b'\\x00')", 1L)]
	[InlineData("BYTE_LENGTH(b'\\x00\\x01\\x02')", 3L)]
	[InlineData("BYTE_LENGTH('hello')", 5L)]
	[InlineData("BYTE_LENGTH('')", 0L)]
	public async Task ByteLength_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task ByteLength_Null_IsNull()
	{
		var result = await Eval("BYTE_LENGTH(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_CONVERT_BYTES_TO_STRING edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#safe_convert_bytes_to_string
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SafeConvertBytesToString_ValidUtf8()
	{
		var result = (string)(await Eval("SAFE_CONVERT_BYTES_TO_STRING(b'hello')"))!;
		result.Should().Be("hello");
	}

	[Fact]
	public async Task SafeConvertBytesToString_EmptyBytes()
	{
		var result = (string)(await Eval("SAFE_CONVERT_BYTES_TO_STRING(b'')"))!;
		result.Should().Be("");
	}

	[Fact]
	public async Task SafeConvertBytesToString_Null_IsNull()
	{
		var result = await Eval("SAFE_CONVERT_BYTES_TO_STRING(CAST(NULL AS BYTES))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NORMALIZE / NORMALIZE_AND_CASEFOLD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#normalize
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NORMALIZE('hello', NFC)", "hello")]
	[InlineData("NORMALIZE('hello', NFD)", "hello")]
	[InlineData("NORMALIZE('hello', NFKC)", "hello")]
	[InlineData("NORMALIZE('hello', NFKD)", "hello")]
	[InlineData("NORMALIZE('', NFC)", "")]
	public async Task Normalize_AsciiStrings(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Normalize_Null_IsNull()
	{
		var result = await Eval("NORMALIZE(CAST(NULL AS STRING), NFC)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task NormalizeAndCasefold_Lowercases()
	{
		var result = (string)(await Eval("NORMALIZE_AND_CASEFOLD('HELLO', NFC)"))!;
		result.Should().Be("hello");
	}

	[Fact]
	public async Task NormalizeAndCasefold_AlreadyLower()
	{
		var result = (string)(await Eval("NORMALIZE_AND_CASEFOLD('hello', NFC)"))!;
		result.Should().Be("hello");
	}

	[Fact]
	public async Task NormalizeAndCasefold_Null_IsNull()
	{
		var result = await Eval("NORMALIZE_AND_CASEFOLD(CAST(NULL AS STRING), NFC)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SPLIT_SUBSTR
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_Basic()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split_substr
		//   start_split is 1-based.
		var result = (string)(await Eval("SPLIT_SUBSTR('hello-world-foo', '-', 1, 1)"))!;
		result.Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_Second()
	{
		var result = (string)(await Eval("SPLIT_SUBSTR('hello-world-foo', '-', 2, 1)"))!;
		result.Should().Be("world");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_Last()
	{
		var result = (string)(await Eval("SPLIT_SUBSTR('hello-world-foo', '-', 3, 1)"))!;
		result.Should().Be("foo");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_Null_IsNull()
	{
		var result = await Eval("SPLIT_SUBSTR(CAST(NULL AS STRING), '-', 0)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_INSTR — position of first regex match
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_instr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("REGEXP_INSTR('hello world', 'world')", 7L)]
	[InlineData("REGEXP_INSTR('hello world', 'o')", 5L)]
	[InlineData("REGEXP_INSTR('hello world', 'xyz')", 0L)]
	[InlineData("REGEXP_INSTR('abc123', '[0-9]+')", 4L)]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task RegexpInstr_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task RegexpInstr_Null_IsNull()
	{
		var result = await Eval("REGEXP_INSTR(CAST(NULL AS STRING), 'test')");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_EXTRACT_ALL edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task RegexpExtractAll_MultipleMatches()
	{
		var rows = await QueryAsync("SELECT m FROM UNNEST(REGEXP_EXTRACT_ALL('abc 123 def 456', '[0-9]+')) AS m");
		rows.Select(r => r["m"]).Should().BeEquivalentTo(new object[] { "123", "456" });
	}

	[Fact]
	public async Task RegexpExtractAll_NoMatches()
	{
		var rows = await QueryAsync("SELECT m FROM UNNEST(REGEXP_EXTRACT_ALL('abc def', '[0-9]+')) AS m");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task RegexpExtractAll_EmptyString()
	{
		var rows = await QueryAsync("SELECT m FROM UNNEST(REGEXP_EXTRACT_ALL('', '[0-9]+')) AS m");
		rows.Should().BeEmpty();
	}
}
