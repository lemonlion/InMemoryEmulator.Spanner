using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for ARRAY functions, GENERATE_ARRAY, ARRAY_LENGTH, UNNEST, array subscript,
/// ARRAY_TO_STRING, ARRAY_REVERSE, ARRAY_CONCAT, and related operations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArrayFunctionIntegrationTests : IntegrationTestBase
{
	public ArrayFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task EnsureArrayTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE ArrTest (Id INT64 NOT NULL, Tags ARRAY<STRING(100)>, Nums ARRAY<INT64>) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH([1, 2, 3])", 3L)]
	[InlineData("ARRAY_LENGTH([1])", 1L)]
	[InlineData("ARRAY_LENGTH(CAST([] AS ARRAY<INT64>))", 0L)]
	[InlineData("ARRAY_LENGTH(['a', 'b', 'c', 'd'])", 4L)]
	[InlineData("ARRAY_LENGTH([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])", 10L)]
	public async Task ArrayLength(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ArrayLength_Null_ReturnsNull()
	{
		(await Eval("ARRAY_LENGTH(CAST(NULL AS ARRAY<INT64>))")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_TO_STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ', ')", "a, b, c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], '')", "abc")]
	[InlineData("ARRAY_TO_STRING(['hello'], ',')", "hello")]
	[InlineData("ARRAY_TO_STRING(['a'], '-')", "a")]
	[InlineData("ARRAY_TO_STRING(CAST([] AS ARRAY<STRING>), ',')", "")]
	[InlineData("ARRAY_TO_STRING(['x', 'y'], '|')", "x|y")]
	[InlineData("ARRAY_TO_STRING(['1', '2', '3'], '+')", "1+2+3")]
	public async Task ArrayToString(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', NULL, 'c'], ',', 'N')", "a,N,c")]
	[InlineData("ARRAY_TO_STRING(['a', NULL, 'c'], ',')", "a,c")]
	[InlineData("ARRAY_TO_STRING([NULL, NULL], ',', 'X')", "X,X")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayToString_WithNulls(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// GENERATE_ARRAY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(1, 5))", 5L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(1, 10))", 10L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(0, 0))", 1L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(1, 1))", 1L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(1, 5, 2))", 3L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(0, 10, 3))", 4L)]
	[InlineData("ARRAY_LENGTH(GENERATE_ARRAY(5, 1, -1))", 5L)]
	public async Task GenerateArray_Length(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ARRAY_TO_STRING(CAST(GENERATE_ARRAY(1, 5) AS ARRAY<STRING>), ',')", "1,2,3,4,5")]
	[InlineData("ARRAY_TO_STRING(CAST(GENERATE_ARRAY(1, 3) AS ARRAY<STRING>), '-')", "1-2-3")]
	[InlineData("ARRAY_TO_STRING(CAST(GENERATE_ARRAY(0, 0) AS ARRAY<STRING>), ',')", "0")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateArray_Content(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task GenerateArray_EmptyRange()
	{
		// GENERATE_ARRAY(5, 1) with default step 1 returns empty array
		(await Eval("ARRAY_LENGTH(GENERATE_ARRAY(5, 1))")).Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_TO_STRING(ARRAY_REVERSE(['a', 'b', 'c']), ',')", "c,b,a")]
	[InlineData("ARRAY_TO_STRING(ARRAY_REVERSE(['x']), ',')", "x")]
	[InlineData("ARRAY_TO_STRING(ARRAY_REVERSE(CAST([] AS ARRAY<STRING>)), ',')", "")]
	[InlineData("ARRAY_TO_STRING(ARRAY_REVERSE(['1', '2', '3', '4']), '-')", "4-3-2-1")]
	public async Task ArrayReverse(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_CONCAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_concat
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH(ARRAY_CONCAT([1, 2], [3, 4]))", 4L)]
	[InlineData("ARRAY_LENGTH(ARRAY_CONCAT([1], [2], [3]))", 3L)]
	[InlineData("ARRAY_LENGTH(ARRAY_CONCAT(CAST([] AS ARRAY<INT64>), [1, 2]))", 2L)]
	[InlineData("ARRAY_LENGTH(ARRAY_CONCAT([1, 2], CAST([] AS ARRAY<INT64>)))", 2L)]
	public async Task ArrayConcat_Length(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ARRAY_TO_STRING(ARRAY_CONCAT(['a', 'b'], ['c', 'd']), ',')", "a,b,c,d")]
	[InlineData("ARRAY_TO_STRING(ARRAY_CONCAT(['x'], ['y'], ['z']), '-')", "x-y-z")]
	[InlineData("ARRAY_TO_STRING(ARRAY_CONCAT(CAST([] AS ARRAY<STRING>), ['a']), ',')", "a")]
	public async Task ArrayConcat_Content(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Array subscript with OFFSET / ORDINAL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#accessing_array_elements
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("[10, 20, 30][OFFSET(0)]", 10L)]
	[InlineData("[10, 20, 30][OFFSET(1)]", 20L)]
	[InlineData("[10, 20, 30][OFFSET(2)]", 30L)]
	[InlineData("['a', 'b', 'c'][OFFSET(0)]", "a")]
	[InlineData("['a', 'b', 'c'][OFFSET(2)]", "c")]
	public async Task ArraySubscript_Offset(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("[10, 20, 30][ORDINAL(1)]", 10L)]
	[InlineData("[10, 20, 30][ORDINAL(2)]", 20L)]
	[InlineData("[10, 20, 30][ORDINAL(3)]", 30L)]
	[InlineData("['a', 'b', 'c'][ORDINAL(1)]", "a")]
	[InlineData("['a', 'b', 'c'][ORDINAL(3)]", "c")]
	public async Task ArraySubscript_Ordinal(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("[10, 20, 30][SAFE_OFFSET(0)]", 10L)]
	[InlineData("[10, 20, 30][SAFE_OFFSET(2)]", 30L)]
	public async Task ArraySubscript_SafeOffset_ValidIndex(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ArraySubscript_SafeOffset_OutOfBounds_ReturnsNull()
	{
		(await Eval("[10, 20, 30][SAFE_OFFSET(5)]")).Should().BeNull();
	}

	[Fact]
	public async Task ArraySubscript_SafeOrdinal_OutOfBounds_ReturnsNull()
	{
		(await Eval("[10, 20, 30][SAFE_ORDINAL(5)]")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// UNNEST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#unnest
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Unnest_BasicArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST([10, 20, 30]) AS val ORDER BY val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be(10L);
		rows[1]["val"].Should().Be(20L);
		rows[2]["val"].Should().Be(30L);
	}

	[Fact]
	public async Task Unnest_StringArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(['a', 'b', 'c']) AS val ORDER BY val");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("a");
		rows[1]["val"].Should().Be("b");
		rows[2]["val"].Should().Be("c");
	}

	[Fact]
	public async Task Unnest_EmptyArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(CAST([] AS ARRAY<INT64>)) AS val");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task Unnest_WithOffset()
	{
		var rows = await QueryAsync(
			"SELECT val, off FROM UNNEST(['x', 'y', 'z']) AS val WITH OFFSET off ORDER BY off");
		rows.Should().HaveCount(3);
		rows[0]["val"].Should().Be("x");
		rows[0]["off"].Should().Be(0L);
		rows[1]["val"].Should().Be("y");
		rows[1]["off"].Should().Be(1L);
		rows[2]["val"].Should().Be("z");
		rows[2]["off"].Should().Be(2L);
	}

	[Fact]
	public async Task Unnest_SingleElement()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST([42]) AS val");
		rows.Should().ContainSingle().Which["val"].Should().Be(42L);
	}

	[Fact]
	public async Task Unnest_InSubquery()
	{
		var rows = await QueryAsync(
			"SELECT val FROM UNNEST([1, 2, 3, 4, 5]) AS val WHERE val > 3 ORDER BY val");
		rows.Should().HaveCount(2);
		rows[0]["val"].Should().Be(4L);
		rows[1]["val"].Should().Be(5L);
	}

	[Fact]
	public async Task Unnest_WithAggregate()
	{
		var rows = await QueryAsync(
			"SELECT SUM(val) AS total FROM UNNEST([10, 20, 30]) AS val");
		rows[0]["total"].Should().Be(60L);
	}

	[Fact]
	public async Task Unnest_WithCount()
	{
		var rows = await QueryAsync(
			"SELECT COUNT(*) AS cnt FROM UNNEST([1, 2, 3, 4, 5]) AS val");
		rows[0]["cnt"].Should().Be(5L);
	}

	[Fact]
	public async Task Unnest_GenerateArray()
	{
		var rows = await QueryAsync(
			"SELECT val FROM UNNEST(GENERATE_ARRAY(1, 5)) AS val ORDER BY val");
		rows.Should().HaveCount(5);
		rows[0]["val"].Should().Be(1L);
		rows[4]["val"].Should().Be(5L);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArraySubquery_FromUnnest()
	{
		var result = await Eval(
			"ARRAY_LENGTH(ARRAY(SELECT val FROM UNNEST([10, 20, 30]) AS val WHERE val > 15))");
		result.Should().Be(2L);
	}

	[Fact]
	public async Task ArraySubquery_OrderedUnnest()
	{
		var result = await Eval(
			"ARRAY_TO_STRING(ARRAY(SELECT CAST(val AS STRING) FROM UNNEST([3, 1, 2]) AS val ORDER BY val), ',')");
		result.Should().Be("1,2,3");
	}

	// ═══════════════════════════════════════════════════════════════
	// Array literal expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("[1, 2, 3][OFFSET(0)]", 1L)]
	[InlineData("[1, 2, 3][OFFSET(1)]", 2L)]
	[InlineData("[1, 2, 3][OFFSET(2)]", 3L)]
	public async Task ArrayLiteral_IntAccess(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ARRAY_LENGTH([true, false, true])", 3L)]
	[InlineData("ARRAY_LENGTH([1.0, 2.0])", 2L)]
	public async Task ArrayLiteral_MixedTypes(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY column operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayColumn_InsertAndQuery()
	{
		await EnsureArrayTableAsync();
		try
		{
			await ExecuteDmlAsync(
				"INSERT INTO ArrTest (Id, Tags, Nums) VALUES (1, ['red', 'blue'], [10, 20, 30])");
		}
		catch { }

		var rows = await QueryAsync("SELECT Tags, Nums FROM ArrTest WHERE Id = 1");
		rows.Should().ContainSingle();
	}

	[Fact]
	public async Task ArrayColumn_Unnest()
	{
		await EnsureArrayTableAsync();
		try
		{
			await ExecuteDmlAsync(
				"INSERT INTO ArrTest (Id, Tags) VALUES (2, ['alpha', 'beta', 'gamma'])");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT tag FROM ArrTest, UNNEST(Tags) AS tag WHERE Id = 2 ORDER BY tag");
		rows.Should().HaveCount(3);
		rows[0]["tag"].Should().Be("alpha");
		rows[1]["tag"].Should().Be("beta");
		rows[2]["tag"].Should().Be("gamma");
	}

	// ═══════════════════════════════════════════════════════════════
	// SPLIT function (returns array)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c', ','))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('hello', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a|b|c|d', '|'))", 4L)]
	[InlineData("ARRAY_LENGTH(SPLIT('', ','))", 1L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,,b', ','))", 3L)]
	public async Task Split_Length(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ARRAY_TO_STRING(SPLIT('a,b,c', ','), '|')", "a|b|c")]
	[InlineData("ARRAY_TO_STRING(SPLIT('hello world', ' '), '-')", "hello-world")]
	[InlineData("ARRAY_TO_STRING(SPLIT('x', ','), '|')", "x")]
	public async Task Split_Content(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Array with NULL elements
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Array_WithNulls_Length()
	{
		(await Eval("ARRAY_LENGTH([1, NULL, 3])")).Should().Be(3L);
	}

	[Fact]
	public async Task Array_NullElement_Access()
	{
		(await Eval("[1, NULL, 3][OFFSET(1)]")).Should().BeNull();
	}

	[Fact]
	public async Task Unnest_WithNulls()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST([1, NULL, 3]) AS val ORDER BY val");
		// NULL sorts first or last depending on implementation
		rows.Should().HaveCount(3);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested array operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NestedArrayOps_ReverseAndConcat()
	{
		(await Eval(
			"ARRAY_TO_STRING(ARRAY_REVERSE(ARRAY_CONCAT(['a', 'b'], ['c', 'd'])), ',')"))
			.Should().Be("d,c,b,a");
	}

	[Fact]
	public async Task NestedArrayOps_SplitAndReverse()
	{
		(await Eval(
			"ARRAY_TO_STRING(ARRAY_REVERSE(SPLIT('a,b,c', ',')), ',')"))
			.Should().Be("c,b,a");
	}

	[Fact]
	public async Task NestedArrayOps_GenerateAndLength()
	{
		(await Eval("ARRAY_LENGTH(ARRAY_CONCAT(GENERATE_ARRAY(1, 3), GENERATE_ARRAY(4, 6)))"))
			.Should().Be(6L);
	}
}
