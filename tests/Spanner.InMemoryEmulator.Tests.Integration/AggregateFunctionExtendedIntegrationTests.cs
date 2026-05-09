using System.Collections;
using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended tests for aggregate functions and GROUP BY patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateFunctionExtendedIntegrationTests : IntegrationTestBase
{
	private const string T = "AggFnExtT";

	public AggregateFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTable()
	{
		await ExecuteDdlAsync($"CREATE TABLE IF NOT EXISTS {T} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, FVal FLOAT64, Category STRING(MAX), Active BOOL) PRIMARY KEY (Id)");
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 10L, ["FVal"] = 1.5, ["Category"] = "A", ["Active"] = true });
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Val"] = 20L, ["FVal"] = 2.5, ["Category"] = "A", ["Active"] = false });
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["Val"] = 30L, ["FVal"] = 3.5, ["Category"] = "B", ["Active"] = true });
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["Val"] = 40L, ["FVal"] = 4.5, ["Category"] = "B", ["Active"] = false });
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Eve", ["Val"] = 50L, ["FVal"] = 5.5, ["Category"] = "C", ["Active"] = true });
		await InsertOrUpdateAsync(T, new Dictionary<string, object?> { ["Id"] = 6L, ["Name"] = null, ["Val"] = null, ["FVal"] = null, ["Category"] = "C", ["Active"] = null });
	}

	// ════════════════════════════════════════════════════════════════
	// COUNT variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#count
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_ReturnsAllRows()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(6L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Column_ExcludesNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(Val) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Distinct_Column()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT Category) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Column_AllNulls_ReturnsZero()
	{
		await EnsureTable();
		// Only row 6 has Val=NULL, but COUNT of a column that is NULL for all matching rows
		var rows = await QueryAsync($"SELECT COUNT(Val) AS C FROM {T} WHERE Id = 6");
		((long)rows[0]["C"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {T} WHERE Category = 'A'");
		((long)rows[0]["C"]!).Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["C"]!).Should().Be(2L); // A
		((long)rows[1]["C"]!).Should().Be(2L); // B
		((long)rows[2]["C"]!).Should().Be(2L); // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_WithHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category HAVING COUNT(*) >= 2 ORDER BY Category");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_EmptyResult_ReturnsZero()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {T} WHERE Id = 999");
		((long)rows[0]["C"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Distinct_WithNulls_ExcludesNulls()
	{
		await EnsureTable();
		// Val has values 10,20,30,40,50,NULL → COUNT(DISTINCT Val) = 5
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT Val) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_NoMatchingWhere_ReturnsZero()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {T} WHERE Category = 'Z'");
		((long)rows[0]["C"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Name_ExcludesNullName()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(Name) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Distinct_Name()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT Name) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Star_GroupBy_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} WHERE Val IS NOT NULL GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["C"]!).Should().Be(2L); // A: Alice,Bob
		((long)rows[1]["C"]!).Should().Be(2L); // B: Charlie,Diana
		((long)rows[2]["C"]!).Should().Be(1L); // C: Eve only (row 6 has Val=NULL)
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Having_FiltersGroups()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(Val) AS C FROM {T} GROUP BY Category HAVING COUNT(Val) = 1 ORDER BY Category");
		rows.Should().HaveCount(1);
		((string)rows[0]["Category"]!).Should().Be("C");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Count_Active_ExcludesNullBool()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(Active) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	// ════════════════════════════════════════════════════════════════
	// SUM variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#sum
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Int_ReturnsTotal()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Float_ReturnsTotal()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(FVal) AS S FROM {T}");
		((double)rows[0]["S"]!).Should().BeApproximately(17.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Distinct_Int()
	{
		await EnsureTable();
		// All Val values are distinct (10,20,30,40,50) so DISTINCT doesn't change result
		var rows = await QueryAsync($"SELECT SUM(DISTINCT Val) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_IgnoresNulls()
	{
		await EnsureTable();
		// Row 6 has Val=NULL, SUM should ignore it
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T} WHERE Category = 'A'");
		((long)rows[0]["S"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["S"]!).Should().Be(30L);  // A: 10+20
		((long)rows[1]["S"]!).Should().Be(70L);  // B: 30+40
		((long)rows[2]["S"]!).Should().Be(50L);  // C: 50 (NULL ignored)
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_EmptyResult_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T} WHERE Id = 999");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_WithHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category HAVING SUM(Val) > 50 ORDER BY Category");
		rows.Should().HaveCount(1);
		((string)rows[0]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Float_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(FVal) AS S FROM {T} GROUP BY Category ORDER BY Category");
		((double)rows[0]["S"]!).Should().BeApproximately(4.0, 0.001);  // A: 1.5+2.5
		((double)rows[1]["S"]!).Should().BeApproximately(8.0, 0.001);  // B: 3.5+4.5
		((double)rows[2]["S"]!).Should().BeApproximately(5.5, 0.001);  // C: 5.5 (NULL ignored)
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_AllNulls_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T} WHERE Id = 6");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_WithWhereAndGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} WHERE Val > 15 GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["S"]!).Should().Be(20L);  // A: 20
		((long)rows[1]["S"]!).Should().Be(70L);  // B: 30+40
		((long)rows[2]["S"]!).Should().Be(50L);  // C: 50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Distinct_Float()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(DISTINCT FVal) AS S FROM {T}");
		((double)rows[0]["S"]!).Should().BeApproximately(17.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_SingleRow()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS S FROM {T} WHERE Id = 1");
		((long)rows[0]["S"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Sum_Having_NoMatch()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category HAVING SUM(Val) > 1000");
		rows.Should().BeEmpty();
	}

	// ════════════════════════════════════════════════════════════════
	// AVG variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#avg
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Int_ReturnsFloat()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T}");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Float_ReturnsFloat()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(FVal) AS A FROM {T}");
		((double)rows[0]["A"]!).Should().BeApproximately(3.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(DISTINCT Val) AS A FROM {T}");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_IgnoresNulls()
	{
		await EnsureTable();
		// 5 non-null values: avg = (10+20+30+40+50)/5 = 30
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T}");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T} WHERE Category = 'A'");
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 0.001);  // A: (10+20)/2
		((double)rows[1]["A"]!).Should().BeApproximately(35.0, 0.001);  // B: (30+40)/2
		((double)rows[2]["A"]!).Should().BeApproximately(50.0, 0.001);  // C: 50/1
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T} WHERE Id = 999");
		rows[0]["A"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_AllNulls_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T} WHERE Id = 6");
		rows[0]["A"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Float_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(FVal) AS A FROM {T} GROUP BY Category ORDER BY Category");
		((double)rows[0]["A"]!).Should().BeApproximately(2.0, 0.001);  // A: (1.5+2.5)/2
		((double)rows[1]["A"]!).Should().BeApproximately(4.0, 0.001);  // B: (3.5+4.5)/2
		((double)rows[2]["A"]!).Should().BeApproximately(5.5, 0.001);  // C: 5.5/1
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_WithHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {T} GROUP BY Category HAVING AVG(Val) > 30 ORDER BY Category");
		rows.Should().HaveCount(2);
		((string)rows[0]["Category"]!).Should().Be("B");
		((string)rows[1]["Category"]!).Should().Be("C");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_SingleRow()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(Val) AS A FROM {T} WHERE Id = 3");
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Distinct_Float()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT AVG(DISTINCT FVal) AS A FROM {T}");
		((double)rows[0]["A"]!).Should().BeApproximately(3.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_WithWhereAndGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {T} WHERE Val >= 20 GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((double)rows[0]["A"]!).Should().BeApproximately(20.0, 0.001);  // A: 20
		((double)rows[1]["A"]!).Should().BeApproximately(35.0, 0.001);  // B: (30+40)/2
		((double)rows[2]["A"]!).Should().BeApproximately(50.0, 0.001);  // C: 50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Avg_Having_NoMatch()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {T} GROUP BY Category HAVING AVG(Val) > 1000");
		rows.Should().BeEmpty();
	}

	// ════════════════════════════════════════════════════════════════
	// MIN / MAX variations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#min
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#max
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_Int()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Val) AS M FROM {T}");
		((long)rows[0]["M"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_Int()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Val) AS M FROM {T}");
		((long)rows[0]["M"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_Float()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(FVal) AS M FROM {T}");
		((double)rows[0]["M"]!).Should().BeApproximately(1.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_Float()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(FVal) AS M FROM {T}");
		((double)rows[0]["M"]!).Should().BeApproximately(5.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_String()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Name) AS M FROM {T}");
		((string)rows[0]["M"]!).Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_String()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Name) AS M FROM {T}");
		((string)rows[0]["M"]!).Should().Be("Eve");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_IgnoresNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Val) AS M FROM {T}");
		((long)rows[0]["M"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_IgnoresNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Val) AS M FROM {T}");
		((long)rows[0]["M"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Val) AS M FROM {T} WHERE Category = 'B'");
		((long)rows[0]["M"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Val) AS M FROM {T} WHERE Category = 'B'");
		((long)rows[0]["M"]!).Should().Be(40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, MIN(Val) AS M FROM {T} GROUP BY Category ORDER BY Category");
		((long)rows[0]["M"]!).Should().Be(10L);  // A
		((long)rows[1]["M"]!).Should().Be(30L);  // B
		((long)rows[2]["M"]!).Should().Be(50L);  // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, MAX(Val) AS M FROM {T} GROUP BY Category ORDER BY Category");
		((long)rows[0]["M"]!).Should().Be(20L);  // A
		((long)rows[1]["M"]!).Should().Be(40L);  // B
		((long)rows[2]["M"]!).Should().Be(50L);  // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Min_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MIN(Val) AS M FROM {T} WHERE Id = 999");
		rows[0]["M"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Max_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT MAX(Val) AS M FROM {T} WHERE Id = 999");
		rows[0]["M"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// STRING_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_Basic()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',') AS S FROM {T} WHERE Category = 'A'");
		var result = (string)rows[0]["S"]!;
		result.Split(',').Order().Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_WithDelimiter()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ' | ') AS S FROM {T} WHERE Id IN (1, 2)");
		var result = (string)rows[0]["S"]!;
		result.Split(" | ").Order().Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_WithOrderBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',' ORDER BY Name) AS S FROM {T} WHERE Category = 'A'");
		((string)rows[0]["S"]!).Should().Be("Alice,Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_WithOrderByDesc()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',' ORDER BY Name DESC) AS S FROM {T} WHERE Category = 'A'");
		((string)rows[0]["S"]!).Should().Be("Bob,Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_IgnoresNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',' ORDER BY Id) AS S FROM {T} WHERE Category = 'C'");
		((string)rows[0]["S"]!).Should().Be("Eve");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_EmptyResult_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',') AS S FROM {T} WHERE Id = 999");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_AllNulls_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',') AS S FROM {T} WHERE Id = 6");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, STRING_AGG(Name, ',' ORDER BY Name) AS S FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((string)rows[0]["S"]!).Should().Be("Alice,Bob");
		((string)rows[1]["S"]!).Should().Be("Charlie,Diana");
		((string)rows[2]["S"]!).Should().Be("Eve");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_SingleValue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ',') AS S FROM {T} WHERE Id = 1");
		((string)rows[0]["S"]!).Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(DISTINCT Category, ',' ORDER BY Category) AS S FROM {T}");
		((string)rows[0]["S"]!).Should().Be("A,B,C");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_WithOrderByAndGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, STRING_AGG(Name, ';' ORDER BY Name DESC) AS S FROM {T} WHERE Name IS NOT NULL GROUP BY Category ORDER BY Category");
		((string)rows[0]["S"]!).Should().Be("Bob;Alice");
		((string)rows[1]["S"]!).Should().Be("Diana;Charlie");
		((string)rows[2]["S"]!).Should().Be("Eve");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_EmptyDelimiter()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, '' ORDER BY Name) AS S FROM {T} WHERE Category = 'A'");
		((string)rows[0]["S"]!).Should().Be("AliceBob");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_Category_Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(DISTINCT Category, '-' ORDER BY Category) AS S FROM {T}");
		((string)rows[0]["S"]!).Should().Be("A-B-C");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task StringAgg_MultipleDelimiterChars()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT STRING_AGG(Name, ', ' ORDER BY Name) AS S FROM {T} WHERE Category = 'B'");
		((string)rows[0]["S"]!).Should().Be("Charlie, Diana");
	}

	// ════════════════════════════════════════════════════════════════
	// ARRAY_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_Basic()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name ORDER BY Name) AS A FROM {T} WHERE Category = 'A'");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<string>().Should().BeEquivalentTo(new[] { "Alice", "Bob" }, c => c.WithStrictOrdering());
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_WithOrderByDesc()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name ORDER BY Name DESC) AS A FROM {T} WHERE Category = 'A'");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<string>().Should().BeEquivalentTo(new[] { "Bob", "Alice" }, c => c.WithStrictOrdering());
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_IgnoreNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name IGNORE NULLS ORDER BY Id) AS A FROM {T} WHERE Category = 'C'");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<string>().Should().BeEquivalentTo(new[] { "Eve" });
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_RespectNulls()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name RESPECT NULLS ORDER BY Id) AS A FROM {T} WHERE Category = 'C'");
		var arr = (IList)rows[0]["A"]!;
		arr.Count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, ARRAY_AGG(Name IGNORE NULLS ORDER BY Name) AS A FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		var arrA = (IList)rows[0]["A"]!;
		arrA.Cast<string>().Should().BeEquivalentTo(new[] { "Alice", "Bob" }, c => c.WithStrictOrdering());
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_Int()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Val IGNORE NULLS ORDER BY Val) AS A FROM {T}");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<long>().Should().BeEquivalentTo(new[] { 10L, 20L, 30L, 40L, 50L }, c => c.WithStrictOrdering());
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_EmptyResult()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name IGNORE NULLS) AS A FROM {T} WHERE Id = 999");
		rows[0]["A"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(DISTINCT Category ORDER BY Category) AS A FROM {T}");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<string>().Should().BeEquivalentTo(new[] { "A", "B", "C" }, c => c.WithStrictOrdering());
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_SingleRow()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name) AS A FROM {T} WHERE Id = 1");
		var arr = (IList)rows[0]["A"]!;
		arr.Cast<string>().Should().BeEquivalentTo(new[] { "Alice" });
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_AllNulls_IgnoreNulls_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name IGNORE NULLS) AS A FROM {T} WHERE Id = 6");
		rows[0]["A"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// COUNTIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#countif
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_BasicCondition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Active) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(3L); // Alice, Charlie, Eve
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_FalseCondition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(NOT Active) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(2L); // Bob, Diana
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_ComparisonExpression()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Val > 25) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(3L); // 30, 40, 50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNTIF(Active) AS C FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["C"]!).Should().Be(1L); // A: Alice
		((long)rows[1]["C"]!).Should().Be(1L); // B: Charlie
		((long)rows[2]["C"]!).Should().Be(1L); // C: Eve
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_NullCondition_TreatedAsFalse()
	{
		await EnsureTable();
		// Row 6 has Active=NULL, COUNTIF should treat that as false
		var rows = await QueryAsync($"SELECT COUNTIF(Active) AS C FROM {T} WHERE Category = 'C'");
		((long)rows[0]["C"]!).Should().Be(1L); // Only Eve
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_AllFalse_ReturnsZero()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Val > 1000) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_EmptyTable_ReturnsZero()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Active) AS C FROM {T} WHERE Id = 999");
		((long)rows[0]["C"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_WithIsNotNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Val IS NOT NULL) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_WithHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNTIF(Active) AS C FROM {T} GROUP BY Category HAVING COUNTIF(Active) >= 1 ORDER BY Category");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CountIf_ValEquality()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNTIF(Val = 20) AS C FROM {T}");
		((long)rows[0]["C"]!).Should().Be(1L);
	}

	// ════════════════════════════════════════════════════════════════
	// LOGICAL_AND / LOGICAL_OR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_or
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_AllTrue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id IN (1, 3, 5)");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_SomeFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id IN (1, 2)");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_AllFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id IN (2, 4)");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_WithNull()
	{
		await EnsureTable();
		// NULL is ignored by LOGICAL_AND
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id IN (1, 6)");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id = 999");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, LOGICAL_AND(Active) AS R FROM {T} WHERE Active IS NOT NULL GROUP BY Category ORDER BY Category");
		((bool)rows[0]["R"]!).Should().BeFalse(); // A: true,false
		((bool)rows[1]["R"]!).Should().BeFalse(); // B: true,false
		((bool)rows[2]["R"]!).Should().BeTrue();  // C: true only (Eve)
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_Expression()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Val > 5) AS R FROM {T} WHERE Val IS NOT NULL");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_Expression_SomeFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Val > 15) AS R FROM {T} WHERE Val IS NOT NULL");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalAnd_SingleTrue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_AND(Active) AS R FROM {T} WHERE Id = 1");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_AllTrue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id IN (1, 3, 5)");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_SomeFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id IN (1, 2)");
		((bool)rows[0]["R"]!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_AllFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id IN (2, 4)");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_WithNull()
	{
		await EnsureTable();
		// NULL is ignored by LOGICAL_OR
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id IN (2, 6)");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id = 999");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, LOGICAL_OR(Active) AS R FROM {T} WHERE Active IS NOT NULL GROUP BY Category ORDER BY Category");
		((bool)rows[0]["R"]!).Should().BeTrue();  // A: true,false → true
		((bool)rows[1]["R"]!).Should().BeTrue();  // B: true,false → true
		((bool)rows[2]["R"]!).Should().BeTrue();  // C: true → true
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_Expression()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Val > 45) AS R FROM {T} WHERE Val IS NOT NULL");
		((bool)rows[0]["R"]!).Should().BeTrue(); // 50 > 45
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_Expression_AllFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Val > 100) AS R FROM {T} WHERE Val IS NOT NULL");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task LogicalOr_SingleFalse()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT LOGICAL_OR(Active) AS R FROM {T} WHERE Id = 2");
		((bool)rows[0]["R"]!).Should().BeFalse();
	}

	// ════════════════════════════════════════════════════════════════
	// BIT_AND / BIT_OR / BIT_XOR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_and
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_or
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_xor
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitAnd_BasicValues()
	{
		await EnsureTable();
		// 10 & 20 & 30 & 40 & 50 = 0b1010 & 0b10100 & 0b11110 & 0b101000 & 0b110010 = 0
		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {T} WHERE Val IS NOT NULL");
		((long)rows[0]["R"]!).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitAnd_SameValues()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {T} WHERE Id = 1");
		((long)rows[0]["R"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitAnd_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {T} WHERE Id = 999");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitAnd_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, BIT_AND(Val) AS R FROM {T} WHERE Val IS NOT NULL GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["R"]!).Should().Be(10L & 20L); // A: 10 & 20 = 0
		((long)rows[1]["R"]!).Should().Be(30L & 40L); // B
		((long)rows[2]["R"]!).Should().Be(50L);        // C: single value
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitAnd_IgnoresNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_AND(Val) AS R FROM {T} WHERE Category = 'C'");
		((long)rows[0]["R"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitOr_BasicValues()
	{
		await EnsureTable();
		// 10 | 20 | 30 | 40 | 50 = 62
		var rows = await QueryAsync($"SELECT BIT_OR(Val) AS R FROM {T} WHERE Val IS NOT NULL");
		((long)rows[0]["R"]!).Should().Be(10L | 20L | 30L | 40L | 50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitOr_SingleValue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_OR(Val) AS R FROM {T} WHERE Id = 3");
		((long)rows[0]["R"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitOr_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_OR(Val) AS R FROM {T} WHERE Id = 999");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitOr_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, BIT_OR(Val) AS R FROM {T} WHERE Val IS NOT NULL GROUP BY Category ORDER BY Category");
		((long)rows[0]["R"]!).Should().Be(10L | 20L); // A
		((long)rows[1]["R"]!).Should().Be(30L | 40L); // B
		((long)rows[2]["R"]!).Should().Be(50L);        // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitOr_IgnoresNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_OR(Val) AS R FROM {T} WHERE Category = 'C'");
		((long)rows[0]["R"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitXor_BasicValues()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_XOR(Val) AS R FROM {T} WHERE Val IS NOT NULL");
		((long)rows[0]["R"]!).Should().Be(10L ^ 20L ^ 30L ^ 40L ^ 50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitXor_SingleValue()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_XOR(Val) AS R FROM {T} WHERE Id = 1");
		((long)rows[0]["R"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitXor_EmptySet_ReturnsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_XOR(Val) AS R FROM {T} WHERE Id = 999");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitXor_WithGroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, BIT_XOR(Val) AS R FROM {T} WHERE Val IS NOT NULL GROUP BY Category ORDER BY Category");
		((long)rows[0]["R"]!).Should().Be(10L ^ 20L); // A
		((long)rows[1]["R"]!).Should().Be(30L ^ 40L); // B
		((long)rows[2]["R"]!).Should().Be(50L);        // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task BitXor_IgnoresNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT BIT_XOR(Val) AS R FROM {T} WHERE Category = 'C'");
		((long)rows[0]["R"]!).Should().Be(50L);
	}

	// ════════════════════════════════════════════════════════════════
	// GROUP BY patterns
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#group_by_clause
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_SingleColumn()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_MultipleColumns()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, Active, COUNT(*) AS C FROM {T} WHERE Active IS NOT NULL GROUP BY Category, Active ORDER BY Category, Active");
		rows.Count.Should().BeGreaterOrEqualTo(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithExpression()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Val > 25 AS HighVal, COUNT(*) AS C FROM {T} WHERE Val IS NOT NULL GROUP BY Val > 25 ORDER BY HighVal");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithHaving_Count()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category HAVING COUNT(*) = 2 ORDER BY Category");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithHaving_Sum()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category HAVING SUM(Val) >= 50 ORDER BY Category");
		rows.Should().HaveCount(2); // B: 70, C: 50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithOrderBy_AggColumn()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY SUM(Val)");
		rows.Should().HaveCount(3);
		// Ordered by sum: A(30), C(50), B(70)
		((string)rows[0]["Category"]!).Should().Be("A");
		((string)rows[2]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithOrderByDesc()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY SUM(Val) DESC");
		((string)rows[0]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithLimit()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category ORDER BY Category LIMIT 2");
		rows.Should().HaveCount(2);
		((string)rows[0]["Category"]!).Should().Be("A");
		((string)rows[1]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithOffset()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category ORDER BY Category LIMIT 2 OFFSET 1");
		rows.Should().HaveCount(2);
		((string)rows[0]["Category"]!).Should().Be("B");
		((string)rows[1]["Category"]!).Should().Be("C");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_NullGroup()
	{
		await EnsureTable();
		// Group by Name which has NULL for Id=6
		var rows = await QueryAsync($"SELECT Name, COUNT(*) AS C FROM {T} GROUP BY Name ORDER BY Name");
		// NULL group should appear (sorted last or first depending on implementation)
		rows.Count.Should().BeGreaterOrEqualTo(5);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_BoolColumn()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Active, COUNT(*) AS C FROM {T} GROUP BY Active ORDER BY Active");
		rows.Count.Should().BeGreaterOrEqualTo(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_MultipleAggregatesInSelect()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS Cnt, SUM(Val) AS S, MIN(Val) AS Mi, MAX(Val) AS Ma FROM {T} GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		// Category A
		((long)rows[0]["Cnt"]!).Should().Be(2L);
		((long)rows[0]["S"]!).Should().Be(30L);
		((long)rows[0]["Mi"]!).Should().Be(10L);
		((long)rows[0]["Ma"]!).Should().Be(20L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_CountAndSum()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(Val) AS C, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY Category");
		// C group: COUNT(Val)=1 (Eve), SUM=50
		((long)rows[2]["C"]!).Should().Be(1L);
		((long)rows[2]["S"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_Having_MultipleConditions()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C, SUM(Val) AS S FROM {T} GROUP BY Category HAVING COUNT(*) = 2 AND SUM(Val) > 50 ORDER BY Category");
		rows.Should().HaveCount(1);
		((string)rows[0]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_Having_Or()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category HAVING SUM(Val) < 40 OR SUM(Val) > 60 ORDER BY Category");
		rows.Should().HaveCount(2); // A: 30, B: 70
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_OrderByAlias()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY S DESC");
		((string)rows[0]["Category"]!).Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WhereAndHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A FROM {T} WHERE Val IS NOT NULL GROUP BY Category HAVING AVG(Val) >= 30 ORDER BY Category");
		rows.Should().HaveCount(2); // B: 35, C: 50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_CountStar_Vs_CountColumn()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C1, COUNT(Val) AS C2 FROM {T} GROUP BY Category ORDER BY Category");
		// C: COUNT(*)=2, COUNT(Val)=1
		((long)rows[2]["C1"]!).Should().Be(2L);
		((long)rows[2]["C2"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_WithWhere_FilterBeforeGrouping()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} WHERE Active = true GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((long)rows[0]["C"]!).Should().Be(1L); // A: Alice
		((long)rows[1]["C"]!).Should().Be(1L); // B: Charlie
		((long)rows[2]["C"]!).Should().Be(1L); // C: Eve
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_CountDistinct_PerGroup()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(DISTINCT Active) AS C FROM {T} WHERE Active IS NOT NULL GROUP BY Category ORDER BY Category");
		((long)rows[0]["C"]!).Should().Be(2L); // A: true,false
		((long)rows[1]["C"]!).Should().Be(2L); // B: true,false
		((long)rows[2]["C"]!).Should().Be(1L); // C: true only
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_MinMax_PerGroup()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, MIN(FVal) AS Mi, MAX(FVal) AS Ma FROM {T} GROUP BY Category ORDER BY Category");
		((double)rows[0]["Mi"]!).Should().BeApproximately(1.5, 0.001);
		((double)rows[0]["Ma"]!).Should().BeApproximately(2.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_AvgAndSum_PerGroup()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(Val) AS A, SUM(Val) AS S FROM {T} GROUP BY Category ORDER BY Category");
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 0.001);
		((long)rows[0]["S"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task GroupBy_Having_NoRows()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} GROUP BY Category HAVING COUNT(*) > 100");
		rows.Should().BeEmpty();
	}

	// ════════════════════════════════════════════════════════════════
	// Window function aggregates
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/analytic-function-concepts
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_OverPartition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, Val, SUM(Val) OVER (PARTITION BY Category) AS S FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		rows.Should().HaveCount(5);
		// A group: sum=30
		((long)rows[0]["S"]!).Should().Be(30L);
		((long)rows[1]["S"]!).Should().Be(30L);
		// B group: sum=70
		((long)rows[2]["S"]!).Should().Be(70L);
		((long)rows[3]["S"]!).Should().Be(70L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Avg_Over()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, AVG(Val) OVER () AS A FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		rows.Should().HaveCount(5);
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Count_Over()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, COUNT(*) OVER () AS C FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		rows.Should().HaveCount(5);
		((long)rows[0]["C"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Count_OverPartition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, COUNT(*) OVER (PARTITION BY Category) AS C FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["C"]!).Should().Be(2L); // A
		((long)rows[2]["C"]!).Should().Be(2L); // B
		((long)rows[4]["C"]!).Should().Be(1L); // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Min_OverPartition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, MIN(Val) OVER (PARTITION BY Category) AS M FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["M"]!).Should().Be(10L); // A min
		((long)rows[2]["M"]!).Should().Be(30L); // B min
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Max_OverPartition()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, MAX(Val) OVER (PARTITION BY Category) AS M FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["M"]!).Should().Be(20L); // A max
		((long)rows[2]["M"]!).Should().Be(40L); // B max
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_WithOrderBy_RunningTotal()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, SUM(Val) OVER (ORDER BY Id) AS RunSum FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["RunSum"]!).Should().Be(10L);
		((long)rows[1]["RunSum"]!).Should().Be(30L);
		((long)rows[2]["RunSum"]!).Should().Be(60L);
		((long)rows[3]["RunSum"]!).Should().Be(100L);
		((long)rows[4]["RunSum"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Avg_WithOrderBy_RunningAvg()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, AVG(Val) OVER (ORDER BY Id) AS RunAvg FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((double)rows[0]["RunAvg"]!).Should().BeApproximately(10.0, 0.001);
		((double)rows[1]["RunAvg"]!).Should().BeApproximately(15.0, 0.001);
		((double)rows[2]["RunAvg"]!).Should().BeApproximately(20.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Count_WithOrderBy_RunningCount()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, COUNT(*) OVER (ORDER BY Id) AS RunC FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["RunC"]!).Should().Be(1L);
		((long)rows[4]["RunC"]!).Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_PartitionAndOrder_RunningTotalPerGroup()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, Val, SUM(Val) OVER (PARTITION BY Category ORDER BY Id) AS RunSum FROM {T} WHERE Val IS NOT NULL ORDER BY Category, Id");
		// A group: 10, then 10+20=30
		((long)rows[0]["RunSum"]!).Should().Be(10L);
		((long)rows[1]["RunSum"]!).Should().Be(30L);
		// B group: 30, then 30+40=70
		((long)rows[2]["RunSum"]!).Should().Be(30L);
		((long)rows[3]["RunSum"]!).Should().Be(70L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_RowsBetween()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, SUM(Val) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS S FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["S"]!).Should().Be(10L);       // only current row
		((long)rows[1]["S"]!).Should().Be(30L);       // 10+20
		((long)rows[2]["S"]!).Should().Be(50L);       // 20+30
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_RowsBetweenUnbounded()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, SUM(Val) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[4]["S"]!).Should().Be(150L); // total
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Avg_RowsBetween_SlidingWindow()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, AVG(Val) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS A FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((double)rows[0]["A"]!).Should().BeApproximately(15.0, 0.001); // (10+20)/2
		((double)rows[1]["A"]!).Should().BeApproximately(20.0, 0.001); // (10+20+30)/3
		((double)rows[2]["A"]!).Should().BeApproximately(30.0, 0.001); // (20+30+40)/3
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Max_Over()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, MAX(Val) OVER () AS M FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["M"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Min_Over()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, MIN(Val) OVER () AS M FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["M"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_MultipleWindowFunctions()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Category, SUM(Val) OVER (PARTITION BY Category) AS S, COUNT(*) OVER (PARTITION BY Category) AS C FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["S"]!).Should().Be(30L);
		((long)rows[0]["C"]!).Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Sum_RangeBetween()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, SUM(Val) OVER (ORDER BY Id RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["S"]!).Should().Be(10L);
		((long)rows[4]["S"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_CountIf_Over()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, COUNTIF(Val > 25) OVER () AS C FROM {T} WHERE Val IS NOT NULL ORDER BY Id");
		((long)rows[0]["C"]!).Should().Be(3L);
	}

	// ════════════════════════════════════════════════════════════════
	// Combined patterns
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_MultipleAggregates()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS Cnt, SUM(Val) AS S, AVG(Val) AS A, MIN(Val) AS Mi, MAX(Val) AS Ma FROM {T}");
		((long)rows[0]["Cnt"]!).Should().Be(6L);
		((long)rows[0]["S"]!).Should().Be(150L);
		((double)rows[0]["A"]!).Should().BeApproximately(30.0, 0.001);
		((long)rows[0]["Mi"]!).Should().Be(10L);
		((long)rows[0]["Ma"]!).Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_CastSumAsString()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT CAST(SUM(Val) AS STRING) AS S FROM {T}");
		((string)rows[0]["S"]!).Should().Be("150");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_CastCountAsFloat()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT CAST(COUNT(*) AS FLOAT64) AS C FROM {T}");
		((double)rows[0]["C"]!).Should().BeApproximately(6.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_Aggregate_WithCaseWhen()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(CASE WHEN Active = true THEN Val ELSE 0 END) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(90L); // 10+30+50
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_Aggregate_WithCaseWhen_GroupBy()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, SUM(CASE WHEN Active = true THEN 1 ELSE 0 END) AS ActiveCount FROM {T} GROUP BY Category ORDER BY Category");
		((long)rows[0]["ActiveCount"]!).Should().Be(1L); // A
		((long)rows[1]["ActiveCount"]!).Should().Be(1L); // B
		((long)rows[2]["ActiveCount"]!).Should().Be(1L); // C
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_Aggregate_WithIf()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(IF(Active, Val, 0)) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(90L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_Aggregate_WithCoalesce()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(COALESCE(Val, 0)) AS S FROM {T}");
		((long)rows[0]["S"]!).Should().Be(150L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_Count_And_CountIf()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(*) AS Total, COUNTIF(Active) AS ActiveCount FROM {T}");
		((long)rows[0]["Total"]!).Should().Be(6L);
		((long)rows[0]["ActiveCount"]!).Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_SumFloat_And_SumInt()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT SUM(Val) AS SInt, SUM(FVal) AS SFlt FROM {T}");
		((long)rows[0]["SInt"]!).Should().Be(150L);
		((double)rows[0]["SFlt"]!).Should().BeApproximately(17.5, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_AvgGroupBy_WithCoalesce()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, AVG(COALESCE(Val, 0)) AS A FROM {T} GROUP BY Category ORDER BY Category");
		// C group: (50+0)/2 = 25
		((double)rows[2]["A"]!).Should().BeApproximately(25.0, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_CastAvgAsInt()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT CAST(AVG(Val) AS INT64) AS A FROM {T}");
		((long)rows[0]["A"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_CountDistinct_And_Count()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(Category) AS C1, COUNT(DISTINCT Category) AS C2 FROM {T}");
		((long)rows[0]["C1"]!).Should().Be(6L);
		((long)rows[0]["C2"]!).Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Combined_Aggregate_Arithmetic()
	{
		await EnsureTable();
		// RANGE is a reserved keyword in Cloud Spanner — use R instead
		var rows = await QueryAsync($"SELECT MAX(Val) - MIN(Val) AS R FROM {T}");
		((long)rows[0]["R"]!).Should().Be(40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task Combined_MultipleAggregates_WithGroupByAndHaving()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C, SUM(Val) AS S, AVG(FVal) AS A FROM {T} GROUP BY Category HAVING COUNT(*) = 2 AND SUM(Val) IS NOT NULL ORDER BY Category");
		rows.Count.Should().BeGreaterOrEqualTo(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task ArrayAgg_DefaultNullHandling_OrderBy_IncludesNulls()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
		//   "If a RESPECT NULLS clause is used, or if neither a RESPECT NULLS nor an IGNORE NULLS
		//    clause is present, NULL values are included in the result."
		await EnsureTable();
		var rows = await QueryAsync($"SELECT ARRAY_AGG(Name ORDER BY Id) AS A FROM {T} WHERE Category = 'C'");
		var arr = (IList)rows[0]["A"]!;
		arr.Count.Should().Be(2); // "Eve" and NULL (Id=6)
		arr[0].Should().Be("Eve");
		arr[1].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunctionExtended")]
	public async Task CaseWhen_WithAggregate_EmptyTable_ReturnsElseBranch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case
		//   "CASE WHEN referencing aggregates should still evaluate when the table is empty"
		await EnsureTable();
		var rows = await QueryAsync($"SELECT CASE WHEN COUNT(*) > 0 THEN 'has_data' ELSE 'empty' END AS result FROM {T} WHERE Id = 999");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().Be("empty");
	}
}
