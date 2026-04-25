using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for window functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
/// </summary>
/// <remarks>
/// Go emulator limitation: "Analytic functions not supported" — all tests in this class fail.
/// </remarks>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class WindowFunctionExtendedIntegrationTests : IntegrationTestBase
{
	public WindowFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureWinTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE WinData (Id INT64 NOT NULL, Dept STRING(50), Salary INT64, Score FLOAT64) PRIMARY KEY (Id)");
		}
		catch { }

		try
		{
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 1L, ["Dept"] = "Eng", ["Salary"] = 100L, ["Score"] = 4.5 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 2L, ["Dept"] = "Eng", ["Salary"] = 120L, ["Score"] = 4.8 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 3L, ["Dept"] = "Eng", ["Salary"] = 100L, ["Score"] = 4.2 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 4L, ["Dept"] = "Sales", ["Salary"] = 80L, ["Score"] = 3.5 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 5L, ["Dept"] = "Sales", ["Salary"] = 90L, ["Score"] = 4.0 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 6L, ["Dept"] = "Sales", ["Salary"] = 90L, ["Score"] = 3.8 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 7L, ["Dept"] = "HR", ["Salary"] = 70L, ["Score"] = 3.0 });
			await InsertAsync("WinData", new Dictionary<string, object?> { ["Id"] = 8L, ["Dept"] = "HR", ["Salary"] = 75L, ["Score"] = 3.2 });
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// ROW_NUMBER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task RowNumber_GlobalOrder()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS Rn FROM WinData ORDER BY Id");
		rows.Should().HaveCount(8);
		rows[0]["Rn"].Should().Be(1L);
		rows[7]["Rn"].Should().Be(8L);
	}

	[Fact]
	public async Task RowNumber_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Id) AS Rn FROM WinData ORDER BY Dept, Id");
		// Eng: 1,2,3; HR: 1,2; Sales: 1,2,3
		rows.Where(r => (string)r["Dept"]! == "Eng").Select(r => (long)r["Rn"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
		rows.Where(r => (string)r["Dept"]! == "HR").Select(r => (long)r["Rn"]!).Should().BeEquivalentTo(new[] { 1L, 2L });
		rows.Where(r => (string)r["Dept"]! == "Sales").Select(r => (long)r["Rn"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
	}

	[Fact]
	public async Task RowNumber_OrderBySalaryDesc()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, ROW_NUMBER() OVER (ORDER BY Salary DESC, Id) AS Rn FROM WinData ORDER BY Rn");
		rows[0]["Rn"].Should().Be(1L);
		((long)rows[0]["Salary"]!).Should().BeGreaterThanOrEqualTo((long)rows[1]["Salary"]!);
	}

	// ═══════════════════════════════════════════════════════════════
	// RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Rank_WithTies()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, RANK() OVER (ORDER BY Salary) AS Rnk FROM WinData ORDER BY Salary, Id");
		// Salary 70 → Rank 1, 75 → 2, 80 → 3, 90 → 4 (x2), 100 → 6 (x2), 120 → 8
		rows.Where(r => (long)r["Salary"]! == 70).All(r => (long)r["Rnk"]! == 1).Should().BeTrue();
		rows.Where(r => (long)r["Salary"]! == 90).All(r => (long)r["Rnk"]! == 4).Should().BeTrue();
		rows.Where(r => (long)r["Salary"]! == 100).All(r => (long)r["Rnk"]! == 6).Should().BeTrue();
	}

	[Fact]
	public async Task Rank_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, RANK() OVER (PARTITION BY Dept ORDER BY Salary) AS Rnk FROM WinData ORDER BY Dept, Salary, Id");
		// Within Eng: 100 → 1 (x2), 120 → 3
		var eng = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		eng.Where(r => (long)r["Salary"]! == 100).All(r => (long)r["Rnk"]! == 1).Should().BeTrue();
		eng.Where(r => (long)r["Salary"]! == 120).All(r => (long)r["Rnk"]! == 3).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// DENSE_RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#dense_rank
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DenseRank_WithTies()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, DENSE_RANK() OVER (ORDER BY Salary) AS DRnk FROM WinData ORDER BY Salary, Id");
		// 70 → 1, 75 → 2, 80 → 3, 90 → 4, 100 → 5, 120 → 6
		rows.Where(r => (long)r["Salary"]! == 70).All(r => (long)r["DRnk"]! == 1).Should().BeTrue();
		rows.Where(r => (long)r["Salary"]! == 90).All(r => (long)r["DRnk"]! == 4).Should().BeTrue();
		rows.Where(r => (long)r["Salary"]! == 100).All(r => (long)r["DRnk"]! == 5).Should().BeTrue();
		rows.Where(r => (long)r["Salary"]! == 120).All(r => (long)r["DRnk"]! == 6).Should().BeTrue();
	}

	[Fact]
	public async Task DenseRank_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, DENSE_RANK() OVER (PARTITION BY Dept ORDER BY Salary) AS DRnk FROM WinData ORDER BY Dept, Salary, Id");
		var eng = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		eng.Where(r => (long)r["Salary"]! == 100).All(r => (long)r["DRnk"]! == 1).Should().BeTrue();
		eng.Where(r => (long)r["Salary"]! == 120).All(r => (long)r["DRnk"]! == 2).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sum_Over_Partition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, SUM(Salary) OVER (PARTITION BY Dept) AS DeptTotal FROM WinData ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["DeptTotal"]! == 320L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["DeptTotal"]! == 260L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "HR").All(r => (long)r["DeptTotal"]! == 145L).Should().BeTrue();
	}

	[Fact]
	public async Task Sum_Over_Global()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, SUM(Salary) OVER () AS GrandTotal FROM WinData ORDER BY Id");
		rows.Should().HaveCount(8);
		rows.All(r => (long)r["GrandTotal"]! == 725L).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNT OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Count_Over_Partition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, COUNT(*) OVER (PARTITION BY Dept) AS DeptCnt FROM WinData ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["DeptCnt"]! == 3L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["DeptCnt"]! == 3L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "HR").All(r => (long)r["DeptCnt"]! == 2L).Should().BeTrue();
	}

	[Fact]
	public async Task Count_Over_Global()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, COUNT(*) OVER () AS Total FROM WinData ORDER BY Id");
		rows.All(r => (long)r["Total"]! == 8L).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// AVG OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Avg_Over_Partition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, AVG(Salary) OVER (PARTITION BY Dept) AS DeptAvg FROM WinData ORDER BY Dept, Id");
		var engAvg = (double)rows.First(r => (string)r["Dept"]! == "Eng")["DeptAvg"]!;
		engAvg.Should().BeApproximately(320.0 / 3, 0.01);
	}

	// ═══════════════════════════════════════════════════════════════
	// MIN/MAX OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Min_Over_Partition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, MIN(Salary) OVER (PARTITION BY Dept) AS DeptMin FROM WinData ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["DeptMin"]! == 100L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["DeptMin"]! == 80L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "HR").All(r => (long)r["DeptMin"]! == 70L).Should().BeTrue();
	}

	[Fact]
	public async Task Max_Over_Partition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, MAX(Salary) OVER (PARTITION BY Dept) AS DeptMax FROM WinData ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["DeptMax"]! == 120L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["DeptMax"]! == 90L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "HR").All(r => (long)r["DeptMax"]! == 75L).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// LAG / LEAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#lag
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Lag_FirstRowIsNull()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, LAG(Salary) OVER (ORDER BY Id) AS PrevSalary FROM WinData ORDER BY Id");
		rows[0]["PrevSalary"].Should().BeNull();
		rows[1]["PrevSalary"].Should().Be(100L);
	}

	[Fact]
	public async Task Lag_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, LAG(Salary) OVER (PARTITION BY Dept ORDER BY Id) AS PrevSalary FROM WinData ORDER BY Dept, Id");
		// First row in each partition should be NULL
		var eng = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		eng[0]["PrevSalary"].Should().BeNull();
		eng[1]["PrevSalary"].Should().Be(100L);
		eng[2]["PrevSalary"].Should().Be(120L);
	}

	[Fact]
	public async Task Lead_LastRowIsNull()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, LEAD(Salary) OVER (ORDER BY Id) AS NextSalary FROM WinData ORDER BY Id");
		rows[7]["NextSalary"].Should().BeNull();
		rows[0]["NextSalary"].Should().Be(120L);
	}

	[Fact]
	public async Task Lead_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, LEAD(Salary) OVER (PARTITION BY Dept ORDER BY Id) AS NextSalary FROM WinData ORDER BY Dept, Id");
		var eng = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		eng[2]["NextSalary"].Should().BeNull(); // Last in partition
		eng[0]["NextSalary"].Should().Be(120L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FIRST_VALUE / LAST_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#first_value
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task FirstValue_PartitionByDept()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, FIRST_VALUE(Salary) OVER (PARTITION BY Dept ORDER BY Id) AS FirstSalary FROM WinData ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["FirstSalary"]! == 100L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["FirstSalary"]! == 80L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "HR").All(r => (long)r["FirstSalary"]! == 70L).Should().BeTrue();
	}

	[Fact]
	public async Task LastValue_Global()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, LAST_VALUE(Salary) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS LastSalary FROM WinData ORDER BY Id");
		rows.All(r => (long)r["LastSalary"]! == 75L).Should().BeTrue(); // Id=8 has salary 75
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple window functions in same query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MultipleWindowFunctions_InSameQuery()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, " +
			"ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC, Id) AS Rn, " +
			"SUM(Salary) OVER (PARTITION BY Dept) AS DeptTotal, " +
			"COUNT(*) OVER (PARTITION BY Dept) AS DeptCnt " +
			"FROM WinData ORDER BY Dept, Rn");
		rows.Should().HaveCount(8);
		// Verify first row in Eng partition (highest salary)
		var engFirst = rows.First(r => (string)r["Dept"]! == "Eng" && (long)r["Rn"]! == 1);
		engFirst["Salary"].Should().Be(120L);
		engFirst["DeptTotal"].Should().Be(320L);
		engFirst["DeptCnt"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Window function with WHERE clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task WindowFunction_WithWhere()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, ROW_NUMBER() OVER (ORDER BY Id) AS Rn " +
			"FROM WinData WHERE Dept = 'Eng' ORDER BY Id");
		rows.Should().HaveCount(3);
		rows[0]["Rn"].Should().Be(1L);
		rows[1]["Rn"].Should().Be(2L);
		rows[2]["Rn"].Should().Be(3L);
	}

	[Fact]
	public async Task WindowFunction_WithWhereAndPartition()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Dept, Salary, " +
			"RANK() OVER (ORDER BY Salary DESC) AS GlobalRank " +
			"FROM WinData WHERE Dept IN ('Eng', 'Sales') ORDER BY GlobalRank");
		rows.Should().HaveCount(6);
		rows[0]["GlobalRank"].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Window function with ORDER BY in both OVER and outer query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task WindowFunction_DifferentOrderInOverAndQuery()
	{
		await EnsureWinTableAsync();
		var rows = await QueryAsync(
			"SELECT Id, Salary, ROW_NUMBER() OVER (ORDER BY Salary DESC, Id) AS Rn FROM WinData ORDER BY Id");
		rows.Should().HaveCount(8);
		// Verify ROW_NUMBER is based on Salary DESC ordering
		var rn1Row = rows.Single(r => (long)r["Rn"]! == 1);
		rn1Row["Salary"].Should().Be(120L);
	}
}
