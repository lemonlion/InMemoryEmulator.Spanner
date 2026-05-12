using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Aggregate function additional edge cases: empty input, single values, NULL handling,
/// COUNT variations, HAVING clause, GROUP BY with NULLs.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateAdvancedIntegrationTests : IntegrationTestBase
{
	public AggregateAdvancedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task<List<object?>> EvalMultiRow(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<object?>();
		while (await reader.ReadAsync())
		{
			results.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
		}
		return results;
	}

	private async Task<long> EvalCount(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.GetInt64(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNT variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#count
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Count_Star_OnEmptyUnion()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#where_clause
		//   WHERE without FROM is invalid; use UNNEST of empty array for zero rows.
		(await EvalCount("SELECT COUNT(*) FROM UNNEST(ARRAY<INT64>[]) AS x")).Should().Be(0L);
	}

	[Fact]
	public async Task Count_Star_SingleRow()
	{
		(await EvalCount("SELECT COUNT(*) FROM (SELECT 1)")).Should().Be(1L);
	}

	[Fact]
	public async Task Count_Star_MultipleRows()
	{
		(await EvalCount("SELECT COUNT(*) FROM (SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3)")).Should().Be(3L);
	}

	[Fact]
	public async Task Count_Column_SkipsNulls()
	{
		(await EvalCount("SELECT COUNT(x) FROM (SELECT 1 AS x UNION ALL SELECT NULL UNION ALL SELECT 3)")).Should().Be(2L);
	}

	[Fact]
	public async Task Count_AllNulls_ReturnsZero()
	{
		(await EvalCount("SELECT COUNT(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT NULL)")).Should().Be(0L);
	}

	[Fact]
	public async Task Count_Distinct()
	{
		(await EvalCount("SELECT COUNT(DISTINCT x) FROM (SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 2)")).Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#sum
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_IntValues()
	{
		(await Eval("SELECT SUM(x) FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3)")).Should().Be(6L);
	}

	[Fact]
	public async Task Sum_SkipsNulls()
	{
		(await Eval("SELECT SUM(x) FROM (SELECT 1 AS x UNION ALL SELECT NULL UNION ALL SELECT 3)")).Should().Be(4L);
	}

	[Fact]
	public async Task Sum_AllNulls_ReturnsNull()
	{
		(await Eval("SELECT SUM(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT NULL)")).Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Sum_Empty_ReturnsNull()
	{
		(await Eval("SELECT SUM(x) FROM UNNEST(ARRAY<INT64>[]) AS x")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// AVG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#avg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Avg_IntValues()
	{
		var result = Convert.ToDouble(await Eval("SELECT AVG(x) FROM (SELECT 2 AS x UNION ALL SELECT 4 UNION ALL SELECT 6)"));
		result.Should().BeApproximately(4.0, 1e-10);
	}

	[Fact]
	public async Task Avg_SkipsNulls()
	{
		var result = Convert.ToDouble(await Eval("SELECT AVG(x) FROM (SELECT 2 AS x UNION ALL SELECT NULL UNION ALL SELECT 4)"));
		result.Should().BeApproximately(3.0, 1e-10);
	}

	[Fact]
	public async Task Avg_AllNulls_ReturnsNull()
	{
		(await Eval("SELECT AVG(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT NULL)")).Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Avg_Empty_ReturnsNull()
	{
		(await Eval("SELECT AVG(x) FROM UNNEST(ARRAY<INT64>[]) AS x")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// MIN / MAX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#min
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Min_IntValues()
	{
		(await Eval("SELECT MIN(x) FROM (SELECT 3 AS x UNION ALL SELECT 1 UNION ALL SELECT 2)")).Should().Be(1L);
	}

	[Fact]
	public async Task Max_IntValues()
	{
		(await Eval("SELECT MAX(x) FROM (SELECT 3 AS x UNION ALL SELECT 1 UNION ALL SELECT 2)")).Should().Be(3L);
	}

	[Fact]
	public async Task Min_SkipsNulls()
	{
		(await Eval("SELECT MIN(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT 3 UNION ALL SELECT 1)")).Should().Be(1L);
	}

	[Fact]
	public async Task Max_SkipsNulls()
	{
		(await Eval("SELECT MAX(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT 1 UNION ALL SELECT 3)")).Should().Be(3L);
	}

	[Fact]
	public async Task Min_AllNulls_ReturnsNull()
	{
		(await Eval("SELECT MIN(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task Max_AllNulls_ReturnsNull()
	{
		(await Eval("SELECT MAX(x) FROM (SELECT CAST(NULL AS INT64) AS x UNION ALL SELECT NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task Min_StringValues()
	{
		(await Eval("SELECT MIN(x) FROM (SELECT 'c' AS x UNION ALL SELECT 'a' UNION ALL SELECT 'b')")).Should().Be("a");
	}

	[Fact]
	public async Task Max_StringValues()
	{
		(await Eval("SELECT MAX(x) FROM (SELECT 'c' AS x UNION ALL SELECT 'a' UNION ALL SELECT 'b')")).Should().Be("c");
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringAgg_BasicValues()
	{
		var result = await Eval("SELECT STRING_AGG(x, ',') FROM (SELECT 'a' AS x UNION ALL SELECT 'b' UNION ALL SELECT 'c')");
		var str = (string)result!;
		// Order is not guaranteed, but all values should be present
		str.Split(',').Should().BeEquivalentTo(new[] { "a", "b", "c" });
	}

	[Fact]
	public async Task StringAgg_SkipsNulls()
	{
		var result = await Eval("SELECT STRING_AGG(x, ',') FROM (SELECT 'a' AS x UNION ALL SELECT NULL UNION ALL SELECT 'c')");
		var str = (string)result!;
		str.Split(',').Should().BeEquivalentTo(new[] { "a", "c" });
	}

	[Fact]
	public async Task StringAgg_AllNulls_ReturnsNull()
	{
		(await Eval("SELECT STRING_AGG(x, ',') FROM (SELECT CAST(NULL AS STRING) AS x UNION ALL SELECT NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayAgg_BasicValues()
	{
		var result = await EvalCount("SELECT ARRAY_LENGTH(ARRAY_AGG(x)) FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3)");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task ArrayAgg_IncludesNulls()
	{
		// ARRAY_AGG includes NULLs by default
		var result = await EvalCount("SELECT ARRAY_LENGTH(ARRAY_AGG(x)) FROM (SELECT 1 AS x UNION ALL SELECT CAST(NULL AS INT64) UNION ALL SELECT 3)");
		result.Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// GROUP BY with various key types
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_IntKeys()
	{
		var results = await EvalMultiRow(
			"SELECT k FROM (SELECT 1 AS k UNION ALL SELECT 2 UNION ALL SELECT 1) GROUP BY k ORDER BY k");
		results.Should().HaveCount(2);
		results[0].Should().Be(1L);
		results[1].Should().Be(2L);
	}

	[Fact]
	public async Task GroupBy_StringKeys()
	{
		var results = await EvalMultiRow(
			"SELECT k FROM (SELECT 'a' AS k UNION ALL SELECT 'b' UNION ALL SELECT 'a') GROUP BY k ORDER BY k");
		results.Should().HaveCount(2);
		results[0].Should().Be("a");
		results[1].Should().Be("b");
	}

	[Fact]
	public async Task GroupBy_BoolKeys()
	{
		var results = await EvalMultiRow(
			"SELECT k FROM (SELECT true AS k UNION ALL SELECT false UNION ALL SELECT true) GROUP BY k ORDER BY k");
		results.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// HAVING
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Having_FiltersGroups()
	{
		var results = await EvalMultiRow(
			"SELECT k FROM (SELECT 1 AS k UNION ALL SELECT 1 UNION ALL SELECT 2) GROUP BY k HAVING COUNT(*) > 1");
		results.Should().HaveCount(1);
		results[0].Should().Be(1L);
	}

	[Fact]
	public async Task Having_NoMatchingGroups_ReturnsEmpty()
	{
		var results = await EvalMultiRow(
			"SELECT k FROM (SELECT 1 AS k UNION ALL SELECT 2 UNION ALL SELECT 3) GROUP BY k HAVING COUNT(*) > 1");
		results.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// ORDER BY with NULLs
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
	//   "NULLs are sorted first for ascending order, and last for descending order."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task OrderBy_NullsFirstInAsc()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 2 AS x UNION ALL SELECT NULL UNION ALL SELECT 1) ORDER BY x ASC");
		results.Should().HaveCount(3);
		results[0].Should().BeNull();
		results[1].Should().Be(1L);
		results[2].Should().Be(2L);
	}

	[Fact]
	public async Task OrderBy_NullsLastInDesc()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 2 AS x UNION ALL SELECT NULL UNION ALL SELECT 1) ORDER BY x DESC");
		results.Should().HaveCount(3);
		results[0].Should().Be(2L);
		results[1].Should().Be(1L);
		results[2].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// LIMIT and OFFSET
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Limit_Zero_ReturnsEmpty()
	{
		var results = await EvalMultiRow("SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2) LIMIT 0");
		results.Should().BeEmpty();
	}

	[Fact]
	public async Task Limit_One_ReturnsSingleRow()
	{
		var results = await EvalMultiRow("SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) ORDER BY x LIMIT 1");
		results.Should().HaveCount(1);
		results[0].Should().Be(1L);
	}

	[Fact]
	public async Task Limit_WithOffset()
	{
		var results = await EvalMultiRow("SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) ORDER BY x LIMIT 1 OFFSET 1");
		results.Should().HaveCount(1);
		results[0].Should().Be(2L);
	}

	[Fact]
	public async Task Limit_OffsetBeyondRows_ReturnsEmpty()
	{
		var results = await EvalMultiRow("SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2) ORDER BY x LIMIT 10 OFFSET 10");
		results.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Distinct_RemovesDuplicates()
	{
		var results = await EvalMultiRow(
			"SELECT DISTINCT x FROM (SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 2) ORDER BY x");
		results.Should().HaveCount(2);
		results[0].Should().Be(1L);
		results[1].Should().Be(2L);
	}

	[Fact]
	public async Task Distinct_WithNulls()
	{
		var results = await EvalMultiRow(
			"SELECT DISTINCT x FROM (SELECT NULL AS x UNION ALL SELECT NULL UNION ALL SELECT 1) ORDER BY x");
		results.Should().HaveCount(2); // NULL and 1
	}

	// ═══════════════════════════════════════════════════════════════
	// Subqueries in SELECT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ScalarSubquery_SingleValue()
	{
		(await Eval("SELECT (SELECT 42)")).Should().Be(42L);
	}

	[Fact]
	public async Task ScalarSubquery_NullValue()
	{
		(await Eval("SELECT (SELECT CAST(NULL AS INT64))")).Should().BeNull();
	}

	[Fact]
	public async Task Exists_True()
	{
		(await Eval("SELECT EXISTS(SELECT 1)")).Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Exists_False()
	{
		(await Eval("SELECT EXISTS(SELECT 1 FROM UNNEST(ARRAY<INT64>[]) AS x)")).Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NotExists_True()
	{
		(await Eval("SELECT NOT EXISTS(SELECT 1 FROM UNNEST(ARRAY<INT64>[]) AS x)")).Should().Be(true);
	}

	[Fact]
	public async Task NotExists_False()
	{
		(await Eval("SELECT NOT EXISTS(SELECT 1)")).Should().Be(false);
	}
}
