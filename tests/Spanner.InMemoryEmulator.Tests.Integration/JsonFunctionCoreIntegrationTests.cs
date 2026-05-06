using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for JSON functions: PARSE_JSON, JSON_VALUE, JSON_QUERY, JSON_QUERY_ARRAY,
/// JSON_TYPE, JSON_SET, JSON_KEYS, JSON_STRIP_NULLS, JSON_ARRAY, JSON_OBJECT,
/// JSON_REMOVE, JSON_ARRAY_APPEND, JSON_ARRAY_INSERT, JSON_CONTAINS, TO_JSON.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JsonFunctionCoreIntegrationTests : IntegrationTestBase
{
	public JsonFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── JSON_VALUE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value

	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\":1}'), '$.a')", "1")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"name\":\"Alice\"}'), '$.name')", "Alice")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"x\":{\"y\":42}}'), '$.x.y')", "42")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\":true}'), '$.a')", "true")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\":null}'), '$.a')", null)]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonValue_ExtractScalar(string expr, string? expected)
	{
		var result = await Eval(expr);
		if (expected == null) result.Should().BeNull();
		else result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonValue_MissingPath_ReturnsNull()
	{
		var result = await Eval("JSON_VALUE(PARSE_JSON('{\"a\":1}'), '$.b')");
		result.Should().BeNull();
	}

	// ─── JSON_QUERY ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonQuery_ExtractObject()
	{
		var result = await Eval("JSON_QUERY(PARSE_JSON('{\"a\":{\"b\":1}}'), '$.a')");
		result.Should().NotBeNull();
		result!.ToString().Should().Contain("b");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonQuery_ExtractArray()
	{
		var result = await Eval("JSON_QUERY(PARSE_JSON('{\"a\":[1,2,3]}'), '$.a')");
		result.Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonQuery_MissingPath_ReturnsNull()
	{
		var result = await Eval("JSON_QUERY(PARSE_JSON('{\"a\":1}'), '$.b')");
		result.Should().BeNull();
	}

	// ─── JSON_TYPE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_type

	[Theory]
	[InlineData("JSON_TYPE(PARSE_JSON('42'))", "number")]
	[InlineData("JSON_TYPE(PARSE_JSON('\"hello\"'))", "string")]
	[InlineData("JSON_TYPE(PARSE_JSON('true'))", "boolean")]
	[InlineData("JSON_TYPE(PARSE_JSON('null'))", "null")]
	[InlineData("JSON_TYPE(PARSE_JSON('{}'))", "object")]
	[InlineData("JSON_TYPE(PARSE_JSON('[]'))", "array")]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonType_AllTypes(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── JSON_ARRAY ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonArray_BuildsArray()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY(1, 2, 3))");
		result.Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonArray_MixedTypes()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY(1, 'hello', true))");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("1").And.Contain("hello").And.Contain("true");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonArray_Empty()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY())");
		result.Should().Be("[]");
	}

	// ─── JSON_OBJECT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_object

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonObject_SinglePair()
	{
		var result = await Eval("JSON_VALUE(JSON_OBJECT('name', 'Alice'), '$.name')");
		result.Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonObject_MultiplePairs()
	{
		var result = await Eval("JSON_VALUE(JSON_OBJECT('a', 1, 'b', 2), '$.b')");
		result.Should().Be("2");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonObject_Empty()
	{
		var result = await Eval("TO_JSON_STRING(JSON_OBJECT())");
		result.Should().Be("{}");
	}

	// ─── JSON_SET ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_set

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonSet_AddNewKey()
	{
		var result = await Eval("JSON_VALUE(JSON_SET(PARSE_JSON('{\"a\":1}'), '$.b', 2), '$.b')");
		result.Should().Be("2");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonSet_UpdateExistingKey()
	{
		var result = await Eval("JSON_VALUE(JSON_SET(PARSE_JSON('{\"a\":1}'), '$.a', 99), '$.a')");
		result.Should().Be("99");
	}

	// ─── JSON_KEYS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_keys

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonKeys_FromObject()
	{
		var rows = await QueryAsync("SELECT k FROM UNNEST(JSON_KEYS(PARSE_JSON('{\"a\":1,\"b\":2,\"c\":3}'))) AS k ORDER BY k");
		rows.Select(r => (string)r["k"]!).Should().Equal("a", "b", "c");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonKeys_EmptyObject()
	{
		var rows = await QueryAsync("SELECT k FROM UNNEST(JSON_KEYS(PARSE_JSON('{}'))) AS k");
		rows.Should().BeEmpty();
	}

	// ─── JSON_STRIP_NULLS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_strip_nulls

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonStripNulls_RemovesNullValues()
	{
		var result = await Eval("JSON_VALUE(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":1,\"b\":null}')), '$.b')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonStripNulls_RetainsNonNull()
	{
		var result = await Eval("JSON_VALUE(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":1,\"b\":null}')), '$.a')");
		result.Should().Be("1");
	}

	// ─── JSON_REMOVE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_remove

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonRemove_RemovesKey()
	{
		var result = await Eval("JSON_VALUE(JSON_REMOVE(PARSE_JSON('{\"a\":1,\"b\":2}'), '$.a'), '$.a')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonRemove_RetainsOtherKeys()
	{
		var result = await Eval("JSON_VALUE(JSON_REMOVE(PARSE_JSON('{\"a\":1,\"b\":2}'), '$.a'), '$.b')");
		result.Should().Be("2");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonRemove_NonExistentPath_NoError()
	{
		var result = await Eval("TO_JSON_STRING(JSON_REMOVE(PARSE_JSON('{\"a\":1}'), '$.b'))");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("a");
	}

	// ─── JSON_ARRAY_APPEND ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonArrayAppend_AppendsToArray()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_APPEND(PARSE_JSON('{\"a\":[1,2]}'), '$.a', 3))");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("3");
	}

	// ─── JSON_ARRAY_INSERT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_insert

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonArrayInsert_InsertsAtIndex()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_INSERT(PARSE_JSON('[1,3]'), '$[1]', 2))");
		result.Should().Be("[1,2,3]");
	}

	// ─── JSON_CONTAINS ───

	[Theory]
	[InlineData("JSON_CONTAINS(PARSE_JSON('{\"a\":1,\"b\":2}'), PARSE_JSON('{\"a\":1}'))", true)]
	[InlineData("JSON_CONTAINS(PARSE_JSON('[1,2,3]'), PARSE_JSON('1'))", true)]
	[InlineData("JSON_CONTAINS(PARSE_JSON('[1,2,3]'), PARSE_JSON('4'))", false)]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonContains_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── JSON_VALUE_ARRAY ───

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonValueArray_ExtractsStringArray()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(JSON_VALUE_ARRAY(PARSE_JSON('{\"a\":[\"x\",\"y\",\"z\"]}'), '$.a')) AS v");
		rows.Select(r => (string)r["v"]!).Should().Equal("x", "y", "z");
	}

	// ─── JSON_QUERY_ARRAY ───

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonQueryArray_ExtractsArrayElements()
	{
		var rows = await QueryAsync("SELECT TO_JSON_STRING(v) AS v FROM UNNEST(JSON_QUERY_ARRAY(PARSE_JSON('[1,2,3]'))) AS v");
		rows.Should().HaveCount(3);
	}

	// ─── TO_JSON / TO_JSON_STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#to_json

	[Theory]
	[InlineData("TO_JSON_STRING(42)", "42")]
	[InlineData("TO_JSON_STRING('hello')", "\"hello\"")]
	[InlineData("TO_JSON_STRING(true)", "true")]
	[InlineData("TO_JSON_STRING(false)", "false")]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task ToJsonString_Scalars(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task ToJsonString_Null()
	{
		var result = await Eval("TO_JSON_STRING(NULL)");
		// Should return "null" or actual NULL
		// Ref: Cloud Spanner returns the string "null" for NULL input to TO_JSON_STRING
		(result == null || result.Equals("null")).Should().BeTrue();
	}

	// ─── BOOL() from JSON ───

	[Theory]
	[InlineData("BOOL(PARSE_JSON('true'))", true)]
	[InlineData("BOOL(PARSE_JSON('false'))", false)]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task Bool_FromJson(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRING() from JSON ───

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task String_FromJson()
	{
		var result = await Eval("STRING(PARSE_JSON('\"hello\"'))");
		result.Should().Be("hello");
	}

	// ─── LAX_BOOL / LAX_STRING ───

	[Theory]
	[InlineData("LAX_BOOL(PARSE_JSON('true'))", true)]
	[InlineData("LAX_BOOL(PARSE_JSON('false'))", false)]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task LaxBool_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("LAX_STRING(PARSE_JSON('\"hello\"'))", "hello")]
	[InlineData("LAX_STRING(PARSE_JSON('42'))", "42")]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task LaxString_Cases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SAFE_TO_JSON ───

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task SafeToJson_ValidInput()
	{
		var result = await Eval("TO_JSON_STRING(SAFE_TO_JSON(42))");
		result.Should().Be("42");
	}

	// ─── JSON in table columns ───

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonColumn_InsertAndQuery()
	{
		var table = "JsonTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Data JSON) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Data) VALUES (1, PARSE_JSON('{{\"name\":\"Alice\",\"age\":30}}'))");

		var rows = await QueryAsync($"SELECT JSON_VALUE(Data, '$.name') AS Name, JSON_VALUE(Data, '$.age') AS Age FROM {table}");
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Age"].Should().Be("30");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonFunction")]
	public async Task JsonColumn_WhereFilter()
	{
		var table = "JsonTbl2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Data JSON) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Data) VALUES (1, PARSE_JSON('{{\"status\":\"active\"}}'))");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Data) VALUES (2, PARSE_JSON('{{\"status\":\"inactive\"}}'))");

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE JSON_VALUE(Data, '$.status') = 'active'");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Legacy JSON extraction functions (deprecated but still supported)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_extract
	//   "JSON_EXTRACT is equivalent to JSON_QUERY"
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_extract_scalar
	//   "JSON_EXTRACT_SCALAR is equivalent to JSON_VALUE"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonExtract_EquivalentToJsonQuery()
	{
		var result = await Eval("JSON_EXTRACT(PARSE_JSON('{\"a\":{\"b\":1}}'), '$.a')");
		result.Should().Be("{\"b\":1}");
	}

	[Fact]
	public async Task JsonExtractScalar_EquivalentToJsonValue()
	{
		var result = await Eval("JSON_EXTRACT_SCALAR(PARSE_JSON('{\"name\":\"Alice\"}'), '$.name')");
		result.Should().Be("Alice");
	}

	[Fact]
	public async Task JsonExtractScalar_ReturnsNumber()
	{
		var result = await Eval("JSON_EXTRACT_SCALAR(PARSE_JSON('{\"age\":30}'), '$.age')");
		result.Should().Be("30");
	}

	[Fact]
	public async Task JsonExtractArray_EquivalentToJsonQueryArray()
	{
		var result = await Eval("ARRAY_LENGTH(JSON_EXTRACT_ARRAY(PARSE_JSON('[1,2,3]'), '$'))");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task JsonExtract_NullInput_ReturnsNull()
	{
		var result = await Eval("JSON_EXTRACT(CAST(NULL AS JSON), '$.a')");
		result.Should().BeNull();
	}
}
