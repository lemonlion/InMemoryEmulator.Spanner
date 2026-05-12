using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive aggregate function tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateExhaustiveIntegrationTests : IntegrationTestBase
{
	private const string Table = "AggExhT";

	public AggregateExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTable()
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE IF NOT EXISTS {Table} (Id INT64 NOT NULL, Val INT64, FVal FLOAT64, Str STRING(MAX), Category STRING(MAX)) PRIMARY KEY (Id)");
		// Seed data
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L, ["FVal"] = 1.5, ["Str"] = "apple", ["Category"] = "A" });
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L, ["FVal"] = 2.5, ["Str"] = "banana", ["Category"] = "A" });
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L, ["FVal"] = 3.5, ["Str"] = "cherry", ["Category"] = "B" });
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 40L, ["FVal"] = 4.5, ["Str"] = "date", ["Category"] = "B" });
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 50L, ["FVal"] = 5.5, ["Str"] = "elderberry", ["Category"] = "C" });
		await InsertOrUpdateAsync(Table, new Dictionary<string, object?> { ["Id"] = 6L, ["Val"] = null, ["FVal"] = null, ["Str"] = null, ["Category"] = "C" });
	}

	// ─── COUNT ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Count_Star()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {Table}");
		((long)rows[0]["C"]!).Should().BeGreaterOrEqualTo(6);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Count_Column_SkipsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(Val) AS C FROM {Table}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Count_Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT Category) AS C FROM {Table}");
		((long)rows[0]["C"]!).Should().Be(3L);
	}

	// ─── SUM ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Sum_Int64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {Table}");
		rows[0]["S"].Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Sum_Float64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(FVal) AS S FROM {Table}");
		((double)rows[0]["S"]!).Should().BeApproximately(17.5, 1e-10);
	}

	// ─── AVG ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Avg_Int64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {Table}");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Avg_Float64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(FVal) AS A FROM {Table}");
		((double)rows[0]["A"]!).Should().BeApproximately(3.5, 1e-10);
	}

	// ─── MIN / MAX ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Min_Int64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Val) AS M FROM {Table}");
		rows[0]["M"].Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Max_Int64()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Val) AS M FROM {Table}");
		rows[0]["M"].Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Min_String()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Str) AS M FROM {Table}");
		rows[0]["M"].Should().Be("apple");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Max_String()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Str) AS M FROM {Table}");
		rows[0]["M"].Should().Be("elderberry");
	}

	// ─── GROUP BY ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task GroupBy_Count()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {Table} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["Category"].Should().Be("A");
		((long)rows[0]["C"]!).Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task GroupBy_Sum()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {Table} GROUP BY Category ORDER BY Category");
		rows[0]["S"].Should().Be(30L); // A: 10+20
		rows[1]["S"].Should().Be(70L); // B: 30+40
		rows[2]["S"].Should().Be(50L); // C: 50+null=50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task GroupBy_Avg()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {Table} GROUP BY Category ORDER BY Category");
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 1e-10); // A
		((double)rows[1]["A"]!).Should().BeApproximately(35.0, 1e-10); // B
		((double)rows[2]["A"]!).Should().BeApproximately(50.0, 1e-10); // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task GroupBy_MinMax()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, MIN(Val) AS Mi, MAX(Val) AS Ma FROM {Table} GROUP BY Category ORDER BY Category");
		rows[0]["Mi"].Should().Be(10L);
		rows[0]["Ma"].Should().Be(20L);
		rows[1]["Mi"].Should().Be(30L);
		rows[1]["Ma"].Should().Be(40L);
	}

	// ─── HAVING ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Having_Count()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(Val) AS C FROM {Table} GROUP BY Category HAVING COUNT(Val) = 2");
		rows.Should().HaveCount(2); // A and B have 2 non-null Val rows each; C has 1
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Having_Sum()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {Table} GROUP BY Category HAVING SUM(Val) > 50");
		rows.Should().HaveCount(1); // Only B has sum > 50 (70)
		rows[0]["Category"].Should().Be("B");
	}

	// ─── Aggregate with empty result ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Count_Star_EmptyTable()
	{
		await ExecuteDdlAsync("CREATE TABLE IF NOT EXISTS AggEmptyT (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM AggEmptyT");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Sum_EmptyTable_ReturnsNull()
	{
		await ExecuteDdlAsync("CREATE TABLE IF NOT EXISTS AggEmptyT2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var rows = await QueryAsync("SELECT SUM(Val) AS S FROM AggEmptyT2");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Avg_EmptyTable_ReturnsNull()
	{
		await ExecuteDdlAsync("CREATE TABLE IF NOT EXISTS AggEmptyT3 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var rows = await QueryAsync("SELECT AVG(Val) AS A FROM AggEmptyT3");
		rows[0]["A"].Should().BeNull();
	}

	// ─── Aggregate scalar expressions ───
	[Theory]
	[InlineData("SELECT COUNT(*) AS C FROM UNNEST([1,2,3])", 3L)]
	[InlineData("SELECT SUM(x) AS C FROM UNNEST([1,2,3]) AS x", 6L)]
	[InlineData("SELECT AVG(x) AS C FROM UNNEST([10,20,30]) AS x", 20.0)]
	[InlineData("SELECT MIN(x) AS C FROM UNNEST([3,1,2]) AS x", 1L)]
	[InlineData("SELECT MAX(x) AS C FROM UNNEST([3,1,2]) AS x", 3L)]
	[InlineData("SELECT COUNT(DISTINCT x) AS C FROM UNNEST([1,1,2,2,3]) AS x", 3L)]
	[InlineData("SELECT SUM(DISTINCT x) AS C FROM UNNEST([1,1,2,2,3]) AS x", 6L)]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Aggregate_Unnest(string sql, object expected)
	{
		var rows = await QueryAsync(sql);
		if (expected is double d)
			((double)rows[0]["C"]!).Should().BeApproximately(d, 1e-10);
		else
			rows[0]["C"].Should().Be(expected);
	}

	// ─── STRING_AGG ───
	[Theory]
	[InlineData("SELECT STRING_AGG(x, ',') AS R FROM UNNEST(['a','b','c']) AS x", "a,b,c")]
	[InlineData("SELECT STRING_AGG(x, '-') AS R FROM UNNEST(['hello','world']) AS x", "hello-world")]
	[InlineData("SELECT STRING_AGG(x, '') AS R FROM UNNEST(['a','b','c']) AS x", "abc")]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task StringAgg(string sql, string expected)
	{
		var rows = await QueryAsync(sql);
		rows[0]["R"].Should().Be(expected);
	}

	// ─── ARRAY_AGG ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task ArrayAgg_Basic()
	{
		var rows = await QueryAsync("SELECT ARRAY_LENGTH(ARRAY_AGG(x)) AS L FROM UNNEST([1,2,3]) AS x");
		rows[0]["L"].Should().Be(3L);
	}

	// ─── Aggregate with ORDER BY + LIMIT ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task GroupBy_OrderBy_Limit()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {Table} GROUP BY Category ORDER BY C DESC LIMIT 1");
		rows.Should().HaveCount(1);
	}

	// ─── Aggregate with CASE ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Sum_With_Case()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(CASE WHEN Category = 'A' THEN Val ELSE 0 END) AS S FROM {Table}");
		rows[0]["S"].Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Count_With_Case()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Category = 'A') AS C FROM {Table}");
		((long)rows[0]["C"]!).Should().Be(2L);
	}

	// ─── Multiple aggregates in same query ───
	[Fact]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task Multiple_Aggregates()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C, SUM(Val) AS S, AVG(Val) AS A, MIN(Val) AS Mi, MAX(Val) AS Ma FROM {Table}");
		((long)rows[0]["C"]!).Should().BeGreaterOrEqualTo(6);
		rows[0]["S"].Should().Be(150L);
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 1e-10);
		rows[0]["Mi"].Should().Be(10L);
		rows[0]["Ma"].Should().Be(50L);
	}

	// ─── BIT_AND / BIT_OR / BIT_XOR aggregates ───
	[Theory]
	[InlineData("SELECT BIT_AND(x) AS R FROM UNNEST([7, 5, 3]) AS x", 1L)]
	[InlineData("SELECT BIT_OR(x) AS R FROM UNNEST([1, 2, 4]) AS x", 7L)]
	[InlineData("SELECT BIT_XOR(x) AS R FROM UNNEST([1, 2, 3]) AS x", 0L)]
	[InlineData("SELECT BIT_XOR(x) AS R FROM UNNEST([1, 3]) AS x", 2L)]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task BitAggregates(string sql, long expected)
	{
		var rows = await QueryAsync(sql);
		rows[0]["R"].Should().Be(expected);
	}

	// ─── LOGICAL_AND / LOGICAL_OR ───
	[Theory]
	[InlineData("SELECT LOGICAL_AND(x) AS R FROM UNNEST([TRUE, TRUE, TRUE]) AS x", true)]
	[InlineData("SELECT LOGICAL_AND(x) AS R FROM UNNEST([TRUE, FALSE, TRUE]) AS x", false)]
	[InlineData("SELECT LOGICAL_OR(x) AS R FROM UNNEST([FALSE, FALSE, TRUE]) AS x", true)]
	[InlineData("SELECT LOGICAL_OR(x) AS R FROM UNNEST([FALSE, FALSE, FALSE]) AS x", false)]
	[Trait(TestTraits.Category, "AggregateExhaustive")]
	public async Task LogicalAggregates(string sql, bool expected)
	{
		var rows = await QueryAsync(sql);
		rows[0]["R"].Should().Be(expected);
	}
}
