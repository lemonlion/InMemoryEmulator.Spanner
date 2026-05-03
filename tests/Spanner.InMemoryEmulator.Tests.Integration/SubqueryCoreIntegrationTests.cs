using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for subqueries: scalar, correlated, EXISTS, IN with subquery, CTEs.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SubqueryCoreIntegrationTests : IntegrationTestBase
{
	public SubqueryCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task SetupData(string prefix)
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE {prefix}_Dept (DeptId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (DeptId)",
			$"CREATE TABLE {prefix}_Emp (EmpId INT64 NOT NULL, DeptId INT64, Name STRING(MAX), Salary INT64) PRIMARY KEY (EmpId)");
		await InsertAsync($"{prefix}_Dept",
			new() { ["DeptId"] = 1L, ["Name"] = "Engineering" },
			new() { ["DeptId"] = 2L, ["Name"] = "Sales" },
			new() { ["DeptId"] = 3L, ["Name"] = "Marketing" });
		await InsertAsync($"{prefix}_Emp",
			new() { ["EmpId"] = 1L, ["DeptId"] = 1L, ["Name"] = "Alice", ["Salary"] = 100L },
			new() { ["EmpId"] = 2L, ["DeptId"] = 1L, ["Name"] = "Bob", ["Salary"] = 120L },
			new() { ["EmpId"] = 3L, ["DeptId"] = 2L, ["Name"] = "Charlie", ["Salary"] = 90L },
			new() { ["EmpId"] = 4L, ["DeptId"] = 2L, ["Name"] = "Diana", ["Salary"] = 80L });
	}

	// ─── Scalar subquery ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery_concepts

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task ScalarSubquery_InSelect()
	{
		var p = "SQ1";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name, (SELECT MAX(Salary) FROM {p}_Emp) AS MaxSalary 
			FROM {p}_Dept ORDER BY Name");
		rows.Should().HaveCount(3);
		rows[0]["MaxSalary"].Should().Be(120L);
		rows[1]["MaxSalary"].Should().Be(120L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task ScalarSubquery_InWhere()
	{
		var p = "SQ2";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Emp 
			WHERE Salary > (SELECT AVG(Salary) FROM {p}_Emp)
			ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Bob");
	}

	// ─── EXISTS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subquery_concepts

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task ExistsSubquery_ReturnsMatchingRows()
	{
		var p = "SQ3";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Dept d
			WHERE EXISTS (SELECT 1 FROM {p}_Emp e WHERE e.DeptId = d.DeptId)
			ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Engineering", "Sales");
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task NotExistsSubquery_FiltersCorrectly()
	{
		var p = "SQ4";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Dept d
			WHERE NOT EXISTS (SELECT 1 FROM {p}_Emp e WHERE e.DeptId = d.DeptId)
			ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Marketing");
	}

	// ─── IN with subquery ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#in_subquery_concepts

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task InSubquery_FiltersRows()
	{
		var p = "SQ5";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Emp 
			WHERE DeptId IN (SELECT DeptId FROM {p}_Dept WHERE Name = 'Engineering')
			ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task NotInSubquery_FiltersRows()
	{
		var p = "SQ6";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Emp 
			WHERE DeptId NOT IN (SELECT DeptId FROM {p}_Dept WHERE Name = 'Engineering')
			ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Charlie", "Diana");
	}

	// ─── Correlated subquery ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#correlated_subquery_concepts

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task CorrelatedSubquery_InSelect()
	{
		var p = "SQ7";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT d.Name, 
				   (SELECT COUNT(*) FROM {p}_Emp e WHERE e.DeptId = d.DeptId) AS EmpCount
			FROM {p}_Dept d
			ORDER BY d.Name");

		rows[0]["Name"].Should().Be("Engineering");
		rows[0]["EmpCount"].Should().Be(2L);
		rows[1]["Name"].Should().Be("Marketing");
		rows[1]["EmpCount"].Should().Be(0L);
		rows[2]["Name"].Should().Be("Sales");
		rows[2]["EmpCount"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task CorrelatedSubquery_MaxSalaryPerDept()
	{
		var p = "SQ8";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT e.Name, e.Salary 
			FROM {p}_Emp e
			WHERE e.Salary = (SELECT MAX(e2.Salary) FROM {p}_Emp e2 WHERE e2.DeptId = e.DeptId)
			ORDER BY e.Name");

		rows.Select(r => (string)r["Name"]!).Should().Equal("Bob", "Charlie");
	}

	// ─── Subquery in FROM (derived table) ───

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task DerivedTable_SubqueryInFrom()
	{
		var p = "SQ9";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT sub.DeptId, sub.TotalSalary
			FROM (SELECT DeptId, SUM(Salary) AS TotalSalary FROM {p}_Emp GROUP BY DeptId) sub
			ORDER BY sub.DeptId");

		rows.Should().HaveCount(2);
		rows[0]["DeptId"].Should().Be(1L);
		rows[0]["TotalSalary"].Should().Be(220L);
		rows[1]["DeptId"].Should().Be(2L);
		rows[1]["TotalSalary"].Should().Be(170L);
	}

	// ─── WITH (CTE) ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#with_clause

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task Cte_SimpleCase()
	{
		var p = "SQ10";
		await SetupData(p);

		var rows = await QueryAsync($@"
			WITH DeptSalaries AS (
				SELECT DeptId, SUM(Salary) AS Total
				FROM {p}_Emp GROUP BY DeptId
			)
			SELECT d.Name, ds.Total
			FROM {p}_Dept d
			JOIN DeptSalaries ds ON d.DeptId = ds.DeptId
			ORDER BY d.Name");

		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Engineering");
		rows[0]["Total"].Should().Be(220L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task Cte_MultipleCtes()
	{
		var p = "SQ11";
		await SetupData(p);

		var rows = await QueryAsync($@"
			WITH 
				DeptCounts AS (SELECT DeptId, COUNT(*) AS Cnt FROM {p}_Emp GROUP BY DeptId),
				DeptSums AS (SELECT DeptId, SUM(Salary) AS Total FROM {p}_Emp GROUP BY DeptId)
			SELECT dc.DeptId, dc.Cnt, ds.Total
			FROM DeptCounts dc 
			JOIN DeptSums ds ON dc.DeptId = ds.DeptId
			ORDER BY dc.DeptId");

		rows.Should().HaveCount(2);
		rows[0]["Cnt"].Should().Be(2L);
		rows[0]["Total"].Should().Be(220L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task Cte_ReferencedMultipleTimes()
	{
		var p = "SQ12";
		await SetupData(p);

		var rows = await QueryAsync($@"
			WITH EmpStats AS (
				SELECT DeptId, AVG(Salary) AS AvgSalary FROM {p}_Emp GROUP BY DeptId
			)
			SELECT e.Name, e.Salary, es.AvgSalary
			FROM {p}_Emp e
			JOIN EmpStats es ON e.DeptId = es.DeptId
			WHERE e.Salary > es.AvgSalary
			ORDER BY e.Name");

		rows.Select(r => (string)r["Name"]!).Should().Equal("Bob", "Charlie");
	}

	// ─── UNION / UNION ALL ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#union

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task UnionAll_CombinesResults()
	{
		var rows = await QueryAsync(@"
			SELECT 1 AS v UNION ALL SELECT 2 UNION ALL SELECT 3");
		rows.Select(r => (long)r["v"]!).OrderBy(x => x).Should().Equal(1L, 2L, 3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task UnionAll_IncludesDuplicates()
	{
		var rows = await QueryAsync("SELECT 1 AS v UNION ALL SELECT 1 AS v");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task Union_RemovesDuplicates()
	{
		var rows = await QueryAsync("SELECT 1 AS v UNION DISTINCT SELECT 1 AS v");
		rows.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task UnionAll_WithTableData()
	{
		var p = "SQ13";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name, 'Dept' AS Source FROM {p}_Dept
			UNION ALL
			SELECT Name, 'Emp' AS Source FROM {p}_Emp
			ORDER BY Name");

		rows.Should().HaveCount(7); // 3 depts + 4 emps
	}

	// ─── EXCEPT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#except

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task ExceptDistinct_RemovesCommonRows()
	{
		var rows = await QueryAsync(@"
			SELECT v FROM UNNEST([1,2,3,4,5]) AS v
			EXCEPT DISTINCT
			SELECT v FROM UNNEST([3,4,5,6,7]) AS v
			ORDER BY v");
		rows.Select(r => (long)r["v"]!).Should().Equal(1L, 2L);
	}

	// ─── INTERSECT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#intersect

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task IntersectDistinct_ReturnsCommonRows()
	{
		var rows = await QueryAsync(@"
			SELECT v FROM UNNEST([1,2,3,4,5]) AS v
			INTERSECT DISTINCT
			SELECT v FROM UNNEST([3,4,5,6,7]) AS v
			ORDER BY v");
		rows.Select(r => (long)r["v"]!).Should().Equal(3L, 4L, 5L);
	}

	// ─── Subquery returning no rows ───

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task ScalarSubquery_NoRows_ReturnsNull()
	{
		var table = "SQEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		// Use reader-based pattern since QueryScalarAsync returns "" for NULL
		var rows = await QueryAsync($"SELECT (SELECT MAX(Id) FROM {table}) AS V");
		rows[0]["V"].Should().BeNull();
	}

	// ─── Nested subqueries ───

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task NestedSubquery_TwoLevels()
	{
		var p = "SQNest";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM {p}_Emp
			WHERE DeptId = (
				SELECT DeptId FROM {p}_Dept 
				WHERE Name = (SELECT MAX(Name) FROM {p}_Dept WHERE DeptId IN (1, 2))
			)
			ORDER BY Name");

		rows.Select(r => (string)r["Name"]!).Should().Equal("Charlie", "Diana");
	}

	// ─── Subquery with LIMIT ───

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task SubqueryWithLimit()
	{
		var p = "SQLim";
		await SetupData(p);

		var rows = await QueryAsync($@"
			SELECT Name FROM (
				SELECT Name FROM {p}_Emp ORDER BY Salary DESC LIMIT 2
			) sub ORDER BY Name");

		rows.Should().HaveCount(2);
	}

	// ─── IN with literal list ───

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task InLiteralList_FiltersRows()
	{
		var p = "SQIn";
		await SetupData(p);

		var rows = await QueryAsync($"SELECT Name FROM {p}_Emp WHERE EmpId IN (1, 3) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Charlie");
	}

	[Fact]
	[Trait(TestTraits.Category, "Subquery")]
	public async Task NotInLiteralList_FiltersRows()
	{
		var p = "SQNIn";
		await SetupData(p);

		var rows = await QueryAsync($"SELECT Name FROM {p}_Emp WHERE EmpId NOT IN (1, 3) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Bob", "Diana");
	}
}
