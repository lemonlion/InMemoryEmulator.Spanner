using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for window functions: ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD,
/// FIRST_VALUE, LAST_VALUE, SUM/AVG/COUNT OVER, and frame specifications.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowFunctionCoreIntegrationTests : IntegrationTestBase
{
	public WindowFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task SetupTable(string table)
	{
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Dept STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new() { ["Id"] = 1L, ["Dept"] = "Eng", ["Salary"] = 100L },
			new() { ["Id"] = 2L, ["Dept"] = "Eng", ["Salary"] = 120L },
			new() { ["Id"] = 3L, ["Dept"] = "Eng", ["Salary"] = 100L },
			new() { ["Id"] = 4L, ["Dept"] = "Sales", ["Salary"] = 90L },
			new() { ["Id"] = 5L, ["Dept"] = "Sales", ["Salary"] = 80L },
			new() { ["Id"] = 6L, ["Dept"] = "Sales", ["Salary"] = 110L });
	}

	// ─── ROW_NUMBER ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task RowNumber_GlobalOrder()
	{
		var t = "Win1";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM {t} ORDER BY Id");

		rows.Should().HaveCount(6);
		rows[0]["rn"].Should().Be(1L);
		rows[5]["rn"].Should().Be(6L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task RowNumber_Partitioned()
	{
		var t = "Win2";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Id) AS rn
			FROM {t} ORDER BY Dept, Id");

		var engRows = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		engRows[0]["rn"].Should().Be(1L);
		engRows[1]["rn"].Should().Be(2L);
		engRows[2]["rn"].Should().Be(3L);

		var salesRows = rows.Where(r => (string)r["Dept"]! == "Sales").ToList();
		salesRows[0]["rn"].Should().Be(1L);
		salesRows[1]["rn"].Should().Be(2L);
		salesRows[2]["rn"].Should().Be(3L);
	}

	// ─── RANK ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Rank_WithTies()
	{
		var t = "Win3";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Salary, RANK() OVER (ORDER BY Salary DESC) AS rnk
			FROM {t} ORDER BY rnk, Id");

		// 120, 110, 100, 100, 90, 80
		rows[0]["rnk"].Should().Be(1L); // 120
		rows[1]["rnk"].Should().Be(2L); // 110
		// Two rows with 100 should have rank 3
		rows[2]["rnk"].Should().Be(3L); // 100
		rows[3]["rnk"].Should().Be(3L); // 100
		rows[4]["rnk"].Should().Be(5L); // 90 (skips 4)
		rows[5]["rnk"].Should().Be(6L); // 80
	}

	// ─── DENSE_RANK ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#dense_rank

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task DenseRank_NoGaps()
	{
		var t = "Win4";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Salary, DENSE_RANK() OVER (ORDER BY Salary DESC) AS drnk
			FROM {t} ORDER BY drnk, Id");

		rows[0]["drnk"].Should().Be(1L); // 120
		rows[1]["drnk"].Should().Be(2L); // 110
		rows[2]["drnk"].Should().Be(3L); // 100
		rows[3]["drnk"].Should().Be(3L); // 100
		rows[4]["drnk"].Should().Be(4L); // 90 (no gap)
		rows[5]["drnk"].Should().Be(5L); // 80
	}

	// ─── SUM OVER ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Sum_Over_Partitioned()
	{
		var t = "Win5";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, SUM(Salary) OVER (PARTITION BY Dept) AS DeptTotal
			FROM {t} ORDER BY Dept, Id");

		var engRows = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		engRows[0]["DeptTotal"].Should().Be(320L);

		var salesRows = rows.Where(r => (string)r["Dept"]! == "Sales").ToList();
		salesRows[0]["DeptTotal"].Should().Be(280L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Sum_Over_RunningTotal()
	{
		var t = "Win6";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Salary, 
				SUM(Salary) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunSum
			FROM {t} ORDER BY Id");

		rows[0]["RunSum"].Should().Be(100L);
		rows[1]["RunSum"].Should().Be(220L);
		rows[2]["RunSum"].Should().Be(320L);
	}

	// ─── AVG OVER ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Avg_Over_Partitioned()
	{
		var t = "Win7";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, AVG(Salary) OVER (PARTITION BY Dept) AS AvgSal
			FROM {t} ORDER BY Dept, Id");

		var engAvg = (double)rows.First(r => (string)r["Dept"]! == "Eng")["AvgSal"]!;
		engAvg.Should().BeApproximately(106.67, 0.1);
	}

	// ─── COUNT OVER ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Count_Over_Partitioned()
	{
		var t = "Win8";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, COUNT(*) OVER (PARTITION BY Dept) AS DeptCount
			FROM {t} ORDER BY Id");

		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["DeptCount"]! == 3L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["DeptCount"]! == 3L).Should().BeTrue();
	}

	// ─── NTILE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#ntile

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Ntile_Splits()
	{
		var t = "Win9";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, NTILE(3) OVER (ORDER BY Id) AS tile FROM {t} ORDER BY Id");

		rows[0]["tile"].Should().Be(1L);
		rows[1]["tile"].Should().Be(1L);
		rows[2]["tile"].Should().Be(2L);
		rows[3]["tile"].Should().Be(2L);
		rows[4]["tile"].Should().Be(3L);
		rows[5]["tile"].Should().Be(3L);
	}

	// ─── LAG / LEAD ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#lag

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Lag_PreviousValue()
	{
		var t = "Win10";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Salary, LAG(Salary) OVER (ORDER BY Id) AS PrevSalary
			FROM {t} ORDER BY Id");

		rows[0]["PrevSalary"].Should().BeNull(); // No previous for first row
		rows[1]["PrevSalary"].Should().Be(100L);
		rows[2]["PrevSalary"].Should().Be(120L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Lead_NextValue()
	{
		var t = "Win11";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Salary, LEAD(Salary) OVER (ORDER BY Id) AS NextSalary
			FROM {t} ORDER BY Id");

		rows[0]["NextSalary"].Should().Be(120L);
		rows[1]["NextSalary"].Should().Be(100L);
		rows[5]["NextSalary"].Should().BeNull(); // No next for last row
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Lag_WithOffset()
	{
		var t = "Win12";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, LAG(Id, 2) OVER (ORDER BY Id) AS PrevPrev
			FROM {t} ORDER BY Id");

		rows[0]["PrevPrev"].Should().BeNull();
		rows[1]["PrevPrev"].Should().BeNull();
		rows[2]["PrevPrev"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task Lag_WithDefault()
	{
		var t = "Win13";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, LAG(Salary, 1, 0) OVER (ORDER BY Id) AS PrevSalary
			FROM {t} ORDER BY Id");

		rows[0]["PrevSalary"].Should().Be(0L);
		rows[1]["PrevSalary"].Should().Be(100L);
	}

	// ─── FIRST_VALUE / LAST_VALUE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#first_value

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task FirstValue_PerPartition()
	{
		var t = "Win14";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, FIRST_VALUE(Salary) OVER (PARTITION BY Dept ORDER BY Id) AS FirstSal
			FROM {t} ORDER BY Dept, Id");

		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["FirstSal"]! == 100L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["FirstSal"]! == 90L).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task LastValue_PerPartition()
	{
		var t = "Win15";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept, LAST_VALUE(Salary) OVER (PARTITION BY Dept ORDER BY Id 
				ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS LastSal
			FROM {t} ORDER BY Dept, Id");

		rows.Where(r => (string)r["Dept"]! == "Eng").All(r => (long)r["LastSal"]! == 100L).Should().BeTrue();
		rows.Where(r => (string)r["Dept"]! == "Sales").All(r => (long)r["LastSal"]! == 110L).Should().BeTrue();
	}

	// ─── MIN/MAX OVER ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task MinMax_Over_Partitioned()
	{
		var t = "Win16";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, Dept,
				MIN(Salary) OVER (PARTITION BY Dept) AS MinSal,
				MAX(Salary) OVER (PARTITION BY Dept) AS MaxSal
			FROM {t} ORDER BY Dept, Id");

		var eng = rows.First(r => (string)r["Dept"]! == "Eng");
		eng["MinSal"].Should().Be(100L);
		eng["MaxSal"].Should().Be(120L);

		var sales = rows.First(r => (string)r["Dept"]! == "Sales");
		sales["MinSal"].Should().Be(80L);
		sales["MaxSal"].Should().Be(110L);
	}

	// ─── Multiple window functions in one query ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task MultipleWindowFunctions_InOneQuery()
	{
		var t = "Win17";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, 
				ROW_NUMBER() OVER (ORDER BY Id) AS rn,
				RANK() OVER (ORDER BY Salary DESC) AS rnk,
				SUM(Salary) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS runSum
			FROM {t} ORDER BY Id");

		rows[0]["rn"].Should().Be(1L);
		rows[0]["runSum"].Should().Be(100L);
		rows.Should().HaveCount(6);
	}

	// ─── Window function with WHERE ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task WindowFunction_WithWhere()
	{
		var t = "Win18";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn
			FROM {t} 
			WHERE Dept = 'Eng'
			ORDER BY Id");

		rows.Should().HaveCount(3);
		rows[0]["rn"].Should().Be(1L);
		rows[2]["rn"].Should().Be(3L);
	}

	// ─── Window over empty result set ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task WindowFunction_EmptyResult()
	{
		var t = "Win19";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn
			FROM {t} WHERE 1 = 0");

		rows.Should().BeEmpty();
	}

	// ─── ROW_NUMBER without PARTITION ───

	[Fact]
	[Trait(TestTraits.Category, "WindowFunction")]
	public async Task RowNumber_DescendingOrder()
	{
		var t = "Win20";
		await SetupTable(t);

		var rows = await QueryAsync($@"
			SELECT Id, ROW_NUMBER() OVER (ORDER BY Id DESC) AS rn
			FROM {t} ORDER BY rn");

		rows[0]["Id"].Should().Be(6L);
		rows[0]["rn"].Should().Be(1L);
	}
}
