using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for aggregate functions with various data patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateExtendedIntegrationTests : IntegrationTestBase
{
	public AggregateExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureAggTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AggData (Id INT64 NOT NULL, Category STRING(100), Val INT64, Score FLOAT64, Flag BOOL, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		// Seed with diverse data
		try
		{
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "A", ["Val"] = 10L, ["Score"] = 1.5, ["Flag"] = true, ["Name"] = "alpha" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "A", ["Val"] = 20L, ["Score"] = 2.5, ["Flag"] = true, ["Name"] = "beta" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "A", ["Val"] = 30L, ["Score"] = 3.5, ["Flag"] = false, ["Name"] = "gamma" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 4L, ["Category"] = "B", ["Val"] = 40L, ["Score"] = 4.5, ["Flag"] = true, ["Name"] = "delta" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 5L, ["Category"] = "B", ["Val"] = 50L, ["Score"] = 5.5, ["Flag"] = false, ["Name"] = "epsilon" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 6L, ["Category"] = "C", ["Val"] = 10L, ["Score"] = 1.0, ["Flag"] = false, ["Name"] = "zeta" });
			await InsertAsync("AggData", new Dictionary<string, object?> { ["Id"] = 7L, ["Category"] = "C", ["Val"] = 10L, ["Score"] = 2.0, ["Flag"] = true, ["Name"] = "eta" });
		}
		catch { }
	}

	private async Task EnsureNullTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AggNulls (Id INT64 NOT NULL, Val INT64, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		try
		{
			await InsertAsync("AggNulls", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L, ["Name"] = "a" });
			await InsertAsync("AggNulls", new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null, ["Name"] = null });
			await InsertAsync("AggNulls", new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L, ["Name"] = "c" });
			await InsertAsync("AggNulls", new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = null, ["Name"] = null });
			await InsertAsync("AggNulls", new Dictionary<string, object?> { ["Id"] = 5L, ["Val"] = 50L, ["Name"] = "a" });
		}
		catch { }
	}

	private async Task EnsureEmptyTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AggEmpty (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNT / COUNT(*)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#count
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CountStar_AllRows()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(*) FROM AggData");
		result.Should().Be(7L);
	}

	[Fact]
	public async Task Count_Column_ExcludesNulls()
	{
		await EnsureNullTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(Val) FROM AggNulls");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task CountStar_EmptyTable()
	{
		await EnsureEmptyTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(*) FROM AggEmpty");
		result.Should().Be(0L);
	}

	[Fact]
	public async Task Count_Distinct()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(DISTINCT Category) FROM AggData");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task Count_Distinct_Val()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(DISTINCT Val) FROM AggData");
		result.Should().Be(5L); // 10, 20, 30, 40, 50
	}

	[Fact]
	public async Task Count_WithWhere()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(*) FROM AggData WHERE Category = 'A'");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task Count_WithWhere_Empty()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(*) FROM AggData WHERE Category = 'Z'");
		result.Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#sum
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_AllRows()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT SUM(Val) FROM AggData");
		result.Should().Be(170L);
	}

	[Fact]
	public async Task Sum_WithWhere()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT SUM(Val) FROM AggData WHERE Category = 'A'");
		result.Should().Be(60L);
	}

	[Fact]
	public async Task Sum_WithNulls_IgnoresNull()
	{
		await EnsureNullTableAsync();
		var result = await QueryScalarAsync("SELECT SUM(Val) FROM AggNulls");
		result.Should().Be(90L);
	}

	[Fact]
	public async Task Sum_EmptyTable_ReturnsNull()
	{
		await EnsureEmptyTableAsync();
		var result = await QueryScalarAsync("SELECT SUM(Val) FROM AggEmpty");
		// SUM on empty set returns NULL
		(result == null || result == DBNull.Value).Should().BeTrue();
	}

	[Fact]
	public async Task Sum_Float()
	{
		await EnsureAggTableAsync();
		var result = (double)(await QueryScalarAsync("SELECT SUM(Score) FROM AggData"))!;
		result.Should().BeApproximately(20.5, 0.01);
	}

	[Fact]
	public async Task Sum_Distinct()
	{
		await EnsureAggTableAsync();
		// Distinct vals: 10, 20, 30, 40, 50 = 150
		var result = await QueryScalarAsync("SELECT SUM(DISTINCT Val) FROM AggData");
		result.Should().Be(150L);
	}

	// ═══════════════════════════════════════════════════════════════
	// AVG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#avg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Avg_AllRows()
	{
		await EnsureAggTableAsync();
		var result = (double)(await QueryScalarAsync("SELECT AVG(Score) FROM AggData"))!;
		result.Should().BeApproximately(20.5 / 7, 0.01);
	}

	[Fact]
	public async Task Avg_WithNulls_IgnoresNull()
	{
		await EnsureNullTableAsync();
		var result = (double)(await QueryScalarAsync("SELECT AVG(Val) FROM AggNulls"))!;
		result.Should().BeApproximately(30.0, 0.01); // (10 + 30 + 50) / 3
	}

	[Fact]
	public async Task Avg_EmptyTable_ReturnsNull()
	{
		await EnsureEmptyTableAsync();
		var result = await QueryScalarAsync("SELECT AVG(Val) FROM AggEmpty");
		(result == null || result == DBNull.Value).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// MIN / MAX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#min
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Min_Int()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MIN(Val) FROM AggData");
		result.Should().Be(10L);
	}

	[Fact]
	public async Task Max_Int()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MAX(Val) FROM AggData");
		result.Should().Be(50L);
	}

	[Fact]
	public async Task Min_String()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MIN(Name) FROM AggData");
		result.Should().Be("alpha");
	}

	[Fact]
	public async Task Max_String()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MAX(Name) FROM AggData");
		result.Should().Be("zeta");
	}

	[Fact]
	public async Task Min_WithNulls_IgnoresNull()
	{
		await EnsureNullTableAsync();
		var result = await QueryScalarAsync("SELECT MIN(Val) FROM AggNulls");
		result.Should().Be(10L);
	}

	[Fact]
	public async Task Max_WithNulls_IgnoresNull()
	{
		await EnsureNullTableAsync();
		var result = await QueryScalarAsync("SELECT MAX(Val) FROM AggNulls");
		result.Should().Be(50L);
	}

	[Fact]
	public async Task Min_EmptyTable_ReturnsNull()
	{
		await EnsureEmptyTableAsync();
		var result = await QueryScalarAsync("SELECT MIN(Val) FROM AggEmpty");
		(result == null || result == DBNull.Value).Should().BeTrue();
	}

	[Fact]
	public async Task Max_EmptyTable_ReturnsNull()
	{
		await EnsureEmptyTableAsync();
		var result = await QueryScalarAsync("SELECT MAX(Val) FROM AggEmpty");
		(result == null || result == DBNull.Value).Should().BeTrue();
	}

	[Fact]
	public async Task Min_Float()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MIN(Score) FROM AggData");
		result.Should().Be(1.0);
	}

	[Fact]
	public async Task Max_Float()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT MAX(Score) FROM AggData");
		result.Should().Be(5.5);
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNTIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#countif
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CountIf_True()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNTIF(Flag) FROM AggData");
		result.Should().Be(4L);
	}

	[Fact]
	public async Task CountIf_Expression()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNTIF(Val > 20) FROM AggData");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task CountIf_AllFalse()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNTIF(Val > 100) FROM AggData");
		result.Should().Be(0L);
	}

	[Fact]
	public async Task CountIf_AllTrue()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNTIF(Val > 0) FROM AggData");
		result.Should().Be(7L);
	}

	// ═══════════════════════════════════════════════════════════════
	// ANY_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#any_value
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AnyValue_ReturnsNonNull()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT ANY_VALUE(Category) FROM AggData");
		result.Should().NotBeNull();
		new[] { "A", "B", "C" }.Should().Contain((string)result!);
	}

	[Fact]
	public async Task AnyValue_WithGroupBy()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync("SELECT Category, ANY_VALUE(Name) AS SomeName FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["SomeName"].Should().NotBeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// LOGICAL_AND / LOGICAL_OR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LogicalAnd_AllTrue_ReturnsTrue()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT LOGICAL_AND(Val > 0) FROM AggData");
		result.Should().Be(true);
	}

	[Fact]
	public async Task LogicalAnd_SomeFalse_ReturnsFalse()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT LOGICAL_AND(Val > 20) FROM AggData");
		result.Should().Be(false);
	}

	[Fact]
	public async Task LogicalOr_SomeTrue_ReturnsTrue()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT LOGICAL_OR(Val > 40) FROM AggData");
		result.Should().Be(true);
	}

	[Fact]
	public async Task LogicalOr_AllFalse_ReturnsFalse()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT LOGICAL_OR(Val > 100) FROM AggData");
		result.Should().Be(false);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringAgg_Default()
	{
		await EnsureAggTableAsync();
		var result = (string)(await QueryScalarAsync("SELECT STRING_AGG(Category) FROM AggData"))!;
		result.Should().NotBeNullOrEmpty();
		// Contains all categories
		result.Should().Contain("A");
		result.Should().Contain("B");
		result.Should().Contain("C");
	}

	[Fact]
	public async Task StringAgg_WithDelimiter()
	{
		await EnsureAggTableAsync();
		var result = (string)(await QueryScalarAsync(
			"SELECT STRING_AGG(DISTINCT Category, ',') FROM AggData"))!;
		var parts = result.Split(',').OrderBy(x => x).ToArray();
		parts.Should().BeEquivalentTo(new[] { "A", "B", "C" });
	}

	[Fact]
	public async Task StringAgg_WithNulls_IgnoresNull()
	{
		await EnsureNullTableAsync();
		var result = (string)(await QueryScalarAsync(
			"SELECT STRING_AGG(Name, ',') FROM AggNulls"))!;
		result.Should().NotBeNull();
		// Only non-null names: 'a', 'c', 'a'
		result.Split(',').Should().HaveCount(3);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_AGG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayAgg_Distinct_Length()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync(
			"SELECT ARRAY_LENGTH(ARRAY_AGG(DISTINCT Category)) FROM AggData");
		result.Should().Be(3L);
	}

	[Fact]
	public async Task ArrayAgg_AllRows_Length()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync(
			"SELECT ARRAY_LENGTH(ARRAY_AGG(Name)) FROM AggData");
		result.Should().Be(7L);
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT_AND / BIT_OR / BIT_XOR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#bit_and
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task BitAnd_AllSame()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync(
			"SELECT BIT_AND(Val) FROM AggData WHERE Category = 'C'");
		// 10 & 10 = 10
		result.Should().Be(10L);
	}

	[Fact]
	public async Task BitOr_AllSame()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync(
			"SELECT BIT_OR(Val) FROM AggData WHERE Category = 'C'");
		// 10 | 10 = 10
		result.Should().Be(10L);
	}

	[Fact]
	public async Task BitXor_AllSame()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync(
			"SELECT BIT_XOR(Val) FROM AggData WHERE Category = 'C'");
		// 10 ^ 10 = 0
		result.Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// GROUP BY
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_Count()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["Category"].Should().Be("A");
		rows[0]["Cnt"].Should().Be(3L);
		rows[1]["Category"].Should().Be("B");
		rows[1]["Cnt"].Should().Be(2L);
		rows[2]["Category"].Should().Be("C");
		rows[2]["Cnt"].Should().Be(2L);
	}

	[Fact]
	public async Task GroupBy_Sum()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, SUM(Val) AS Total FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["Total"].Should().Be(60L);
		rows[1]["Total"].Should().Be(90L);
		rows[2]["Total"].Should().Be(20L);
	}

	[Fact]
	public async Task GroupBy_Avg()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, AVG(Val) AS Avg FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		((double)rows[0]["Avg"]!).Should().BeApproximately(20.0, 0.01);
		((double)rows[1]["Avg"]!).Should().BeApproximately(45.0, 0.01);
		((double)rows[2]["Avg"]!).Should().BeApproximately(10.0, 0.01);
	}

	[Fact]
	public async Task GroupBy_Min_Max()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, MIN(Val) AS MinV, MAX(Val) AS MaxV FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["MinV"].Should().Be(10L);
		rows[0]["MaxV"].Should().Be(30L);
		rows[1]["MinV"].Should().Be(40L);
		rows[1]["MaxV"].Should().Be(50L);
		rows[2]["MinV"].Should().Be(10L);
		rows[2]["MaxV"].Should().Be(10L);
	}

	[Fact]
	public async Task GroupBy_MultipleAggregates()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt, SUM(Val) AS Total, MIN(Val) AS MinV, MAX(Val) AS MaxV " +
			"FROM AggData GROUP BY Category ORDER BY Category");
		rows.Should().HaveCount(3);
		rows[0]["Cnt"].Should().Be(3L);
		rows[0]["Total"].Should().Be(60L);
	}

	// ═══════════════════════════════════════════════════════════════
	// HAVING
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Having_FiltersGroups()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData GROUP BY Category HAVING COUNT(*) > 2");
		rows.Should().ContainSingle();
		rows[0]["Category"].Should().Be("A");
	}

	[Fact]
	public async Task Having_Sum()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, SUM(Val) AS Total FROM AggData GROUP BY Category HAVING SUM(Val) >= 60 ORDER BY Category");
		rows.Should().HaveCount(2);
		rows[0]["Category"].Should().Be("A");
		rows[1]["Category"].Should().Be("B");
	}

	[Fact]
	public async Task Having_WithWhere()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData WHERE Flag = true GROUP BY Category HAVING COUNT(*) > 1 ORDER BY Category");
		rows.Should().ContainSingle();
		rows[0]["Category"].Should().Be("A");
		rows[0]["Cnt"].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SELECT with aggregate + non-aggregate (not in GROUP BY) edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Aggregate_MultiColumn_OnWholeTable()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT COUNT(*) AS Cnt, SUM(Val) AS Total, MIN(Val) AS MinV, MAX(Val) AS MaxV FROM AggData");
		rows.Should().ContainSingle();
		rows[0]["Cnt"].Should().Be(7L);
		rows[0]["Total"].Should().Be(170L);
		rows[0]["MinV"].Should().Be(10L);
		rows[0]["MaxV"].Should().Be(50L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregates with expressions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_Expression()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT SUM(Val * 2) FROM AggData");
		result.Should().Be(340L);
	}

	[Fact]
	public async Task Count_Expression()
	{
		await EnsureAggTableAsync();
		var result = await QueryScalarAsync("SELECT COUNT(DISTINCT Val * 2) FROM AggData");
		result.Should().Be(5L);
	}

	[Fact]
	public async Task Avg_Expression()
	{
		await EnsureAggTableAsync();
		var result = (double)(await QueryScalarAsync("SELECT AVG(Score * 2) FROM AggData"))!;
		result.Should().BeApproximately(41.0 / 7.0, 0.01);
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregates with ORDER BY
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_OrderBy_Count()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData GROUP BY Category ORDER BY Cnt DESC");
		rows.Should().HaveCount(3);
		rows[0]["Category"].Should().Be("A");
		rows[0]["Cnt"].Should().Be(3L);
	}

	[Fact]
	public async Task GroupBy_OrderBy_Sum()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, SUM(Val) AS Total FROM AggData GROUP BY Category ORDER BY Total DESC");
		rows.Should().HaveCount(3);
		rows[0]["Category"].Should().Be("B");
		rows[0]["Total"].Should().Be(90L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregates with LIMIT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_Limit()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData GROUP BY Category ORDER BY Cnt DESC LIMIT 2");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public async Task GroupBy_Limit_Offset()
	{
		await EnsureAggTableAsync();
		var rows = await QueryAsync(
			"SELECT Category, COUNT(*) AS Cnt FROM AggData GROUP BY Category ORDER BY Category LIMIT 2 OFFSET 1");
		rows.Should().HaveCount(2);
		rows[0]["Category"].Should().Be("B");
		rows[1]["Category"].Should().Be("C");
	}
}
