using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for statistical aggregate functions (STDDEV, VARIANCE, VAR_SAMP)
/// and advanced aggregate edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StatisticalAggregateIntegrationTests : IntegrationTestBase
{
	public StatisticalAggregateIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task<string> SetupStatsTable()
	{
		var table = $"Stats_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Val FLOAT64, Grp STRING(10)) PRIMARY KEY (Id)");
		return table;
	}

	// ═══════════════════════════════════════════════════════════════
	// STDDEV (STDDEV_SAMP) — sample standard deviation
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#stddev
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Stddev_BasicValues()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 2.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 6L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 7L, ["Val"] = 7.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 8L, ["Val"] = 9.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S FROM {table}");
		// Sample stddev of [2,4,4,4,5,5,7,9]: mean=5, sum_sq_dev=32, var_samp=32/7, stddev≈2.138
		((double)rows[0]["S"]!).Should().BeApproximately(2.138, 0.01);
	}

	[Fact]
	public async Task Stddev_SingleRow_ReturnsNull()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 5.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S FROM {table}");
		// Sample stddev with n=1 has N-1=0 denominator, typically returns NULL or 0
		// GCP Spanner returns NULL for n=1
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	public async Task Stddev_AllIdenticalValues()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S FROM {table}");
		((double)rows[0]["S"]!).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	public async Task Stddev_WithNulls_IgnoresNulls()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 2.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = null, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 6.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S FROM {table}");
		// stddev of [2, 4, 6] = 2.0
		((double)rows[0]["S"]!).Should().BeApproximately(2.0, 1e-10);
	}

	[Fact]
	public async Task Stddev_EmptyTable_ReturnsNull()
	{
		var table = await SetupStatsTable();
		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S FROM {table}");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	public async Task Stddev_GroupBy()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 3.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 10.0, ["Grp"] = "B" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 20.0, ["Grp"] = "B" });

		var rows = await QueryAsync($"SELECT Grp, STDDEV(Val) AS S FROM {table} GROUP BY Grp ORDER BY Grp");
		rows.Should().HaveCount(2);
		// Group A: stddev of [1, 3] = sqrt(2) ≈ 1.414
		((double)rows[0]["S"]!).Should().BeApproximately(Math.Sqrt(2), 0.01);
		// Group B: stddev of [10, 20] = sqrt(50) ≈ 7.07
		((double)rows[1]["S"]!).Should().BeApproximately(Math.Sqrt(50), 0.01);
	}

	// ═══════════════════════════════════════════════════════════════
	// VARIANCE (VAR_SAMP) — sample variance
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#variance
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Variance_BasicValues()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 2.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 6L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 7L, ["Val"] = 7.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 8L, ["Val"] = 9.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT VARIANCE(Val) AS V FROM {table}");
		// Sample variance of [2,4,4,4,5,5,7,9]: mean=5, sum_sq_dev=32, var_samp=32/7≈4.571
		((double)rows[0]["V"]!).Should().BeApproximately(4.571, 0.1);
	}

	[Fact]
	public async Task Variance_SingleRow_ReturnsNull()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 5.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT VARIANCE(Val) AS V FROM {table}");
		rows[0]["V"].Should().BeNull();
	}

	[Fact]
	public async Task Variance_AllIdentical_ReturnsZero()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 7.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 7.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 7.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT VARIANCE(Val) AS V FROM {table}");
		((double)rows[0]["V"]!).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	public async Task Variance_EmptyTable_ReturnsNull()
	{
		var table = await SetupStatsTable();
		var rows = await QueryAsync($"SELECT VARIANCE(Val) AS V FROM {table}");
		rows[0]["V"].Should().BeNull();
	}

	[Fact]
	public async Task Variance_WithNulls_IgnoresNulls()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 2.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 4.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 6.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT VARIANCE(Val) AS V FROM {table}");
		// Variance of [2, 4, 6] = 4.0
		((double)rows[0]["V"]!).Should().BeApproximately(4.0, 1e-10);
	}

	[Fact]
	public async Task Variance_GroupBy()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 3.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 10.0, ["Grp"] = "B" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 20.0, ["Grp"] = "B" });

		var rows = await QueryAsync($"SELECT Grp, VARIANCE(Val) AS V FROM {table} GROUP BY Grp ORDER BY Grp");
		rows.Should().HaveCount(2);
		// Group A: var of [1, 3] = 2.0
		((double)rows[0]["V"]!).Should().BeApproximately(2.0, 0.01);
		// Group B: var of [10, 20] = 50.0
		((double)rows[1]["V"]!).Should().BeApproximately(50.0, 0.01);
	}

	[Fact]
	public async Task Stddev_IsSquareRootOfVariance()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 5.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 10.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 15.0, ["Grp"] = "A" });

		var rows = await QueryAsync($"SELECT STDDEV(Val) AS S, VARIANCE(Val) AS V FROM {table}");
		var stddev = (double)rows[0]["S"]!;
		var variance = (double)rows[0]["V"]!;
		(stddev * stddev).Should().BeApproximately(variance, 1e-8);
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregate with ORDER BY (STRING_AGG, ARRAY_AGG)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringAgg_OrderBy()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 3.0, ["Grp"] = "C" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 2.0, ["Grp"] = "B" });

		var rows = await QueryAsync($"SELECT STRING_AGG(Grp, ',' ORDER BY Grp) AS S FROM {table}");
		rows[0]["S"].Should().Be("A,B,C");
	}

	[Fact]
	public async Task StringAgg_OrderByDesc()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 3.0, ["Grp"] = "C" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 2.0, ["Grp"] = "B" });

		var rows = await QueryAsync($"SELECT STRING_AGG(Grp, ',' ORDER BY Grp DESC) AS S FROM {table}");
		rows[0]["S"].Should().Be("C,B,A");
	}

	[Fact]
	public async Task StringAgg_WithNulls_IgnoresNulls()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 2.0, ["Grp"] = null });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 3.0, ["Grp"] = "C" });

		var rows = await QueryAsync($"SELECT STRING_AGG(Grp, ',' ORDER BY Grp) AS S FROM {table}");
		rows[0]["S"].Should().Be("A,C");
	}

	[Fact]
	public async Task StringAgg_Empty_ReturnsNull()
	{
		var table = await SetupStatsTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Grp, ',') AS S FROM {table}");
		rows[0]["S"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_AGG edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayAgg_OrderBy()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 3.0, ["Grp"] = "C" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 2.0, ["Grp"] = "B" });

		var rows = await QueryAsync(
			$"SELECT item FROM UNNEST((SELECT ARRAY_AGG(Grp ORDER BY Grp) FROM {table})) AS item");
		rows.Select(r => r["item"]).Should().BeEquivalentTo(
			new object[] { "A", "B", "C" },
			opts => opts.WithStrictOrdering());
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple DISTINCT aggregates  
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MultipleDistinctAggregates()
	{
		var table = await SetupStatsTable();
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 1.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 1.0, ["Grp"] = "B" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 2.0, ["Grp"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 2.0, ["Grp"] = "C" });

		var rows = await QueryAsync(
			$"SELECT COUNT(DISTINCT Val) AS DV, COUNT(DISTINCT Grp) AS DG FROM {table}");
		rows[0]["DV"].Should().Be(2L);
		rows[0]["DG"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// LOGICAL_AND / LOGICAL_OR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LogicalAnd_AllTrue()
	{
		var table = $"LA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Flag"] = true });

		var rows = await QueryAsync($"SELECT LOGICAL_AND(Flag) AS R FROM {table}");
		rows[0]["R"].Should().Be(true);
	}

	[Fact]
	public async Task LogicalAnd_OneFalse()
	{
		var table = $"LA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = false });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Flag"] = true });

		var rows = await QueryAsync($"SELECT LOGICAL_AND(Flag) AS R FROM {table}");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	public async Task LogicalAnd_AllNull()
	{
		var table = $"LA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = null });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = null });

		var rows = await QueryAsync($"SELECT LOGICAL_AND(Flag) AS R FROM {table}");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	public async Task LogicalAnd_Empty_ReturnsNull()
	{
		var table = $"LA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");

		var rows = await QueryAsync($"SELECT LOGICAL_AND(Flag) AS R FROM {table}");
		rows[0]["R"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT_AND / BIT_OR / BIT_XOR aggregates
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_and
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task BitAnd_Aggregate()
	{
		var table = $"BA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 0b1111L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 0b1100L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 0b1010L });

		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {table}");
		rows[0]["R"].Should().Be(0b1000L);
	}

	[Fact]
	public async Task BitAnd_Empty_ReturnsNull()
	{
		var table = $"BA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {table}");
		// Ref: BIT_AND on empty set returns -1 (all bits set) per SQL standard
		// GCP Spanner returns NULL for empty set
		rows[0]["R"].Should().BeNull();
	}
}
