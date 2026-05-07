using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Window function additional edge cases: ROW_NUMBER, RANK, DENSE_RANK, NTILE,
/// LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTH_VALUE with NULLs and boundaries.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class WindowFunctionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public WindowFunctionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

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

	private async Task<List<(object?, object?)>> EvalTwoColMultiRow(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<(object?, object?)>();
		while (await reader.ReadAsync())
		{
			var c1 = reader.IsDBNull(0) ? null : reader.GetValue(0);
			var c2 = reader.IsDBNull(1) ? null : reader.GetValue(1);
			results.Add((c1, c2));
		}
		return results;
	}

	// ═══════════════════════════════════════════════════════════════
	// ROW_NUMBER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task RowNumber_Sequential()
	{
		var results = await EvalMultiRow(
			"SELECT ROW_NUMBER() OVER (ORDER BY x) FROM (SELECT 30 AS x UNION ALL SELECT 10 UNION ALL SELECT 20)");
		results.Should().HaveCount(3);
		results.Should().BeEquivalentTo(new object[] { 1L, 2L, 3L });
	}

	[Fact]
	public async Task RowNumber_WithPartition()
	{
		var results = await EvalTwoColMultiRow(
			"SELECT g, ROW_NUMBER() OVER (PARTITION BY g ORDER BY x) " +
			"FROM (SELECT 'a' AS g, 2 AS x UNION ALL SELECT 'a', 1 UNION ALL SELECT 'b', 1) ORDER BY g, x");
		results.Should().HaveCount(3);
		// 'a' group: ROW_NUMBER 1, 2
		// 'b' group: ROW_NUMBER 1
		results[0].Item2.Should().Be(1L);
		results[1].Item2.Should().Be(2L);
		results[2].Item2.Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// RANK and DENSE_RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Rank_WithTies()
	{
		var results = await EvalTwoColMultiRow(
			"SELECT x, RANK() OVER (ORDER BY x) " +
			"FROM (SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 2) ORDER BY x");
		// 1,1 → rank 1; 1,1 → rank 1; 2 → rank 3
		results[0].Item2.Should().Be(1L);
		results[1].Item2.Should().Be(1L);
		results[2].Item2.Should().Be(3L);
	}

	[Fact]
	public async Task DenseRank_WithTies()
	{
		var results = await EvalTwoColMultiRow(
			"SELECT x, DENSE_RANK() OVER (ORDER BY x) " +
			"FROM (SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 2) ORDER BY x");
		// 1,1 → dense_rank 1; 1,1 → dense_rank 1; 2 → dense_rank 2
		results[0].Item2.Should().Be(1L);
		results[1].Item2.Should().Be(1L);
		results[2].Item2.Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// LAG / LEAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#lag
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Lag_DefaultOffset()
	{
		var results = await EvalMultiRow(
			"SELECT LAG(x) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// 10 → null (no previous), 20 → 10, 30 → 20
		results[0].Should().BeNull();
		results[1].Should().Be(10L);
		results[2].Should().Be(20L);
	}

	[Fact]
	public async Task Lead_DefaultOffset()
	{
		var results = await EvalMultiRow(
			"SELECT LEAD(x) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// 10 → 20, 20 → 30, 30 → null
		results[0].Should().Be(20L);
		results[1].Should().Be(30L);
		results[2].Should().BeNull();
	}

	[Fact]
	public async Task Lag_WithOffset2()
	{
		var results = await EvalMultiRow(
			"SELECT LAG(x, 2) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// offset=2: 10 → null, 20 → null, 30 → 10
		results[0].Should().BeNull();
		results[1].Should().BeNull();
		results[2].Should().Be(10L);
	}

	[Fact]
	public async Task Lead_WithOffset2()
	{
		var results = await EvalMultiRow(
			"SELECT LEAD(x, 2) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// offset=2: 10 → 30, 20 → null, 30 → null
		results[0].Should().Be(30L);
		results[1].Should().BeNull();
		results[2].Should().BeNull();
	}

	[Fact]
	public async Task Lag_WithDefault()
	{
		var results = await EvalMultiRow(
			"SELECT LAG(x, 1, -1) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// 10 → -1 (default), 20 → 10, 30 → 20
		results[0].Should().Be(-1L);
		results[1].Should().Be(10L);
		results[2].Should().Be(20L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FIRST_VALUE / LAST_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#first_value
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task FirstValue_OverEntirePartition()
	{
		var results = await EvalMultiRow(
			"SELECT FIRST_VALUE(x) OVER (ORDER BY x ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) " +
			"FROM (SELECT 30 AS x UNION ALL SELECT 10 UNION ALL SELECT 20) ORDER BY x");
		// All rows should see 10 as first value
		results.Should().AllBeEquivalentTo(10L);
	}

	[Fact]
	public async Task LastValue_OverEntirePartition()
	{
		var results = await EvalMultiRow(
			"SELECT LAST_VALUE(x) OVER (ORDER BY x ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) " +
			"FROM (SELECT 30 AS x UNION ALL SELECT 10 UNION ALL SELECT 20) ORDER BY x");
		// All rows should see 30 as last value
		results.Should().AllBeEquivalentTo(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM / AVG as window functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SumWindow_RunningTotal()
	{
		var results = await EvalMultiRow(
			"SELECT SUM(x) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// Running sum: 10, 30, 60
		results[0].Should().Be(10L);
		results[1].Should().Be(30L);
		results[2].Should().Be(60L);
	}

	[Fact]
	public async Task CountWindow_RunningCount()
	{
		var results = await EvalMultiRow(
			"SELECT COUNT(*) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// Running count: 1, 2, 3
		results[0].Should().Be(1L);
		results[1].Should().Be(2L);
		results[2].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Window functions with NULLs in data
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Lag_WithNullValues()
	{
		var results = await EvalMultiRow(
			"SELECT LAG(x) OVER (ORDER BY COALESCE(x, -999)) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT NULL UNION ALL SELECT 30) ORDER BY COALESCE(x, -999)");
		// Sorted: NULL(-999), 10, 30
		// LAG: null → null, 10 → null (prev is NULL), 30 → 10
		results[0].Should().BeNull();
		results[1].Should().BeNull();
		results[2].Should().Be(10L);
	}

	// ═══════════════════════════════════════════════════════════════
	// NTILE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#ntile
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Ntile_EvenDistribution()
	{
		var results = await EvalMultiRow(
			"SELECT NTILE(2) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30 UNION ALL SELECT 40) ORDER BY x");
		// 4 rows, 2 buckets: 10→1, 20→1, 30→2, 40→2
		results[0].Should().Be(1L);
		results[1].Should().Be(1L);
		results[2].Should().Be(2L);
		results[3].Should().Be(2L);
	}

	[Fact]
	public async Task Ntile_UnevenDistribution()
	{
		var results = await EvalMultiRow(
			"SELECT NTILE(2) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		// 3 rows, 2 buckets: 10→1, 20→1, 30→2
		results[0].Should().Be(1L);
		results[1].Should().Be(1L);
		results[2].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple window functions in same query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MultipleWindowFuns_SameQuery()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT x, ROW_NUMBER() OVER (ORDER BY x), SUM(x) OVER (ORDER BY x) " +
			"FROM (SELECT 10 AS x UNION ALL SELECT 20 UNION ALL SELECT 30) ORDER BY x");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(10L);
		reader.GetInt64(1).Should().Be(1L);
		reader.GetInt64(2).Should().Be(10L);

		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(20L);
		reader.GetInt64(1).Should().Be(2L);
		reader.GetInt64(2).Should().Be(30L);

		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(30L);
		reader.GetInt64(1).Should().Be(3L);
		reader.GetInt64(2).Should().Be(60L);
	}
}
