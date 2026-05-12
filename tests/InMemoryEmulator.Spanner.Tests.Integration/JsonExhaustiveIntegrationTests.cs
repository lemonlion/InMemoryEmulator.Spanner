using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive JSON function tests: JSON_VALUE, JSON_QUERY, JSON_TYPE, PARSE_JSON,
/// TO_JSON, TO_JSON_STRING, JSON_ARRAY, JSON_OBJECT, JSON_SET, JSON_REMOVE,
/// JSON_STRIP_NULLS, JSON_KEYS, JSON_ARRAY_APPEND, JSON_ARRAY_INSERT, JSON_CONTAINS.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JsonExhaustiveIntegrationTests : IntegrationTestBase
{
	public JsonExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── PARSE_JSON / TO_JSON_STRING round-trip ───
	[Theory]
	[InlineData("'{\"a\":1}'", "{\"a\":1}")]
	[InlineData("'[1,2,3]'", "[1,2,3]")]
	[InlineData("'\"hello\"'", "\"hello\"")]
	[InlineData("'42'", "42")]
	[InlineData("'true'", "true")]
	[InlineData("'null'", "null")]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ParseJson_ToJsonString_Roundtrip(string input, string expected)
	{
		var result = await Eval($"TO_JSON_STRING(PARSE_JSON({input}))");
		result.Should().Be(expected);
	}

	// ─── JSON_VALUE ───
	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\":\"hello\"}'), '$.a')", "hello")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"x\":{\"y\":\"deep\"}}'), '$.x.y')", "deep")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"n\":42}'), '$.n')", "42")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"b\":true}'), '$.b')", "true")]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonValue_Paths(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonValue_MissingPath_ReturnsNull()
	{
		var result = await Eval("JSON_VALUE(PARSE_JSON('{\"a\":1}'), '$.b')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonValue_ArrayElement()
	{
		var result = await Eval("JSON_VALUE(PARSE_JSON('[10,20,30]'), '$[1]')");
		result.Should().Be("20");
	}

	// ─── JSON_QUERY ───
	[Theory]
	[InlineData("JSON_QUERY(PARSE_JSON('{\"a\":{\"b\":1}}'), '$.a')", "{\"b\":1}")]
	[InlineData("JSON_QUERY(PARSE_JSON('[1,2,3]'), '$[0]')", "1")]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonQuery_Paths(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonQuery_MissingPath()
	{
		var result = await Eval("JSON_QUERY(PARSE_JSON('{\"a\":1}'), '$.b')");
		result.Should().BeNull();
	}

	// ─── JSON_TYPE ───
	[Theory]
	[InlineData("JSON_TYPE(PARSE_JSON('42'))", "number")]
	[InlineData("JSON_TYPE(PARSE_JSON('\"hello\"'))", "string")]
	[InlineData("JSON_TYPE(PARSE_JSON('true'))", "boolean")]
	[InlineData("JSON_TYPE(PARSE_JSON('null'))", "null")]
	[InlineData("JSON_TYPE(PARSE_JSON('[1,2]'))", "array")]
	[InlineData("JSON_TYPE(PARSE_JSON('{\"a\":1}'))", "object")]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonType(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── JSON_ARRAY ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_Ints()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY(1, 2, 3))");
		result.Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_Strings()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY('a', 'b'))");
		result.Should().Be("[\"a\",\"b\"]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_Mixed()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY(1, 'two', TRUE))");
		result.Should().Be("[1,\"two\",true]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_Empty()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY())");
		result.Should().Be("[]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_Nested()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY(JSON_ARRAY(1,2), JSON_ARRAY(3,4)))");
		result.Should().Be("[[1,2],[3,4]]");
	}

	// ─── JSON_OBJECT ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonObject_Simple()
	{
		var result = await Eval("TO_JSON_STRING(JSON_OBJECT('a', 1, 'b', 2))");
		result.Should().Be("{\"a\":1,\"b\":2}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonObject_StringValues()
	{
		var result = await Eval("TO_JSON_STRING(JSON_OBJECT('name', 'Alice'))");
		result.Should().Be("{\"name\":\"Alice\"}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonObject_Empty()
	{
		var result = await Eval("TO_JSON_STRING(JSON_OBJECT())");
		result.Should().Be("{}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonObject_NestedJson()
	{
		var result = await Eval("TO_JSON_STRING(JSON_OBJECT('arr', JSON_ARRAY(1,2)))");
		result.Should().Be("{\"arr\":[1,2]}");
	}

	// ─── JSON_SET ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonSet_NewKey()
	{
		var result = await Eval("TO_JSON_STRING(JSON_SET(PARSE_JSON('{\"a\":1}'), '$.b', 2))");
		result.Should().Be("{\"a\":1,\"b\":2}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonSet_ReplaceExisting()
	{
		var result = await Eval("TO_JSON_STRING(JSON_SET(PARSE_JSON('{\"a\":1}'), '$.a', 99))");
		result.Should().Be("{\"a\":99}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonSet_NestedPath()
	{
		var result = await Eval("TO_JSON_STRING(JSON_SET(PARSE_JSON('{\"a\":{\"b\":1}}'), '$.a.b', 2))");
		result.Should().Be("{\"a\":{\"b\":2}}");
	}

	// ─── JSON_REMOVE ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonRemove_Key()
	{
		var result = await Eval("TO_JSON_STRING(JSON_REMOVE(PARSE_JSON('{\"a\":1,\"b\":2}'), '$.b'))");
		result.Should().Be("{\"a\":1}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonRemove_NestedKey()
	{
		var result = await Eval("TO_JSON_STRING(JSON_REMOVE(PARSE_JSON('{\"a\":{\"b\":1,\"c\":2}}'), '$.a.c'))");
		result.Should().Be("{\"a\":{\"b\":1}}");
	}

	// ─── JSON_STRIP_NULLS ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonStripNulls_TopLevel()
	{
		var result = await Eval("TO_JSON_STRING(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":1,\"b\":null}')))");
		result.Should().Be("{\"a\":1}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonStripNulls_Nested()
	{
		var result = await Eval("TO_JSON_STRING(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":{\"x\":null,\"y\":1},\"b\":null}')))");
		result.Should().Be("{\"a\":{\"y\":1}}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonStripNulls_NoNulls()
	{
		var result = await Eval("TO_JSON_STRING(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":1}')))");
		result.Should().Be("{\"a\":1}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonStripNulls_AllNull()
	{
		var result = await Eval("TO_JSON_STRING(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":null,\"b\":null}')))");
		result.Should().Be("{}");
	}

	// ─── JSON_ARRAY_APPEND ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArrayAppend_Root()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_APPEND(PARSE_JSON('[1,2]'), '$', 3))");
		result.Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArrayAppend_NestedPath()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_APPEND(PARSE_JSON('{\"a\":[1,2]}'), '$.a', 3))");
		result.Should().Be("{\"a\":[1,2,3]}");
	}

	// ─── JSON_ARRAY_INSERT ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArrayInsert_AtStart()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_INSERT(PARSE_JSON('[2,3]'), '$[0]', 1))");
		result.Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArrayInsert_AtEnd()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_INSERT(PARSE_JSON('[1,2]'), '$[2]', 3))");
		result.Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArrayInsert_InMiddle()
	{
		var result = await Eval("TO_JSON_STRING(JSON_ARRAY_INSERT(PARSE_JSON('[1,3]'), '$[1]', 2))");
		result.Should().Be("[1,2,3]");
	}

	// ─── JSON_CONTAINS ───
	[Theory]
	[InlineData("JSON_CONTAINS(PARSE_JSON('{\"a\":1,\"b\":2}'), PARSE_JSON('{\"a\":1}'))", true)]
	[InlineData("JSON_CONTAINS(PARSE_JSON('[1,2,3]'), PARSE_JSON('1'))", true)]
	[InlineData("JSON_CONTAINS(PARSE_JSON('[1,2,3]'), PARSE_JSON('4'))", false)]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonContains(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── JSON_KEYS ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonKeys_Object()
	{
		var rows = await QueryAsync(
			"SELECT k FROM UNNEST(JSON_KEYS(PARSE_JSON('{\"a\":1,\"b\":2,\"c\":3}'))) AS k ORDER BY k");
		rows.Select(r => (string)r["k"]!).Should().BeEquivalentTo(new[] { "a", "b", "c" });
	}

	// ─── TO_JSON ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ToJson_Int()
	{
		var result = await Eval("TO_JSON_STRING(TO_JSON(42))");
		result.Should().Be("42");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ToJson_String()
	{
		var result = await Eval("TO_JSON_STRING(TO_JSON('hello'))");
		result.Should().Be("\"hello\"");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ToJson_Bool()
	{
		var result = await Eval("TO_JSON_STRING(TO_JSON(TRUE))");
		result.Should().Be("true");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ToJson_Float()
	{
		var result = await Eval("TO_JSON_STRING(TO_JSON(3.14))");
		((string)result!).Should().Contain("3.14");
	}

	// ─── SAFE_TO_JSON ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task SafeToJson_ValidInput()
	{
		var result = await Eval("TO_JSON_STRING(SAFE_TO_JSON(42))");
		result.Should().Be("42");
	}

	// ─── Chained JSON operations ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task ChainedJsonOperations()
	{
		var result = await Eval(
			"TO_JSON_STRING(JSON_SET(JSON_STRIP_NULLS(PARSE_JSON('{\"a\":null,\"b\":1}')), '$.c', 2))");
		result.Should().Be("{\"b\":1,\"c\":2}");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonArray_ThenAppend()
	{
		var result = await Eval(
			"TO_JSON_STRING(JSON_ARRAY_APPEND(JSON_ARRAY(1, 2), '$', 3))");
		result.Should().Be("[1,2,3]");
	}

	// ─── JSON in table columns ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task Json_InsertAndQuery()
	{
		var t = $"Jt_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Data JSON) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Data) VALUES (1, PARSE_JSON('{{\"name\":\"Alice\",\"age\":30}}'))");
		var rows = await QueryAsync($"SELECT JSON_VALUE(Data, '$.name') AS Name FROM {t}");
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task Json_UpdateColumn()
	{
		var t = $"Jt2_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Data JSON) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Data) VALUES (1, PARSE_JSON('{{\"x\":1}}'))");
		await ExecuteDmlAsync($"UPDATE {t} SET Data = JSON_SET(Data, '$.y', 2) WHERE Id = 1");
		var rows = await QueryAsync($"SELECT TO_JSON_STRING(Data) AS D FROM {t}");
		((string)rows[0]["D"]!).Should().Contain("\"y\":2");
	}

	// ─── JSON_VALUE with array index ───
	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"arr\":[10,20,30]}'), '$.arr[0]')", "10")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"arr\":[10,20,30]}'), '$.arr[2]')", "30")]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonValue_ArrayIndex(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── JSON_QUERY_ARRAY ───
	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonQueryArray_TopLevel()
	{
		var rows = await QueryAsync(
			"SELECT elem FROM UNNEST(JSON_QUERY_ARRAY(PARSE_JSON('[1,2,3]'))) AS elem");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "JsonExhaustive")]
	public async Task JsonQueryArray_NestedPath()
	{
		var rows = await QueryAsync(
			"SELECT elem FROM UNNEST(JSON_QUERY_ARRAY(PARSE_JSON('{\"items\":[1,2,3]}'), '$.items')) AS elem");
		rows.Should().HaveCount(3);
	}
}
