using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive window function tests: ROW_NUMBER, RANK, DENSE_RANK, LAG, LEAD, 
/// SUM/AVG/COUNT/MIN/MAX OVER, FIRST_VALUE, LAST_VALUE, NTH_VALUE, NTILE, frames.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowFunctionExhaustiveIntegrationTests : IntegrationTestBase
{
	public WindowFunctionExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> SeedTable()
	{
		var t = $"Win_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Dept STRING(MAX), Name STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync(
			$@"INSERT INTO {t} (Id, Dept, Name, Salary) VALUES
				(1, 'Eng',  'Alice',   100),
				(2, 'Eng',  'Bob',     120),
				(3, 'Eng',  'Charlie', 110),
				(4, 'Sales','Diana',   90),
				(5, 'Sales','Eve',     95),
				(6, 'HR',   'Frank',   85)");
		return t;
	}

	// ─── ROW_NUMBER ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task RowNumber_OverOrderBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RN FROM {t} ORDER BY RN");
		((long)rows[0]["RN"]!).Should().Be(1);
		rows[0]["Name"].Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task RowNumber_PartitionByDept()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Dept, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS RN FROM {t} ORDER BY Dept, RN");
		// Eng: Bob(1), Charlie(2), Alice(3)
		rows.Where(r => (string)r["Dept"]! == "Eng").Select(r => (long)r["RN"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
	}

	// ─── RANK ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Rank_WithTies()
	{
		var t2 = $"WinR_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 10), (3, 20), (4, 30)");
		var rows = await QueryAsync($"SELECT Val, RANK() OVER (ORDER BY Val) AS R FROM {t2} ORDER BY Val, Id");
		rows[0]["R"].Should().Be(1L); // Val=10
		rows[1]["R"].Should().Be(1L); // Val=10 (tie)
		rows[2]["R"].Should().Be(3L); // Val=20 (skips 2)
		rows[3]["R"].Should().Be(4L); // Val=30
	}

	// ─── DENSE_RANK ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task DenseRank_WithTies()
	{
		var t2 = $"WinDR_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 10), (3, 20), (4, 30)");
		var rows = await QueryAsync($"SELECT Val, DENSE_RANK() OVER (ORDER BY Val) AS R FROM {t2} ORDER BY Val, Id");
		rows[0]["R"].Should().Be(1L);
		rows[1]["R"].Should().Be(1L);
		rows[2]["R"].Should().Be(2L); // No skip
		rows[3]["R"].Should().Be(3L);
	}

	// ─── LAG / LEAD ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lag_Default()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LAG(Salary) OVER (ORDER BY Salary) AS Prev FROM {t} ORDER BY Salary");
		rows[0]["Prev"].Should().BeNull(); // first row has no lag
		rows[1]["Prev"].Should().Be(85L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lag_WithOffset()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LAG(Salary, 2) OVER (ORDER BY Salary) AS Prev2 FROM {t} ORDER BY Salary");
		rows[0]["Prev2"].Should().BeNull();
		rows[1]["Prev2"].Should().BeNull();
		rows[2]["Prev2"].Should().Be(85L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lag_WithDefault()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LAG(Salary, 1, -1) OVER (ORDER BY Salary) AS Prev FROM {t} ORDER BY Salary");
		rows[0]["Prev"].Should().Be(-1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lead_Default()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LEAD(Salary) OVER (ORDER BY Salary) AS Nxt FROM {t} ORDER BY Salary");
		rows[^1]["Nxt"].Should().BeNull();
		rows[0]["Nxt"].Should().Be(90L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lead_WithOffset()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LEAD(Salary, 2) OVER (ORDER BY Salary) AS Nxt2 FROM {t} ORDER BY Salary");
		rows[^1]["Nxt2"].Should().BeNull();
		rows[^2]["Nxt2"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Lead_WithDefault()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Salary, LEAD(Salary, 1, 0) OVER (ORDER BY Salary) AS Nxt FROM {t} ORDER BY Salary");
		rows[^1]["Nxt"].Should().Be(0L);
	}

	// ─── SUM OVER ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Sum_Over_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Dept, SUM(Salary) OVER (PARTITION BY Dept) AS DeptTotal FROM {t} ORDER BY Dept, Name");
		rows.Where(r => (string)r["Dept"]! == "Eng").Select(r => (long)r["DeptTotal"]!).Distinct().Single().Should().Be(330);
		rows.Where(r => (string)r["Dept"]! == "Sales").Select(r => (long)r["DeptTotal"]!).Distinct().Single().Should().Be(185);
		rows.Where(r => (string)r["Dept"]! == "HR").Select(r => (long)r["DeptTotal"]!).Distinct().Single().Should().Be(85);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Sum_Over_OrderBy_RunningTotal()
	{
		var t2 = $"WinRun_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var rows = await QueryAsync(
			$"SELECT Id, Val, SUM(Val) OVER (ORDER BY Id) AS Running FROM {t2} ORDER BY Id");
		rows[0]["Running"].Should().Be(10L);
		rows[1]["Running"].Should().Be(30L);
		rows[2]["Running"].Should().Be(60L);
	}

	// ─── AVG OVER ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Avg_Over_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Dept, AVG(Salary) OVER (PARTITION BY Dept) AS AvgSalary FROM {t} WHERE Dept = 'Eng'");
		rows.Should().HaveCount(3);
		((double)rows[0]["AvgSalary"]!).Should().Be(110.0);
	}

	// ─── COUNT OVER ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Count_Over_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Dept, COUNT(*) OVER (PARTITION BY Dept) AS Cnt FROM {t} ORDER BY Dept, Id");
		rows.Where(r => (string)r["Dept"]! == "Eng").Select(r => (long)r["Cnt"]!).Distinct().Single().Should().Be(3);
		rows.Where(r => (string)r["Dept"]! == "HR").Select(r => (long)r["Cnt"]!).Distinct().Single().Should().Be(1);
	}

	// ─── MIN / MAX OVER ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Min_Over_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Dept, MIN(Salary) OVER (PARTITION BY Dept) AS MinS FROM {t} WHERE Dept = 'Eng'");
		rows[0]["MinS"].Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Max_Over_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Dept, MAX(Salary) OVER (PARTITION BY Dept) AS MaxS FROM {t} WHERE Dept = 'Eng'");
		rows[0]["MaxS"].Should().Be(120L);
	}

	// ─── FIRST_VALUE / LAST_VALUE ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task FirstValue_PartitionBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Dept, FIRST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS TopEarner FROM {t} WHERE Dept = 'Eng'");
		rows.Select(r => (string)r["TopEarner"]!).Distinct().Single().Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task LastValue_WithFrame()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, Dept, LAST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS LV FROM {t} WHERE Dept = 'Eng'");
		rows.Select(r => (string)r["LV"]!).Distinct().Single().Should().Be("Bob");
	}

	// ─── NTILE ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Ntile_3Buckets()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, NTILE(3) OVER (ORDER BY Salary) AS Bucket FROM {t} ORDER BY Salary");
		// 6 rows / 3 buckets = 2 per bucket
		rows[0]["Bucket"].Should().Be(1L);
		rows[1]["Bucket"].Should().Be(1L);
		rows[2]["Bucket"].Should().Be(2L);
		rows[3]["Bucket"].Should().Be(2L);
		rows[4]["Bucket"].Should().Be(3L);
		rows[5]["Bucket"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Ntile_2Buckets()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, NTILE(2) OVER (ORDER BY Salary) AS Bucket FROM {t} ORDER BY Salary");
		rows[0]["Bucket"].Should().Be(1L);
		rows[1]["Bucket"].Should().Be(1L);
		rows[2]["Bucket"].Should().Be(1L);
		rows[3]["Bucket"].Should().Be(2L);
		rows[4]["Bucket"].Should().Be(2L);
		rows[5]["Bucket"].Should().Be(2L);
	}

	// ─── Multiple window functions in same query ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task MultipleWindowFunctions()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$@"SELECT Name, Salary,
				ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RN,
				SUM(Salary) OVER () AS Total,
				RANK() OVER (ORDER BY Salary DESC) AS Rnk
			FROM {t} ORDER BY RN");
		rows.Should().HaveCount(6);
		((long)rows[0]["RN"]!).Should().Be(1);
		((long)rows[0]["Total"]!).Should().Be(600);
	}

	// ─── Window with WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task WindowFunction_WithWhere()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary) AS RN FROM {t} WHERE Salary >= 100 ORDER BY RN");
		rows.Should().HaveCount(3);
	}

	// ─── Empty partition ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task WindowFunction_EmptyResult()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary) AS RN FROM {t} WHERE Salary > 9999");
		rows.Should().BeEmpty();
	}

	// ─── Window function with ORDER BY and ROWS frame ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Sum_RowsBetween_Preceding_Following()
	{
		var t2 = $"WinFrame_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50)");
		var rows = await QueryAsync(
			$"SELECT Id, SUM(Val) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS S FROM {t2} ORDER BY Id");
		rows[0]["S"].Should().Be(30L);  // 10+20
		rows[1]["S"].Should().Be(60L);  // 10+20+30
		rows[2]["S"].Should().Be(90L);  // 20+30+40
		rows[3]["S"].Should().Be(120L); // 30+40+50
		rows[4]["S"].Should().Be(90L);  // 40+50
	}

	// ─── SUM OVER() without ORDER BY = full partition ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Sum_OverEmpty_FullPartition()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name, SUM(Salary) OVER () AS Total FROM {t}");
		rows.Should().AllSatisfy(r => ((long)r["Total"]!).Should().Be(600));
	}

	// ─── COUNT OVER with ORDER BY = running count ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Count_Over_OrderBy_RunningCount()
	{
		var t2 = $"WinCnt_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var rows = await QueryAsync(
			$"SELECT Id, COUNT(*) OVER (ORDER BY Id) AS RC FROM {t2} ORDER BY Id");
		rows[0]["RC"].Should().Be(1L);
		rows[1]["RC"].Should().Be(2L);
		rows[2]["RC"].Should().Be(3L);
	}

	// ─── AVG OVER with ORDER BY ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Avg_Over_OrderBy_RunningAvg()
	{
		var t2 = $"WinAvg_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var rows = await QueryAsync(
			$"SELECT Id, AVG(Val) OVER (ORDER BY Id) AS RA FROM {t2} ORDER BY Id");
		((double)rows[0]["RA"]!).Should().Be(10.0);
		((double)rows[1]["RA"]!).Should().Be(15.0);
		((double)rows[2]["RA"]!).Should().Be(20.0);
	}

	// ─── ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW ───
	[Fact]
	[Trait(TestTraits.Category, "WindowExhaustive")]
	public async Task Sum_RowsBetween_UnboundedPreceding_CurrentRow()
	{
		var t2 = $"WinRUPC_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t2} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var rows = await QueryAsync(
			$"SELECT Id, SUM(Val) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM {t2} ORDER BY Id");
		rows[0]["S"].Should().Be(10L);
		rows[1]["S"].Should().Be(30L);
		rows[2]["S"].Should().Be(60L);
	}
}
