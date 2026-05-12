using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Dense aggregate function tests with data.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateCombinationIntegrationTests : IntegrationTestBase
{
	public AggregateCombinationIntegrationTests(EmulatorSession session) : base(session) { }
private readonly string _t = $"AggComb_{Guid.NewGuid():N}";

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// Setup: seed data for aggregate tests
	// ═══════════════════════════════════════════════════════════════

	private async Task EnsureAggTable()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE " + _t + @" (
				Id INT64 NOT NULL,
				GroupKey STRING(10),
				IntVal INT64,
				FloatVal FLOAT64,
				StrVal STRING(100),
				BoolVal BOOL
			) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task SeedAggData()
	{
		await EnsureAggTable();
		await InsertAsync(_t,
			new Dictionary<string, object?> { ["Id"] = 1L, ["GroupKey"] = "A", ["IntVal"] = 10L, ["FloatVal"] = 1.5, ["StrVal"] = "apple", ["BoolVal"] = true },
			new Dictionary<string, object?> { ["Id"] = 2L, ["GroupKey"] = "A", ["IntVal"] = 20L, ["FloatVal"] = 2.5, ["StrVal"] = "banana", ["BoolVal"] = true },
			new Dictionary<string, object?> { ["Id"] = 3L, ["GroupKey"] = "A", ["IntVal"] = 30L, ["FloatVal"] = 3.5, ["StrVal"] = "cherry", ["BoolVal"] = false },
			new Dictionary<string, object?> { ["Id"] = 4L, ["GroupKey"] = "B", ["IntVal"] = 40L, ["FloatVal"] = 4.5, ["StrVal"] = "date", ["BoolVal"] = true },
			new Dictionary<string, object?> { ["Id"] = 5L, ["GroupKey"] = "B", ["IntVal"] = 50L, ["FloatVal"] = 5.5, ["StrVal"] = "elderberry", ["BoolVal"] = false },
			new Dictionary<string, object?> { ["Id"] = 6L, ["GroupKey"] = "C", ["IntVal"] = null, ["FloatVal"] = null, ["StrVal"] = null, ["BoolVal"] = null });
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#count
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Count_Star()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM " + _t + @"");
		rows[0]["C"].Should().Be(6L);
	}

	[Fact]
	public async Task Count_Column_ExcludesNull()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT COUNT(IntVal) AS C FROM " + _t + @"");
		rows[0]["C"].Should().Be(5L);
	}

	[Fact]
	public async Task Count_Distinct()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT COUNT(DISTINCT GroupKey) AS C FROM " + _t + @"");
		rows[0]["C"].Should().Be(3L);
	}

	[Fact]
	public async Task CountIf_TrueCondition()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT COUNTIF(IntVal > 25) AS C FROM " + _t + @"");
		rows[0]["C"].Should().Be(3L);
	}

	[Fact]
	public async Task CountIf_AllFalse()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT COUNTIF(IntVal > 999) AS C FROM " + _t + @"");
		rows[0]["C"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#sum
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_Int64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT SUM(IntVal) AS S FROM " + _t + @"");
		rows[0]["S"].Should().Be(150L);
	}

	[Fact]
	public async Task Sum_Float64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT SUM(FloatVal) AS S FROM " + _t + @"");
		((double)rows[0]["S"]!).Should().BeApproximately(17.5, 1e-10);
	}

	[Fact]
	public async Task Sum_WithGroupBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, SUM(IntVal) AS S FROM " + _t + @" GROUP BY GroupKey ORDER BY GroupKey");
		rows.Should().HaveCount(3);
		rows[0]["GroupKey"].Should().Be("A");
		rows[0]["S"].Should().Be(60L);
		rows[1]["GroupKey"].Should().Be("B");
		rows[1]["S"].Should().Be(90L);
	}

	[Fact]
	public async Task Sum_GroupWithNull()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, SUM(IntVal) AS S FROM " + _t + @" WHERE GroupKey = 'C' GROUP BY GroupKey");
		rows.Should().ContainSingle();
		rows[0]["S"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// AVG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#avg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Avg_Int64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT AVG(IntVal) AS A FROM " + _t + @"");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 1e-10);
	}

	[Fact]
	public async Task Avg_Float64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT AVG(FloatVal) AS A FROM " + _t + @"");
		((double)rows[0]["A"]!).Should().BeApproximately(3.5, 1e-10);
	}

	[Fact]
	public async Task Avg_WithGroupBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, AVG(IntVal) AS A FROM " + _t + @" GROUP BY GroupKey ORDER BY GroupKey");
		rows.Should().HaveCount(3);
		((double)rows[0]["A"]!).Should().BeApproximately(20.0, 1e-10); // A: (10+20+30)/3
		((double)rows[1]["A"]!).Should().BeApproximately(45.0, 1e-10); // B: (40+50)/2
	}

	// ═══════════════════════════════════════════════════════════════
	// MIN and MAX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#min
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Min_Int64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MIN(IntVal) AS M FROM " + _t + @"");
		rows[0]["M"].Should().Be(10L);
	}

	[Fact]
	public async Task Max_Int64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MAX(IntVal) AS M FROM " + _t + @"");
		rows[0]["M"].Should().Be(50L);
	}

	[Fact]
	public async Task Min_String()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MIN(StrVal) AS M FROM " + _t + @"");
		rows[0]["M"].Should().Be("apple");
	}

	[Fact]
	public async Task Max_String()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MAX(StrVal) AS M FROM " + _t + @"");
		rows[0]["M"].Should().Be("elderberry");
	}

	[Fact]
	public async Task Min_Float64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MIN(FloatVal) AS M FROM " + _t + @"");
		((double)rows[0]["M"]!).Should().BeApproximately(1.5, 1e-10);
	}

	[Fact]
	public async Task Max_Float64()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT MAX(FloatVal) AS M FROM " + _t + @"");
		((double)rows[0]["M"]!).Should().BeApproximately(5.5, 1e-10);
	}

	[Fact]
	public async Task MinMax_WithGroupBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, MIN(IntVal) AS MN, MAX(IntVal) AS MX FROM " + _t + @" GROUP BY GroupKey ORDER BY GroupKey");
		rows[0]["MN"].Should().Be(10L);
		rows[0]["MX"].Should().Be(30L);
		rows[1]["MN"].Should().Be(40L);
		rows[1]["MX"].Should().Be(50L);
	}

	// ═══════════════════════════════════════════════════════════════
	// LOGICAL_AND, LOGICAL_OR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LogicalAnd_Mixed()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT LOGICAL_AND(BoolVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		rows[0]["V"].Should().Be(false);
	}

	[Fact]
	public async Task LogicalAnd_AllTrue()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT LOGICAL_AND(BoolVal) AS V FROM " + _t + @" WHERE GroupKey = 'A' AND BoolVal = TRUE");
		rows[0]["V"].Should().Be(true);
	}

	[Fact]
	public async Task LogicalOr_Mixed()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT LOGICAL_OR(BoolVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		rows[0]["V"].Should().Be(true);
	}

	[Fact]
	public async Task LogicalOr_AllFalse()
	{
		await SeedAggData();
		var rows = await QueryAsync("SELECT LOGICAL_OR(BoolVal) AS V FROM " + _t + @" WHERE GroupKey = 'A' AND BoolVal = FALSE");
		rows[0]["V"].Should().Be(false);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringAgg_Default()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT STRING_AGG(StrVal, ',') AS V FROM " + _t + @" WHERE GroupKey = 'B'");
		var result = (string)rows[0]["V"]!;
		result.Should().Contain("date");
		result.Should().Contain("elderberry");
	}

	[Fact]
	public async Task StringAgg_OrderBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT STRING_AGG(StrVal, '|' ORDER BY StrVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		rows[0]["V"].Should().Be("apple|banana|cherry");
	}

	[Fact]
	public async Task StringAgg_WithGroupBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, STRING_AGG(StrVal, ',' ORDER BY StrVal) AS V FROM " + _t + @" GROUP BY GroupKey ORDER BY GroupKey");
		rows.Should().HaveCount(3);
		rows[0]["V"].Should().Be("apple,banana,cherry");
		rows[1]["V"].Should().Be("date,elderberry");
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayAgg_Int64()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT ARRAY_AGG(IntVal ORDER BY IntVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		var arr = (List<long>)rows[0]["V"]!;
		arr.Should().Equal(10L, 20L, 30L);
	}

	[Fact]
	public async Task ArrayAgg_String()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT ARRAY_AGG(StrVal ORDER BY StrVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		var arr = (List<string>)rows[0]["V"]!;
		arr.Should().Equal("apple", "banana", "cherry");
	}

	// ═══════════════════════════════════════════════════════════════
	// ANY_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#any_value
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AnyValue_ReturnsNonNull()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT ANY_VALUE(IntVal) AS V FROM " + _t + @" WHERE GroupKey = 'A'");
		rows[0]["V"].Should().NotBeNull();
		new[] { 10L, 20L, 30L }.Should().Contain((long)rows[0]["V"]!);
	}

	// ═══════════════════════════════════════════════════════════════
	// HAVING clause
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#having_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Having_FilterGroups()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, SUM(IntVal) AS S FROM " + _t + @" GROUP BY GroupKey HAVING SUM(IntVal) > 70 ORDER BY GroupKey");
		rows.Should().ContainSingle();
		rows[0]["GroupKey"].Should().Be("B");
		rows[0]["S"].Should().Be(90L);
	}

	[Fact]
	public async Task Having_CountFilter()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, COUNT(*) AS C FROM " + _t + @" GROUP BY GroupKey HAVING COUNT(*) > 1 ORDER BY GroupKey");
		rows.Should().HaveCount(2);
		rows[0]["GroupKey"].Should().Be("A");
		rows[0]["C"].Should().Be(3L);
		rows[1]["GroupKey"].Should().Be("B");
		rows[1]["C"].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregate over empty set
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COUNT(*)", 0L)]
	public async Task Aggregate_EmptySet_Count(string agg, long expected)
	{
		await EnsureAggTable();
		var rows = await QueryAsync($"SELECT {agg} AS R FROM " + _t + @" WHERE 1 = 0");
		rows[0]["R"].Should().Be(expected);
	}

	[Theory]
	[InlineData("SUM(IntVal)")]
	[InlineData("AVG(IntVal)")]
	[InlineData("MIN(IntVal)")]
	[InlineData("MAX(IntVal)")]
	[InlineData("STRING_AGG(StrVal, ',')")]
	public async Task Aggregate_EmptySet_ReturnsNull(string agg)
	{
		await EnsureAggTable();
		var rows = await QueryAsync($"SELECT {agg} AS R FROM " + _t + @" WHERE 1 = 0");
		rows[0]["R"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregate + DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_Distinct()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AggDistinct (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("AggDistinct",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 30L });

		var rows = await QueryAsync("SELECT SUM(DISTINCT Val) AS S FROM AggDistinct");
		rows[0]["S"].Should().Be(60L);  // 10+20+30
	}

	[Fact]
	public async Task Avg_Distinct()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AggAvgDistinct (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("AggAvgDistinct",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 20L });

		var rows = await QueryAsync("SELECT AVG(DISTINCT Val) AS A FROM AggAvgDistinct");
		// Distinct values: 10, 20 => avg = 15
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple aggregates in single query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MultipleAggregates_SingleQuery()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT COUNT(*) AS C, SUM(IntVal) AS S, AVG(IntVal) AS A, MIN(IntVal) AS MN, MAX(IntVal) AS MX FROM " + _t + @" WHERE GroupKey = 'A'");
		rows[0]["C"].Should().Be(3L);
		rows[0]["S"].Should().Be(60L);
		((double)rows[0]["A"]!).Should().BeApproximately(20.0, 1e-10);
		rows[0]["MN"].Should().Be(10L);
		rows[0]["MX"].Should().Be(30L);
	}

	[Fact]
	public async Task MultipleAggregates_WithGroupBy()
	{
		await SeedAggData();
		var rows = await QueryAsync(@"
			SELECT GroupKey, COUNT(*) AS C, SUM(IntVal) AS S, MIN(IntVal) AS MN, MAX(IntVal) AS MX
			FROM " + _t + @"
			GROUP BY GroupKey
			ORDER BY GroupKey");
		rows.Should().HaveCount(3);

		// Group A
		rows[0]["C"].Should().Be(3L);
		rows[0]["S"].Should().Be(60L);
		rows[0]["MN"].Should().Be(10L);
		rows[0]["MX"].Should().Be(30L);

		// Group B
		rows[1]["C"].Should().Be(2L);
		rows[1]["S"].Should().Be(90L);
		rows[1]["MN"].Should().Be(40L);
		rows[1]["MX"].Should().Be(50L);
	}

	// ═══════════════════════════════════════════════════════════════
	// GROUP BY + ORDER BY + LIMIT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_OrderByAggregate_Limit()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, SUM(IntVal) AS S FROM " + _t + @" GROUP BY GroupKey ORDER BY S DESC LIMIT 2");
		rows.Should().HaveCount(2);
		rows[0]["GroupKey"].Should().Be("B");
		rows[0]["S"].Should().Be(90L);
	}

	[Fact]
	public async Task GroupBy_Having_OrderBy_Limit()
	{
		await SeedAggData();
		var rows = await QueryAsync(
			"SELECT GroupKey, COUNT(*) AS C FROM " + _t + @" GROUP BY GroupKey HAVING COUNT(*) > 0 ORDER BY C DESC LIMIT 1");
		rows.Should().ContainSingle();
		rows[0]["GroupKey"].Should().Be("A");
		rows[0]["C"].Should().Be(3L);
	}
}
