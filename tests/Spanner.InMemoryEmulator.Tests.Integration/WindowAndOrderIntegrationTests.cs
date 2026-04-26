using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense window function, ORDER BY, LIMIT/OFFSET, and DISTINCT tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowAndOrderIntegrationTests : IntegrationTestBase
{
	public WindowAndOrderIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureDataTable()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE WinOrd (
				Id INT64 NOT NULL,
				Dept STRING(10),
				Name STRING(50),
				Salary INT64,
				HireDate DATE
			) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task SeedData()
	{
		await EnsureDataTable();
		using var conn = Fixture.CreateConnection();
		using var delCmd = conn.CreateDmlCommand("DELETE FROM WinOrd WHERE TRUE");
		await delCmd.ExecuteNonQueryAsync();
		await InsertAsync("WinOrd",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Dept"] = "Eng", ["Name"] = "Alice", ["Salary"] = 100000L, ["HireDate"] = new DateTime(2020, 1, 15) },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Dept"] = "Eng", ["Name"] = "Bob", ["Salary"] = 90000L, ["HireDate"] = new DateTime(2019, 6, 1) },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Dept"] = "Eng", ["Name"] = "Charlie", ["Salary"] = 110000L, ["HireDate"] = new DateTime(2021, 3, 10) },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Dept"] = "Sales", ["Name"] = "Diana", ["Salary"] = 80000L, ["HireDate"] = new DateTime(2020, 7, 20) },
			new Dictionary<string, object?> { ["Id"] = 5L, ["Dept"] = "Sales", ["Name"] = "Eve", ["Salary"] = 85000L, ["HireDate"] = new DateTime(2018, 11, 5) },
			new Dictionary<string, object?> { ["Id"] = 6L, ["Dept"] = "Sales", ["Name"] = "Frank", ["Salary"] = 95000L, ["HireDate"] = new DateTime(2022, 2, 28) },
			new Dictionary<string, object?> { ["Id"] = 7L, ["Dept"] = "HR", ["Name"] = "Grace", ["Salary"] = 75000L, ["HireDate"] = new DateTime(2021, 8, 15) },
			new Dictionary<string, object?> { ["Id"] = 8L, ["Dept"] = "HR", ["Name"] = "Hank", ["Salary"] = 70000L, ["HireDate"] = new DateTime(2020, 4, 1) });
	}

	// ═══════════════════════════════════════════════════════════════
	// ROW_NUMBER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RowNumber_OverOrderBy()
	{
		await SeedData();
		var rows = await QueryAsync(
			"SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RN FROM WinOrd ORDER BY RN");
		rows.Should().HaveCount(8);
		rows[0]["Name"].Should().Be("Charlie");
		((long)rows[0]["RN"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RowNumber_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS RN
			FROM WinOrd ORDER BY Dept, RN");
		// Eng: Charlie(1), Alice(2), Bob(3)
		rows[0]["Name"].Should().Be("Charlie");
		((long)rows[0]["RN"]!).Should().Be(1L);
		rows[1]["Name"].Should().Be("Alice");
		((long)rows[1]["RN"]!).Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// RANK and DENSE_RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Rank_OverOrderBy()
	{
		await SeedData();
		var rows = await QueryAsync(
			"SELECT Name, RANK() OVER (ORDER BY Salary DESC) AS R FROM WinOrd ORDER BY R, Name");
		rows[0]["Name"].Should().Be("Charlie");
		((long)rows[0]["R"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DenseRank_OverOrderBy()
	{
		await SeedData();
		var rows = await QueryAsync(
			"SELECT Name, DENSE_RANK() OVER (ORDER BY Salary DESC) AS DR FROM WinOrd ORDER BY DR, Name");
		rows[0]["Name"].Should().Be("Charlie");
		((long)rows[0]["DR"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Rank_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name, RANK() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS R
			FROM WinOrd WHERE Dept = 'Eng' ORDER BY R");
		rows[0]["Name"].Should().Be("Charlie");
		rows[1]["Name"].Should().Be("Alice");
		rows[2]["Name"].Should().Be("Bob");
	}

	// ═══════════════════════════════════════════════════════════════
	// SUM OVER (window aggregate)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SumOver_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name, Salary, SUM(Salary) OVER (PARTITION BY Dept) AS DeptTotal
			FROM WinOrd WHERE Dept = 'Eng' ORDER BY Name");
		rows.Should().AllSatisfy(r => r["DeptTotal"].Should().Be(300000L));
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SumOver_RunningTotal()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				SUM(Salary) OVER (ORDER BY Salary ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunSum
			FROM WinOrd ORDER BY Salary");
		// Running sum: 70000, 145000, 225000, ...
		rows[0]["RunSum"].Should().Be(70000L);
		rows[1]["RunSum"].Should().Be(145000L);
	}

	// ═══════════════════════════════════════════════════════════════
	// COUNT OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CountOver_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, COUNT(*) OVER (PARTITION BY Dept) AS DeptCount
			FROM WinOrd WHERE Dept = 'Eng'");
		rows.Should().AllSatisfy(r => r["DeptCount"].Should().Be(3L));
	}

	// ═══════════════════════════════════════════════════════════════
	// AVG OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AvgOver_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, AVG(Salary) OVER (PARTITION BY Dept) AS AvgSal
			FROM WinOrd WHERE Dept = 'Eng'");
		rows.Should().AllSatisfy(r => ((double)r["AvgSal"]!).Should().BeApproximately(100000.0, 1e-10));
	}

	// ═══════════════════════════════════════════════════════════════
	// MIN/MAX OVER
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MinMaxOver_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, MIN(Salary) OVER (PARTITION BY Dept) AS MinSal, MAX(Salary) OVER (PARTITION BY Dept) AS MaxSal
			FROM WinOrd WHERE Dept = 'Eng'");
		rows.Should().AllSatisfy(r =>
		{
			r["MinSal"].Should().Be(90000L);
			r["MaxSal"].Should().Be(110000L);
		});
	}

	// ═══════════════════════════════════════════════════════════════
	// LAG and LEAD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#lag
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Lag_Default()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, LAG(Salary) OVER (ORDER BY Salary) AS PrevSal
			FROM WinOrd ORDER BY Salary");
		rows[0]["PrevSal"].Should().BeNull(); // First row has no previous
		rows[1]["PrevSal"].Should().Be(70000L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Lead_Default()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, LEAD(Salary) OVER (ORDER BY Salary) AS NextSal
			FROM WinOrd ORDER BY Salary");
		rows[^1]["NextSal"].Should().BeNull(); // Last row has no next
		rows[0]["NextSal"].Should().Be(75000L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Lag_WithOffset()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, LAG(Salary, 2) OVER (ORDER BY Salary) AS PrevPrevSal
			FROM WinOrd ORDER BY Salary");
		rows[0]["PrevPrevSal"].Should().BeNull();
		rows[1]["PrevPrevSal"].Should().BeNull();
		rows[2]["PrevPrevSal"].Should().Be(70000L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Lag_WithDefault()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, LAG(Salary, 1, 0) OVER (ORDER BY Salary) AS PrevSal
			FROM WinOrd ORDER BY Salary");
		rows[0]["PrevSal"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FIRST_VALUE and LAST_VALUE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#first_value
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FirstValue_PartitionedByDept()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name, FIRST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS TopEarner
			FROM WinOrd WHERE Dept = 'Eng'");
		rows.Should().AllSatisfy(r => r["TopEarner"].Should().Be("Charlie"));
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task LastValue_UnboundedFrame()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name,
				LAST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC
					ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS LowestEarner
			FROM WinOrd WHERE Dept = 'Eng'");
		rows.Should().AllSatisfy(r => r["LowestEarner"].Should().Be("Bob"));
	}

	// ═══════════════════════════════════════════════════════════════
	// ORDER BY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task OrderBy_Ascending()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Salary ASC");
		rows[0]["Name"].Should().Be("Hank");
		rows[^1]["Name"].Should().Be("Charlie");
	}

	[Fact]
	public async Task OrderBy_Descending()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Salary DESC");
		rows[0]["Name"].Should().Be("Charlie");
		rows[^1]["Name"].Should().Be("Hank");
	}

	[Fact]
	public async Task OrderBy_MultipleColumns()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Dept ASC, Salary DESC");
		rows[0]["Name"].Should().Be("Charlie"); // Eng, highest
		rows[1]["Name"].Should().Be("Alice");   // Eng, middle
		rows[2]["Name"].Should().Be("Bob");     // Eng, lowest
	}

	[Fact]
	public async Task OrderBy_NullsFirst()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinOrdNull (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinOrdNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });

		var rows = await QueryAsync("SELECT Id, Val FROM WinOrdNull ORDER BY Val ASC");
		// In Spanner, NULLs sort first by default in ASC
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task OrderBy_NullsLast_Desc()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinOrdNull2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinOrdNull2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });

		var rows = await QueryAsync("SELECT Id, Val FROM WinOrdNull2 ORDER BY Val DESC");
		// NULLs sort last in DESC
		rows[^1]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task OrderBy_Expression()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name, Salary FROM WinOrd ORDER BY Salary * -1");
		rows[0]["Name"].Should().Be("Charlie"); // Highest salary * -1 = most negative
	}

	[Fact]
	public async Task OrderBy_Alias()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name, Salary AS S FROM WinOrd ORDER BY S DESC");
		rows[0]["Name"].Should().Be("Charlie");
	}

	// ═══════════════════════════════════════════════════════════════
	// LIMIT and OFFSET
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#limit_and_offset_clause
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(5)]
	[InlineData(8)]
	[InlineData(100)]
	public async Task Limit_VariousSizes(int limit)
	{
		await SeedData();
		var rows = await QueryAsync($"SELECT Name FROM WinOrd ORDER BY Id LIMIT {limit}");
		rows.Should().HaveCount(Math.Min(limit, 8));
	}

	[Theory]
	[InlineData(0, 3)]
	[InlineData(1, 3)]
	[InlineData(5, 3)]
	[InlineData(7, 3)]
	[InlineData(8, 3)]
	public async Task Offset_VariousPositions(int offset, int limit)
	{
		await SeedData();
		var rows = await QueryAsync($"SELECT Id FROM WinOrd ORDER BY Id LIMIT {limit} OFFSET {offset}");
		rows.Should().HaveCount(Math.Min(limit, Math.Max(0, 8 - offset)));
		if (rows.Count > 0)
			((long)rows[0]["Id"]!).Should().Be(offset + 1);
	}

	[Fact]
	public async Task Limit_Zero()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd LIMIT 0");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// DISTINCT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_distinct
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Distinct_SingleColumn()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT DISTINCT Dept FROM WinOrd ORDER BY Dept");
		rows.Should().HaveCount(3);
		rows[0]["Dept"].Should().Be("Eng");
		rows[1]["Dept"].Should().Be("HR");
		rows[2]["Dept"].Should().Be("Sales");
	}

	[Fact]
	public async Task Distinct_MultipleColumns()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinDistinct (Id INT64 NOT NULL, A STRING(10), B STRING(10)) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinDistinct",
			new Dictionary<string, object?> { ["Id"] = 1L, ["A"] = "x", ["B"] = "1" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["A"] = "x", ["B"] = "1" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["A"] = "x", ["B"] = "2" },
			new Dictionary<string, object?> { ["Id"] = 4L, ["A"] = "y", ["B"] = "1" });

		var rows = await QueryAsync("SELECT DISTINCT A, B FROM WinDistinct ORDER BY A, B");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Distinct_WithNull()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinDistNull (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinDistNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = null });

		var rows = await QueryAsync("SELECT DISTINCT Val FROM WinDistNull ORDER BY Val");
		rows.Should().HaveCount(2); // "a" and NULL
	}

	// ═══════════════════════════════════════════════════════════════
	// Window functions combined with WHERE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task WindowFunction_WithWhere()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RN
			FROM WinOrd
			WHERE Dept = 'Eng'
			ORDER BY RN");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Charlie");
	}

	// ═══════════════════════════════════════════════════════════════
	// Window functions in subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task WindowFunction_TopNPerGroup()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Dept, Name, Salary FROM (
				SELECT Dept, Name, Salary,
					ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS RN
				FROM WinOrd
			) WHERE RN = 1 ORDER BY Dept");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Charlie");  // Eng top
		rows[1]["Name"].Should().Be("Grace");    // HR top
		rows[2]["Name"].Should().Be("Frank");    // Sales top
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple window functions in same query
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultipleWindowFunctions()
	{
		await SeedData();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RN,
				RANK() OVER (ORDER BY Salary DESC) AS R,
				SUM(Salary) OVER () AS Total
			FROM WinOrd
			ORDER BY RN
			LIMIT 3");
		rows.Should().HaveCount(3);
		((long)rows[0]["RN"]!).Should().Be(1L);
		((long)rows[0]["R"]!).Should().Be(1L);
		// Total salary = 100+90+110+80+85+95+75+70 = 705000
		rows[0]["Total"].Should().Be(705000L);
	}
}
