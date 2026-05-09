using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive array function tests: ARRAY construction, ARRAY_LENGTH, ARRAY_CONCAT,
/// ARRAY_REVERSE, ARRAY_TO_STRING, UNNEST, ARRAY subquery, ARRAY_INCLUDES, etc.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArrayExhaustiveIntegrationTests : IntegrationTestBase
{
	public ArrayExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── ARRAY literal ───
	[Theory]
	[InlineData("[1, 2, 3]", new long[] { 1, 2, 3 })]
	[InlineData("[10]", new long[] { 10 })]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Array_Int64Literal(string array, long[] expected)
	{
		var rows = await QueryAsync($"SELECT val FROM UNNEST({array}) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().BeEquivalentTo(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Array_StringLiteral()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(['a', 'b', 'c']) AS val ORDER BY val");
		rows.Select(r => (string)r["val"]!).Should().BeEquivalentTo(new[] { "a", "b", "c" });
	}

	// ─── ARRAY_LENGTH ───
	[Theory]
	[InlineData("ARRAY_LENGTH([1,2,3])", 3L)]
	[InlineData("ARRAY_LENGTH([])", 0L)]
	[InlineData("ARRAY_LENGTH([10,20])", 2L)]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayLength(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayLength_Null()
	{
		var result = await Eval("ARRAY_LENGTH(CAST(NULL AS ARRAY<INT64>))");
		// NULL array returns NULL
		result.Should().BeNull();
	}

	// ─── ARRAY_CONCAT ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayConcat_TwoArrays()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_CONCAT([1,2], [3,4])) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L, 4L });
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayConcat_ThreeArrays()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_CONCAT([1], [2], [3])) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayConcat_Strings()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_CONCAT(['a','b'], ['c'])) AS val ORDER BY val");
		rows.Select(r => (string)r["val"]!).Should().BeEquivalentTo(new[] { "a", "b", "c" });
	}

	// ─── ARRAY_REVERSE ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayReverse_Int()
	{
		// Use WITH OFFSET to preserve array order (UNNEST order is not guaranteed on Cloud Spanner)
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_REVERSE([1,2,3])) AS val WITH OFFSET AS off ORDER BY off");
		rows.Select(r => (long)r["val"]!).Should().Equal(3L, 2L, 1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayReverse_Single()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_REVERSE([42])) AS val");
		rows.Select(r => (long)r["val"]!).Should().Equal(42L);
	}

	// ─── ARRAY_TO_STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	//   ARRAY_TO_STRING only accepts ARRAY<STRING> or ARRAY<BYTES>.
	[Theory]
	[InlineData("ARRAY_TO_STRING(['1','2','3'], ',')", "1,2,3")]
	[InlineData("ARRAY_TO_STRING(['a','b','c'], '-')", "a-b-c")]
	[InlineData("ARRAY_TO_STRING(['hello','world'], ' ')", "hello world")]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── UNNEST ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Unnest_WithOffset()
	{
		var rows = await QueryAsync("SELECT val, off FROM UNNEST([10,20,30]) AS val WITH OFFSET AS off ORDER BY off");
		rows[0]["val"].Should().Be(10L);
		rows[0]["off"].Should().Be(0L);
		rows[1]["val"].Should().Be(20L);
		rows[1]["off"].Should().Be(1L);
		rows[2]["val"].Should().Be(30L);
		rows[2]["off"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Unnest_Strings()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(['x','y','z']) AS val ORDER BY val");
		rows.Select(r => (string)r["val"]!).Should().BeEquivalentTo(new[] { "x", "y", "z" });
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Unnest_InWhere_Exists()
	{
		var t = $"ArrE_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A'), (2, 'B'), (3, 'C')");
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id IN UNNEST([1,3]) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "A", "C" });
	}

	// ─── GENERATE_ARRAY ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateArray_Int()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(GENERATE_ARRAY(1, 5)) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().Equal(1L, 2L, 3L, 4L, 5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateArray_WithStep()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(GENERATE_ARRAY(0, 10, 3)) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().Equal(0L, 3L, 6L, 9L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateArray_Descending()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(GENERATE_ARRAY(5, 1, -1)) AS val");
		rows.Select(r => (long)r["val"]!).Should().Equal(5L, 4L, 3L, 2L, 1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateArray_SingleElement()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(GENERATE_ARRAY(5, 5)) AS val");
		rows.Select(r => (long)r["val"]!).Should().Equal(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateArray_Empty()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(GENERATE_ARRAY(5, 1)) AS val");
		rows.Should().BeEmpty();
	}

	// ─── ARRAY subquery ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Array_Subquery()
	{
		var t = $"ArrSub_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var result = await Eval($"ARRAY_LENGTH(ARRAY(SELECT Val FROM {t}))");
		result.Should().Be(3L);
	}

	// ─── ARRAY in WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Array_InUnnestWhere()
	{
		var t = $"ArrW_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A'), (2, 'B'), (3, 'C'), (4, 'D')");
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id IN UNNEST([2, 4]) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "B", "D" });
	}

	// ─── ARRAY_INCLUDES (checks if value is in array) ───
	[Theory]
	[InlineData("2 IN UNNEST([1,2,3])", true)]
	[InlineData("5 IN UNNEST([1,2,3])", false)]
	[InlineData("'b' IN UNNEST(['a','b','c'])", true)]
	[InlineData("'d' IN UNNEST(['a','b','c'])", false)]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ValueInUnnest(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Nested UNNEST ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Unnest_NestedArray()
	{
		var result = await Eval("ARRAY_LENGTH(ARRAY_CONCAT([1,2], [3,4,5]))");
		result.Should().Be(5L);
	}

	// ─── ARRAY with aggregate ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayAgg_FromTable()
	{
		var t = $"ArrAgg_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 30), (2, 10), (3, 20)");
		var rows = await QueryAsync($"SELECT ARRAY_LENGTH(ARRAY_AGG(Val)) AS R FROM {t}");
		((long)rows[0]["R"]!).Should().Be(3L);
	}

	// ─── GENERATE_DATE_ARRAY ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateDateArray_Days()
	{
		var rows = await QueryAsync(
			"SELECT d FROM UNNEST(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-05')) AS d ORDER BY d");
		rows.Should().HaveCount(5);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task GenerateDateArray_Monthly()
	{
		var rows = await QueryAsync(
			"SELECT d FROM UNNEST(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-06-01', INTERVAL 1 MONTH)) AS d ORDER BY d");
		rows.Should().HaveCount(6);
	}

	// ─── Array with NULL elements ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task Array_WithNullElements()
	{
		var result = await Eval("ARRAY_LENGTH([1, NULL, 3])");
		result.Should().Be(3L);
	}

	// ─── ARRAY_TO_STRING with NULL handling ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayToString_NullSkipped()
	{
		// NULLs are omitted from the result
		var result = await Eval("ARRAY_TO_STRING(['a', NULL, 'c'], ',')");
		result.Should().Be("a,c");
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task ArrayToString_NullWithDefault()
	{
		var result = await Eval("ARRAY_TO_STRING(['a', NULL, 'c'], ',', 'N')");
		result.Should().Be("a,N,c");
	}

	// ─── ARRAY from SELECT with ORDER BY ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Array_SubqueryWithOrderBy()
	{
		var t = $"ArrOrd_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 30), (2, 10), (3, 20)");
		// Use WITH OFFSET to preserve array order (UNNEST order is not guaranteed on Cloud Spanner)
		var rows = await QueryAsync($"SELECT v FROM UNNEST(ARRAY(SELECT Val FROM {t} ORDER BY Val)) AS v WITH OFFSET AS off ORDER BY off");
		rows.Select(r => (long)r["v"]!).Should().Equal(10L, 20L, 30L);
	}

	// ─── Typed empty array literal (ARRAY<T>[]) ───
	[Theory]
	[InlineData("ARRAY_LENGTH(ARRAY<INT64>[])", 0L)]
	[InlineData("ARRAY_LENGTH(ARRAY<STRING>[])", 0L)]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task TypedEmptyArrayLiteral_HasZeroLength(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task TypedEmptyArrayLiteral_ArrayToString_ReturnsEmpty()
	{
		var result = await Eval("ARRAY_TO_STRING(ARRAY<STRING>[], ',')");
		result.Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task TypedEmptyArrayLiteral_ArrayConcat_WithNonEmpty()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY_CONCAT(ARRAY<INT64>[], [1,2])) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().BeEquivalentTo(new[] { 1L, 2L });
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task TypedEmptyArrayLiteral_ArrayReverse_ReturnsEmpty()
	{
		var result = await Eval("ARRAY_LENGTH(ARRAY_REVERSE(ARRAY<INT64>[]))");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task TypedArrayLiteral_NonEmpty()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(ARRAY<INT64>[1,2,3]) AS val ORDER BY val");
		rows.Select(r => (long)r["val"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
	}

	// ─── UNNEST cross join ───
	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task UnnestCrossJoin_CartesianProduct()
	{
		var rows = await QueryAsync("SELECT a, b FROM UNNEST([1,2]) AS a CROSS JOIN UNNEST([10,20]) AS b ORDER BY a, b");
		rows.Should().HaveCount(4);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayExhaustive")]
	public async Task UnnestCrossJoin_Expression()
	{
		var rows = await QueryAsync("SELECT a + b AS s FROM UNNEST([1,2]) AS a CROSS JOIN UNNEST([10,20]) AS b ORDER BY s");
		rows.Select(r => (long)r["s"]!).Should().Equal(11L, 12L, 21L, 22L);
	}
}
