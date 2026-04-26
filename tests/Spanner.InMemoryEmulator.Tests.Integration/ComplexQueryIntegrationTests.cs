using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Complex multi-feature query tests: multi-table JOINs, nested subqueries with aggregates,
/// GROUP BY + HAVING + ORDER BY + LIMIT combinations, correlated subqueries, CTEs, and
/// combinations of window functions with other clauses.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ComplexQueryIntegrationTests : IntegrationTestBase
{
	public ComplexQueryIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTablesAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE CqEmployees (Id INT64 NOT NULL, Name STRING(100), DeptId INT64, Salary INT64, HireDate DATE) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE CqDepartments (Id INT64 NOT NULL, Name STRING(100), Budget INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE CqProjects (Id INT64 NOT NULL, Name STRING(100), DeptId INT64, Active BOOL) PRIMARY KEY (Id)");
		}
		catch { }

		try
		{
			// Departments
			await InsertAsync("CqDepartments", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Engineering", ["Budget"] = 1000000L });
			await InsertAsync("CqDepartments", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Marketing", ["Budget"] = 500000L });
			await InsertAsync("CqDepartments", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "HR", ["Budget"] = 300000L });

			// Employees
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["DeptId"] = 1L, ["Salary"] = 120000L, ["HireDate"] = new DateTime(2020, 1, 15) });
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["DeptId"] = 1L, ["Salary"] = 110000L, ["HireDate"] = new DateTime(2019, 6, 1) });
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["DeptId"] = 1L, ["Salary"] = 95000L, ["HireDate"] = new DateTime(2021, 3, 10) });
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["DeptId"] = 2L, ["Salary"] = 85000L, ["HireDate"] = new DateTime(2020, 7, 20) });
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Eve", ["DeptId"] = 2L, ["Salary"] = 90000L, ["HireDate"] = new DateTime(2018, 4, 5) });
			await InsertAsync("CqEmployees", new Dictionary<string, object?> { ["Id"] = 6L, ["Name"] = "Frank", ["DeptId"] = 3L, ["Salary"] = 75000L, ["HireDate"] = new DateTime(2022, 1, 1) });

			// Projects
			await InsertAsync("CqProjects", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alpha", ["DeptId"] = 1L, ["Active"] = true });
			await InsertAsync("CqProjects", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Beta", ["DeptId"] = 1L, ["Active"] = false });
			await InsertAsync("CqProjects", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Gamma", ["DeptId"] = 2L, ["Active"] = true });
			await InsertAsync("CqProjects", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Delta", ["DeptId"] = 3L, ["Active"] = true });
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// Multi-table JOINs
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_TwoTables_EmployeeDept()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT e.Name, d.Name AS Dept FROM CqEmployees e JOIN CqDepartments d ON e.DeptId = d.Id ORDER BY e.Name");
		rows.Should().HaveCount(6);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Dept"].Should().Be("Engineering");
	}

	[Fact]
	public async Task Join_ThreeTables()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT e.Name AS EmpName, d.Name AS DeptName, p.Name AS ProjName
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			JOIN CqProjects p ON p.DeptId = d.Id
			WHERE p.Active = true
			ORDER BY e.Name, p.Name");
		rows.Should().NotBeEmpty();
		// Alice, Bob, Charlie are in Eng (dept 1) which has project Alpha (active)
		rows.Where(r => (string)r["EmpName"]! == "Alice" && (string)r["ProjName"]! == "Alpha").Should().HaveCount(1);
	}

	[Fact]
	public async Task LeftJoin_IncludesUnmatched()
	{
		await EnsureTablesAsync();
		// Add a department with no employees
		try { await ExecuteDmlAsync("INSERT INTO CqDepartments (Id, Name, Budget) VALUES (4, 'Empty', 0)"); } catch { }

		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept, COUNT(e.Id) AS EmpCount
			FROM CqDepartments d
			LEFT JOIN CqEmployees e ON e.DeptId = d.Id
			GROUP BY d.Name
			ORDER BY Dept");
		rows.Should().HaveCountGreaterOrEqualTo(3);
	}

	[Fact]
	public async Task CrossJoin()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept, p.Name AS Proj
			FROM CqDepartments d
			CROSS JOIN CqProjects p
			WHERE d.Id = 1 AND p.Active = true
			ORDER BY p.Name");
		rows.Should().NotBeEmpty();
	}

	[Fact]
	public async Task SelfJoin()
	{
		await EnsureTablesAsync();
		// Find pairs of employees in the same department
		var rows = await QueryAsync(@"
			SELECT e1.Name AS Emp1, e2.Name AS Emp2
			FROM CqEmployees e1
			JOIN CqEmployees e2 ON e1.DeptId = e2.DeptId AND e1.Id < e2.Id
			ORDER BY e1.Name, e2.Name");
		rows.Should().NotBeEmpty();
		// Eng has Alice(1), Bob(2), Charlie(3) -> 3 pairs
		rows.Where(r => (string)r["Emp1"]! == "Alice" && (string)r["Emp2"]! == "Bob").Should().HaveCount(1);
	}

	// ═══════════════════════════════════════════════════════════════
	// GROUP BY + HAVING + ORDER BY + LIMIT combinations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GroupBy_Having_OrderBy()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept, COUNT(*) AS Cnt, AVG(e.Salary) AS AvgSal
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			GROUP BY d.Name
			HAVING COUNT(*) > 1
			ORDER BY AvgSal DESC");
		rows.Should().HaveCount(2); // Engineering (3), Marketing (2)
		rows[0]["Dept"].Should().Be("Engineering"); // Higher avg salary
	}

	[Fact]
	public async Task GroupBy_Having_OrderBy_Limit()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept, SUM(e.Salary) AS TotalSal
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			GROUP BY d.Name
			ORDER BY TotalSal DESC
			LIMIT 2");
		rows.Should().HaveCount(2);
		rows[0]["Dept"].Should().Be("Engineering"); // Highest total: 325000
	}

	[Fact]
	public async Task GroupBy_OrderBy_LimitOffset()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept, COUNT(*) AS Cnt
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			GROUP BY d.Name
			ORDER BY Cnt DESC
			LIMIT 1 OFFSET 1");
		rows.Should().ContainSingle();
		rows[0]["Dept"].Should().Be("Marketing"); // 2nd highest count
	}

	[Fact]
	public async Task GroupBy_MultipleColumns()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT DeptId, EXTRACT(YEAR FROM HireDate) AS HireYear, COUNT(*) AS Cnt
			FROM CqEmployees
			GROUP BY DeptId, EXTRACT(YEAR FROM HireDate)
			ORDER BY DeptId, HireYear");
		rows.Should().NotBeEmpty();
	}

	[Fact]
	public async Task GroupBy_WithExpressions()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT CASE WHEN Salary >= 100000 THEN 'High' ELSE 'Low' END AS Band, COUNT(*) AS Cnt
			FROM CqEmployees
			GROUP BY CASE WHEN Salary >= 100000 THEN 'High' ELSE 'Low' END
			ORDER BY Band");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Subquery_InWhere_ScalarComparison()
	{
		await EnsureTablesAsync();
		// Employees with above average salary
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees
			WHERE Salary > (SELECT AVG(Salary) FROM CqEmployees)
			ORDER BY Name");
		rows.Should().NotBeEmpty();
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public async Task Subquery_Exists()
	{
		await EnsureTablesAsync();
		// Departments that have active projects
		var rows = await QueryAsync(@"
			SELECT d.Name FROM CqDepartments d
			WHERE EXISTS (SELECT 1 FROM CqProjects p WHERE p.DeptId = d.Id AND p.Active = true)
			ORDER BY d.Name");
		rows.Should().HaveCount(3); // Eng, Marketing, HR
	}

	[Fact]
	public async Task Subquery_NotExists()
	{
		await EnsureTablesAsync();
		// Departments that have no inactive projects
		var rows = await QueryAsync(@"
			SELECT d.Name FROM CqDepartments d
			WHERE NOT EXISTS (SELECT 1 FROM CqProjects p WHERE p.DeptId = d.Id AND p.Active = false)
			ORDER BY d.Name");
		rows.Should().NotBeEmpty();
		// Engineering has Beta (inactive), so it's excluded
	}

	[Fact]
	public async Task Subquery_InSelect()
	{
		await EnsureTablesAsync();
		// Employee name with department name from subquery
		var rows = await QueryAsync(@"
			SELECT e.Name,
				(SELECT d.Name FROM CqDepartments d WHERE d.Id = e.DeptId) AS Dept
			FROM CqEmployees e
			ORDER BY e.Name");
		rows.Should().HaveCount(6);
		rows[0]["Dept"].Should().Be("Engineering");
	}

	[Fact]
	public async Task Subquery_InFrom()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT DeptName, TotalSalary FROM (
				SELECT d.Name AS DeptName, SUM(e.Salary) AS TotalSalary
				FROM CqEmployees e
				JOIN CqDepartments d ON e.DeptId = d.Id
				GROUP BY d.Name
			) sub
			ORDER BY TotalSalary DESC");
		rows.Should().NotBeEmpty();
		rows[0]["DeptName"].Should().Be("Engineering");
	}

	[Fact]
	public async Task Subquery_In_WithConstants()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees
			WHERE DeptId IN (SELECT Id FROM CqDepartments WHERE Budget > 400000)
			ORDER BY Name");
		rows.Should().NotBeEmpty();
		// Engineering (1M) and Marketing (500K) budgets > 400K
	}

	// ═══════════════════════════════════════════════════════════════
	// CTEs (WITH clauses)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Cte_SingleCte()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			WITH HighSalary AS (
				SELECT Name, Salary FROM CqEmployees WHERE Salary > 100000
			)
			SELECT Name FROM HighSalary ORDER BY Name");
		rows.Should().HaveCount(2); // Alice (120K) and Bob (110K)
	}

	[Fact]
	public async Task Cte_MultipleCtes()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			WITH DeptStats AS (
				SELECT DeptId, AVG(Salary) AS AvgSal FROM CqEmployees GROUP BY DeptId
			),
			HighPayDepts AS (
				SELECT DeptId FROM DeptStats WHERE AvgSal > 90000
			)
			SELECT e.Name FROM CqEmployees e
			WHERE e.DeptId IN (SELECT DeptId FROM HighPayDepts)
			ORDER BY e.Name");
		rows.Should().NotBeEmpty();
	}

	[Fact]
	public async Task Cte_WithJoin()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			WITH DeptSummary AS (
				SELECT DeptId, COUNT(*) AS EmpCount, SUM(Salary) AS TotalSal
				FROM CqEmployees
				GROUP BY DeptId
			)
			SELECT d.Name, ds.EmpCount, ds.TotalSal
			FROM DeptSummary ds
			JOIN CqDepartments d ON d.Id = ds.DeptId
			ORDER BY ds.TotalSal DESC");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Engineering");
	}

	// ═══════════════════════════════════════════════════════════════
	// Window functions combined with other clauses
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_RowNumber_WithJoin()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT e.Name, d.Name AS Dept,
				ROW_NUMBER() OVER (PARTITION BY e.DeptId ORDER BY e.Salary DESC) AS Rank
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			ORDER BY Dept, Rank");
		rows.Should().HaveCount(6);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_Rank_DeptSalary()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				RANK() OVER (ORDER BY Salary DESC) AS SalRank
			FROM CqEmployees
			ORDER BY SalRank");
		rows.Should().HaveCount(6);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["SalRank"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_RunningTotal()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				SUM(Salary) OVER (ORDER BY Salary DESC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunTotal
			FROM CqEmployees
			ORDER BY Salary DESC");
		rows.Should().HaveCount(6);
		rows[0]["RunTotal"].Should().Be(120000L); // Just Alice
		((long)rows[1]["RunTotal"]!).Should().BeGreaterThan(120000L); // Alice + Bob
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Window_DeptAvg_Comparison()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				AVG(Salary) OVER (PARTITION BY DeptId) AS DeptAvg
			FROM CqEmployees
			ORDER BY Name");
		rows.Should().HaveCount(6);
	}

	// ═══════════════════════════════════════════════════════════════
	// DISTINCT queries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Distinct_SingleColumn()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync("SELECT DISTINCT DeptId FROM CqEmployees ORDER BY DeptId");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Distinct_MultipleColumns()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT DISTINCT DeptId, CASE WHEN Salary >= 100000 THEN 'High' ELSE 'Low' END AS Band FROM CqEmployees ORDER BY DeptId");
		rows.Should().NotBeEmpty();
	}

	[Fact]
	public async Task Distinct_WithOrderBy()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync("SELECT DISTINCT DeptId FROM CqEmployees ORDER BY DeptId DESC");
		rows.Should().HaveCount(3);
		rows[0]["DeptId"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Set operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnionAll_TwoQueries()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees WHERE DeptId = 1
			UNION ALL
			SELECT Name FROM CqEmployees WHERE DeptId = 2
			ORDER BY Name");
		rows.Should().HaveCount(5); // 3 Eng + 2 Marketing
	}

	[Fact]
	public async Task UnionDistinct_Deduplication()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT DeptId FROM CqEmployees WHERE DeptId IN (1, 2)
			UNION DISTINCT
			SELECT DeptId FROM CqEmployees WHERE DeptId IN (2, 3)
			ORDER BY DeptId");
		rows.Should().HaveCount(3); // 1, 2, 3
	}

	[Fact]
	public async Task IntersectDistinct()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT DeptId FROM CqEmployees WHERE DeptId IN (1, 2)
			INTERSECT DISTINCT
			SELECT DeptId FROM CqEmployees WHERE DeptId IN (2, 3)
			ORDER BY DeptId");
		rows.Should().ContainSingle().Which["DeptId"].Should().Be(2L);
	}

	[Fact]
	public async Task ExceptDistinct()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT DeptId FROM CqEmployees WHERE DeptId IN (1, 2, 3)
			EXCEPT DISTINCT
			SELECT DeptId FROM CqEmployees WHERE DeptId = 2
			ORDER BY DeptId");
		rows.Should().HaveCount(2); // 1 and 3
	}

	// ═══════════════════════════════════════════════════════════════
	// Complex expression combinations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Complex_CaseWithAggregate()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept,
				CASE
					WHEN SUM(e.Salary) > 200000 THEN 'Large'
					WHEN SUM(e.Salary) > 100000 THEN 'Medium'
					ELSE 'Small'
				END AS Size
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			GROUP BY d.Name
			ORDER BY d.Name");
		rows.Should().HaveCount(3);
		rows[0]["Size"].Should().Be("Large"); // Engineering: 325000
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Complex_SubqueryWithWindow()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary, Rank FROM (
				SELECT Name, Salary,
					RANK() OVER (ORDER BY Salary DESC) AS Rank
				FROM CqEmployees
			) ranked
			WHERE Rank <= 3
			ORDER BY Rank");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Complex_CteWithWindowAndFilter()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			WITH Ranked AS (
				SELECT Name, Salary, DeptId,
					ROW_NUMBER() OVER (PARTITION BY DeptId ORDER BY Salary DESC) AS Rn
				FROM CqEmployees
			)
			SELECT Name, Salary FROM Ranked WHERE Rn = 1 ORDER BY Salary DESC");
		rows.Should().HaveCount(3); // Top earner from each dept
		rows[0]["Name"].Should().Be("Alice"); // 120K
	}

	[Fact]
	public async Task Complex_MultipleAggregates()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT d.Name AS Dept,
				COUNT(*) AS Cnt,
				MIN(e.Salary) AS MinSal,
				MAX(e.Salary) AS MaxSal,
				SUM(e.Salary) AS TotalSal
			FROM CqEmployees e
			JOIN CqDepartments d ON e.DeptId = d.Id
			GROUP BY d.Name
			ORDER BY d.Name");
		rows.Should().HaveCount(3);
		rows[0]["Dept"].Should().Be("Engineering");
		rows[0]["Cnt"].Should().Be(3L);
		rows[0]["MinSal"].Should().Be(95000L);
		rows[0]["MaxSal"].Should().Be(120000L);
	}

	[Fact]
	public async Task Complex_NestingAggregateInScalar()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary,
				Salary - (SELECT AVG(Salary) FROM CqEmployees) AS DiffFromAvg
			FROM CqEmployees
			ORDER BY Name");
		rows.Should().HaveCount(6);
	}

	[Fact]
	public async Task Complex_OrderByExpression()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary FROM CqEmployees
			ORDER BY Salary * -1");
		rows.Should().HaveCount(6);
		// ORDER BY Salary * -1 ASC: most negative first → highest salary first
		rows[0]["Name"].Should().Be("Alice"); // Salary 120000 → -120000 (most negative)
	}

	[Fact]
	public async Task Complex_WhereWithMultipleConditions()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees
			WHERE DeptId = 1
				AND Salary > 100000
				AND Name != 'Charlie'
			ORDER BY Name");
		rows.Should().HaveCount(2); // Alice and Bob
	}

	[Fact]
	public async Task Complex_WhereWithOrAndAnd()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees
			WHERE (DeptId = 1 AND Salary > 100000) OR (DeptId = 2 AND Salary > 85000)
			ORDER BY Name");
		rows.Should().NotBeEmpty();
		// Alice(120K dept1), Bob(110K dept1), Eve(90K dept2)
	}

	[Fact]
	public async Task Complex_WhereWithBetween()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name, Salary FROM CqEmployees
			WHERE Salary BETWEEN 85000 AND 100000
			ORDER BY Salary");
		rows.Should().NotBeEmpty();
		// Diana(85K), Eve(90K), Charlie(95K)
	}

	[Fact]
	public async Task Complex_WhereWithIn_FromSubquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT Name FROM CqEmployees
			WHERE DeptId IN (
				SELECT Id FROM CqDepartments WHERE Budget >= 500000
			)
			ORDER BY Name");
		rows.Should().NotBeEmpty();
		// Engineering(1M) and Marketing(500K) qualify
	}

	[Fact]
	public async Task Complex_SelectAll_WithAlias()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT e.* FROM CqEmployees e WHERE e.Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
	}
}
