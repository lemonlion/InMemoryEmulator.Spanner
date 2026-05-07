using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for array functions and UNNEST operations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArrayFunctionCoreIntegrationTests : IntegrationTestBase
{
	public ArrayFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── ARRAY_LENGTH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_length

	[Theory]
	[InlineData("ARRAY_LENGTH([1,2,3])", 3L)]
	[InlineData("ARRAY_LENGTH([1])", 1L)]
	[InlineData("ARRAY_LENGTH(CAST([] AS ARRAY<INT64>))", 0L)]
	[InlineData("ARRAY_LENGTH([1,2,3,4,5])", 5L)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayLength_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayLength_Null_ReturnsNull()
	{
		var result = await Eval("ARRAY_LENGTH(CAST(NULL AS ARRAY<INT64>))");
		result.Should().BeNull();
	}

	// ─── ARRAY_CONCAT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_concat

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayConcat_TwoArrays()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_CONCAT([1,2], [3,4])) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(1L, 2L, 3L, 4L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayConcat_ThreeArrays()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_CONCAT([1], [2], [3])) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(1L, 2L, 3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayConcat_WithEmptyArray()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_CONCAT([1,2], CAST([] AS ARRAY<INT64>), [3])) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(1L, 2L, 3L);
	}

	// ─── ARRAY_TO_STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(['x'], '-')", "x")]
	[InlineData("ARRAY_TO_STRING(['hello', 'world'], ' ')", "hello world")]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayToString_Cases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── ARRAY_REVERSE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_reverse

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayReverse_ReversesElements()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_REVERSE([1,2,3])) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(3L, 2L, 1L);
	}

	// ─── GENERATE_ARRAY ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateArray_DefaultStep()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(GENERATE_ARRAY(1, 5)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(1L, 2L, 3L, 4L, 5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateArray_WithStep()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(GENERATE_ARRAY(0, 10, 3)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(0L, 3L, 6L, 9L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateArray_NegativeStep()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(GENERATE_ARRAY(5, 1, -1)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(5L, 4L, 3L, 2L, 1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task GenerateArray_SingleElement()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(GENERATE_ARRAY(5, 5)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task GenerateArray_EmptyRange()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(GENERATE_ARRAY(5, 1)) AS v");
		rows.Should().BeEmpty();
	}

	// ─── ARRAY_INCLUDES ───

	[Theory]
	[InlineData("ARRAY_INCLUDES([1,2,3], 2)", true)]
	[InlineData("ARRAY_INCLUDES([1,2,3], 4)", false)]
	[InlineData("ARRAY_INCLUDES(['a','b','c'], 'b')", true)]
	[InlineData("ARRAY_INCLUDES(['a','b','c'], 'd')", false)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayIncludes_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── ARRAY_INCLUDES_ANY / ARRAY_INCLUDES_ALL ───

	[Theory]
	[InlineData("ARRAY_INCLUDES_ANY([1,2,3], [2,4])", true)]
	[InlineData("ARRAY_INCLUDES_ANY([1,2,3], [4,5])", false)]
	[InlineData("ARRAY_INCLUDES_ALL([1,2,3], [1,2])", true)]
	[InlineData("ARRAY_INCLUDES_ALL([1,2,3], [1,4])", false)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayIncludesAnyAll_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── ARRAY_FIRST / ARRAY_LAST ───

	[Theory]
	[InlineData("ARRAY_FIRST([10,20,30])", 10L)]
	[InlineData("ARRAY_LAST([10,20,30])", 30L)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayFirstLast_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── ARRAY_MIN / ARRAY_MAX ───

	[Theory]
	[InlineData("ARRAY_MIN([3,1,2])", 1L)]
	[InlineData("ARRAY_MAX([3,1,2])", 3L)]
	[InlineData("ARRAY_MIN([42])", 42L)]
	[InlineData("ARRAY_MAX([42])", 42L)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayMinMax_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── ARRAY_SUM / ARRAY_AVG ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArraySum_IntArray()
	{
		var result = await Eval("ARRAY_SUM([1,2,3,4,5])");
		result.Should().Be(15L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayAvg_IntArray()
	{
		var result = await Eval("ARRAY_AVG([10,20,30])");
		((double)result!).Should().BeApproximately(20.0, 0.01);
	}

	// ─── ARRAY_SLICE ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArraySlice_BasicCase()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_slice
		//   ARRAY_SLICE uses 0-based indexing, inclusive on both ends
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_SLICE([10,20,30,40,50], 1, 3)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(20L, 30L, 40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArraySlice_FromMiddle()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(ARRAY_SLICE([10,20,30,40,50], 2, 4)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(30L, 40L, 50L);
	}

	// ─── ARRAY_IS_DISTINCT ───

	[Theory]
	[InlineData("ARRAY_IS_DISTINCT([1,2,3])", true)]
	[InlineData("ARRAY_IS_DISTINCT([1,2,2])", false)]
	[InlineData("ARRAY_IS_DISTINCT([1])", true)]
	[InlineData("ARRAY_IS_DISTINCT(CAST([] AS ARRAY<INT64>))", true)]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayIsDistinct_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── UNNEST ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task Unnest_BasicCase()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST([10, 20, 30]) AS v ORDER BY v");
		rows.Select(r => (long)r["v"]!).Should().Equal(10L, 20L, 30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task Unnest_StringArray()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(['a', 'b', 'c']) AS v ORDER BY v");
		rows.Select(r => (string)r["v"]!).Should().Equal("a", "b", "c");
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task Unnest_EmptyArray()
	{
		var rows = await QueryAsync("SELECT v FROM UNNEST(CAST([] AS ARRAY<INT64>)) AS v");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task Unnest_WithOffset()
	{
		var rows = await QueryAsync("SELECT v, off FROM UNNEST(['a','b','c']) AS v WITH OFFSET off ORDER BY off");
		rows.Should().HaveCount(3);
		rows[0]["v"].Should().Be("a");
		rows[0]["off"].Should().Be(0L);
		rows[2]["v"].Should().Be("c");
		rows[2]["off"].Should().Be(2L);
	}

	// ─── ARRAY column in table ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayColumn_InsertAndQuery()
	{
		var table = "ArrTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Tags ARRAY<STRING(MAX)>) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Tags) VALUES (1, ['red', 'blue', 'green'])");

		var rows = await QueryAsync($"SELECT ARRAY_LENGTH(Tags) AS R FROM {table}");
		rows[0]["R"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayColumn_UnnestInJoin()
	{
		var table = "ArrTbl2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Vals ARRAY<INT64>) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Vals) VALUES (1, [10, 20, 30])");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Vals) VALUES (2, [40, 50])");

		var rows = await QueryAsync($"SELECT t.Id, v FROM {table} t, UNNEST(t.Vals) AS v ORDER BY t.Id, v");
		rows.Should().HaveCount(5);
		rows[0]["Id"].Should().Be(1L);
		rows[0]["v"].Should().Be(10L);
	}

	// ─── Literal array in WHERE ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArrayInWhere_UsingArrayIncludes()
	{
		var table = "ArrWhere1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Tags ARRAY<STRING(MAX)>) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Tags) VALUES (1, ['red', 'blue'])");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Tags) VALUES (2, ['green', 'yellow'])");
		await ExecuteDmlAsync($"INSERT INTO {table} (Id, Tags) VALUES (3, ['red', 'green'])");

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE ARRAY_INCLUDES(Tags, 'red') ORDER BY Id");
		rows.Select(r => (long)r["Id"]!).Should().Equal(1L, 3L);
	}

	// ─── ARRAY subquery ───

	[Fact]
	[Trait(TestTraits.Category, "ArrayFunction")]
	public async Task ArraySubquery_FromTable()
	{
		var table = "ArrSub1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new() { ["Id"] = 1L, ["Val"] = 10L },
			new() { ["Id"] = 2L, ["Val"] = 20L },
			new() { ["Id"] = 3L, ["Val"] = 30L });

		var result = await Eval($"ARRAY_LENGTH(ARRAY(SELECT Val FROM {table}))");
		result.Should().Be(3L);
	}
}
