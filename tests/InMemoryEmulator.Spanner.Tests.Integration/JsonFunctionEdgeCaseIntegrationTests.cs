using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Additional JSON function edge cases: JSON_VALUE, JSON_QUERY, JSON_QUERY_ARRAY with NULLs,
/// nested paths, missing keys, array indexing.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JsonFunctionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public JsonFunctionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE('{\"a\": 1}', '$.a')", "1")]
	[InlineData("JSON_VALUE('{\"a\": \"hello\"}', '$.a')", "hello")]
	[InlineData("JSON_VALUE('{\"a\": true}', '$.a')", "true")]
	[InlineData("JSON_VALUE('{\"a\": null}', '$.a')", null)]
	[InlineData("JSON_VALUE('{\"a\": {\"b\": 1}}', '$.a.b')", "1")]
	public async Task JsonValue_ValidPaths(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null)
			result.Should().BeNull();
		else
			result.Should().Be(expected);
	}

	[Theory]
	[InlineData("JSON_VALUE('{\"a\": 1}', '$.b')")]
	[InlineData("JSON_VALUE('{\"a\": 1}', '$.a.b')")]
	public async Task JsonValue_MissingPath_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("JSON_VALUE(NULL, '$.a')")]
	[InlineData("JSON_VALUE('{\"a\": 1}', NULL)")]
	public async Task JsonValue_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_QUERY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonQuery_ReturnsObject()
	{
		var result = await Eval("JSON_QUERY('{\"a\": {\"b\": 1}}', '$.a')");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("b");
	}

	[Fact]
	public async Task JsonQuery_ReturnsArray()
	{
		var result = await Eval("JSON_QUERY('{\"a\": [1, 2, 3]}', '$.a')");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("[");
	}

	[Theory]
	[InlineData("JSON_QUERY(NULL, '$.a')")]
	[InlineData("JSON_QUERY('{\"a\": 1}', NULL)")]
	public async Task JsonQuery_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Fact]
	public async Task JsonQuery_MissingPath_ReturnsNull()
	{
		(await Eval("JSON_QUERY('{\"a\": 1}', '$.b')")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE with array indexing
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE('{\"a\": [10, 20, 30]}', '$.a[0]')", "10")]
	[InlineData("JSON_VALUE('{\"a\": [10, 20, 30]}', '$.a[1]')", "20")]
	[InlineData("JSON_VALUE('{\"a\": [10, 20, 30]}', '$.a[2]')", "30")]
	public async Task JsonValue_ArrayIndexing(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task JsonValue_ArrayIndexOutOfBounds_ReturnsNull()
	{
		(await Eval("JSON_VALUE('{\"a\": [10, 20]}', '$.a[5]')")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested JSON
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonValue_DeeplyNested()
	{
		(await Eval("JSON_VALUE('{\"a\": {\"b\": {\"c\": 42}}}', '$.a.b.c')")).Should().Be("42");
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE with special values
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE('{\"a\": \"\"}', '$.a')", "")]
	[InlineData("JSON_VALUE('{\"a\": 0}', '$.a')", "0")]
	[InlineData("JSON_VALUE('{\"a\": false}', '$.a')", "false")]
	public async Task JsonValue_SpecialValues(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Full-path JSON_VALUE (no path = root scalar)
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE('42')", "42")]
	[InlineData("JSON_VALUE('\"hello\"')", "hello")]
	[InlineData("JSON_VALUE('true')", "true")]
	public async Task JsonValue_RootScalar(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}
}
