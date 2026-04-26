using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for CAST, SAFE_CAST, type literals, and type conversion edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastAndTypeIntegrationTests : IntegrationTestBase
{
	public CastAndTypeIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(42 AS STRING)", "42")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(100 AS STRING)", "100")]
	[InlineData("CAST(999999 AS STRING)", "999999")]
	[InlineData("CAST(-999999 AS STRING)", "-999999")]
	public async Task Cast_IntToString(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(true AS STRING)", "true")]
	[InlineData("CAST(false AS STRING)", "false")]
	public async Task Cast_BoolToString(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(3.14 AS STRING)", "3.14")]
	[InlineData("CAST(0.0 AS STRING)", "0")]
	[InlineData("CAST(-1.5 AS STRING)", "-1.5")]
	public async Task Cast_FloatToString(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to INT64
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('100' AS INT64)", 100L)]
	[InlineData("CAST('999999' AS INT64)", 999999L)]
	[InlineData("CAST('-999999' AS INT64)", -999999L)]
	public async Task Cast_StringToInt(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(3.9 AS INT64)", 4L)]
	[InlineData("CAST(3.1 AS INT64)", 3L)]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	[InlineData("CAST(-3.9 AS INT64)", -4L)]
	[InlineData("CAST(-3.1 AS INT64)", -3L)]
	[InlineData("CAST(100.99 AS INT64)", 101L)]
	[InlineData("CAST(1.0 AS INT64)", 1L)]
	[InlineData("CAST(-1.0 AS INT64)", -1L)]
	public async Task Cast_FloatToInt(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(true AS INT64)", 1L)]
	[InlineData("CAST(false AS INT64)", 0L)]
	public async Task Cast_BoolToInt(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to FLOAT64
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST('100' AS FLOAT64)", 100.0)]
	[InlineData("CAST('0.001' AS FLOAT64)", 0.001)]
	public async Task Cast_StringToFloat(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	[InlineData("CAST(100 AS FLOAT64)", 100.0)]
	public async Task Cast_IntToFloat(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to BOOL
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(0 AS BOOL)", false)]
	public async Task Cast_ToBool(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST NULL
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	public async Task Cast_Null_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE_CAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#safe_cast
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('xyz' AS INT64)")]
	[InlineData("SAFE_CAST('' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('not_bool' AS BOOL)")]
	public async Task SafeCast_Invalid_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("SAFE_CAST('42' AS INT64)", 42L)]
	[InlineData("SAFE_CAST('0' AS INT64)", 0L)]
	[InlineData("SAFE_CAST('-1' AS INT64)", -1L)]
	public async Task SafeCast_Valid_ReturnsValue(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SAFE_CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("SAFE_CAST('0' AS FLOAT64)", 0.0)]
	public async Task SafeCast_ValidFloat_ReturnsValue(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[InlineData("SAFE_CAST('false' AS BOOL)", false)]
	public async Task SafeCast_ValidBool_ReturnsValue(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SAFE_CAST(NULL AS STRING)")]
	[InlineData("SAFE_CAST(NULL AS INT64)")]
	[InlineData("SAFE_CAST(NULL AS FLOAT64)")]
	[InlineData("SAFE_CAST(NULL AS BOOL)")]
	public async Task SafeCast_Null_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to DATE
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Cast_StringToDate()
	{
		var result = (DateTime)(await Eval("CAST('2024-06-15' AS DATE)"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	[Fact]
	public async Task Cast_StringToDate_Jan1()
	{
		var result = (DateTime)(await Eval("CAST('2024-01-01' AS DATE)"))!;
		result.Should().Be(new DateTime(2024, 1, 1));
	}

	[Fact]
	public async Task Cast_StringToDate_Dec31()
	{
		var result = (DateTime)(await Eval("CAST('2024-12-31' AS DATE)"))!;
		result.Should().Be(new DateTime(2024, 12, 31));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to TIMESTAMP
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Cast_StringToTimestamp()
	{
		var result = (DateTime)(await Eval("CAST('2024-06-15T10:30:00Z' AS TIMESTAMP)"))!;
		result.Should().Be(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task Cast_StringToTimestamp_Midnight()
	{
		var result = (DateTime)(await Eval("CAST('2024-01-01T00:00:00Z' AS TIMESTAMP)"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Chained CAST operations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(3.14 AS STRING) AS FLOAT64)", 3.14)]
	[InlineData("CAST(CAST(true AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST('100' AS INT64) AS STRING)", "100")]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS INT64)", 42L)]
	[InlineData("CAST(CAST('0' AS INT64) AS BOOL)", false)]
	[InlineData("CAST(CAST('1' AS INT64) AS BOOL)", true)]
	public async Task ChainedCast_RoundTrips(string expr, object expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST in expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(1 + 2 AS STRING)", "3")]
	[InlineData("CAST(10 * 5 AS STRING)", "50")]
	[InlineData("CAST(CAST('5' AS INT64) + 3 AS STRING)", "8")]
	[InlineData("LENGTH(CAST(12345 AS STRING))", 5L)]
	[InlineData("CAST(LENGTH('hello') AS STRING)", "5")]
	public async Task Cast_InExpressions(string expr, object expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// PARSE_JSON
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#parse_json
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\": 1}'), '$.a')", "1")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"name\": \"test\"}'), '$.name')", "test")]
	[InlineData("JSON_TYPE(PARSE_JSON('42'))", "number")]
	[InlineData("JSON_TYPE(PARSE_JSON('\"hello\"'))", "string")]
	[InlineData("JSON_TYPE(PARSE_JSON('true'))", "boolean")]
	[InlineData("JSON_TYPE(PARSE_JSON('null'))", "null")]
	[InlineData("JSON_TYPE(PARSE_JSON('[1,2,3]'))", "array")]
	[InlineData("JSON_TYPE(PARSE_JSON('{\"a\":1}'))", "object")]
	public async Task ParseJson_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// JSON_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_VALUE('{\"a\":1}', '$.a')", "1")]
	[InlineData("JSON_VALUE('{\"a\":\"hello\"}', '$.a')", "hello")]
	[InlineData("JSON_VALUE('{\"a\":{\"b\":2}}', '$.a.b')", "2")]
	[InlineData("JSON_VALUE('{\"x\":true}', '$.x')", "true")]
	[InlineData("JSON_VALUE('{\"x\":false}', '$.x')", "false")]
	[InlineData("JSON_VALUE('{\"x\":null}', '$.x')", null)]
	[InlineData("JSON_VALUE('{\"a\":1}', '$.b')", null)]
	public async Task JsonValue_ReturnsExpected(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// JSON_QUERY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_QUERY('{\"a\":{\"b\":2}}', '$.a')", "{\"b\":2}")]
	[InlineData("JSON_QUERY('{\"a\":[1,2,3]}', '$.a')", "[1,2,3]")]
	[InlineData("JSON_QUERY('{\"a\":1}', '$.a')", "1")]
	public async Task JsonQuery_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// JSON_TYPE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_type
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_TYPE(PARSE_JSON('{\"a\":1}'))", "object")]
	[InlineData("JSON_TYPE(PARSE_JSON('[1,2]'))", "array")]
	[InlineData("JSON_TYPE(PARSE_JSON('42'))", "number")]
	[InlineData("JSON_TYPE(PARSE_JSON('\"str\"'))", "string")]
	[InlineData("JSON_TYPE(PARSE_JSON('true'))", "boolean")]
	[InlineData("JSON_TYPE(PARSE_JSON('false'))", "boolean")]
	[InlineData("JSON_TYPE(PARSE_JSON('null'))", "null")]
	public async Task JsonType_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// JSON_QUERY_ARRAY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query_array
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task JsonQueryArray_ReturnsArrayLength()
	{
		var result = await Eval("ARRAY_LENGTH(JSON_QUERY_ARRAY('{\"a\":[1,2,3]}', '$.a'))");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task JsonQueryArray_EmptyArray()
	{
		var result = await Eval("ARRAY_LENGTH(JSON_QUERY_ARRAY('{\"a\":[]}', '$.a'))");
		result.Should().Be(0L);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Nested JSON operations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\":{\"b\":{\"c\":\"deep\"}}}'), '$.a.b.c')", "deep")]
	[InlineData("CAST(JSON_VALUE('{\"val\":42}', '$.val') AS INT64)", 42L)]
	[InlineData("LENGTH(JSON_VALUE('{\"name\":\"hello\"}', '$.name'))", 5L)]
	public async Task NestedJson_ReturnsExpected(string expr, object expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL propagation for JSON functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("JSON_VALUE(NULL, '$.a')")]
	[InlineData("JSON_QUERY(NULL, '$.a')")]
	[InlineData("JSON_TYPE(NULL)")]
	public async Task JsonFunction_NullPropagation(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT function (string formatting)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("FORMAT('%s', 'hello')", "hello")]
	[InlineData("FORMAT('%d', 42)", "42")]
	[InlineData("FORMAT('%s %s', 'hello', 'world')", "hello world")]
	public async Task Format_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);
}
