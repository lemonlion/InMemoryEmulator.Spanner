using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for subqueries, CTEs, and set operations (Phase 9).
/// </summary>
public class SubqueryCteSetTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithData()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE Employees (Id INT64 NOT NULL, Name STRING(MAX), DeptId INT64, Salary INT64) PRIMARY KEY (Id)",
			"CREATE TABLE Departments (DeptId INT64 NOT NULL, DeptName STRING(MAX)) PRIMARY KEY (DeptId)");

		db.Insert("Departments", new Dictionary<string, object?> { ["DeptId"] = 1L, ["DeptName"] = "Engineering" });
		db.Insert("Departments", new Dictionary<string, object?> { ["DeptId"] = 2L, ["DeptName"] = "Marketing" });

		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["DeptId"] = 1L, ["Salary"] = 100000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["DeptId"] = 1L, ["Salary"] = 90000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["DeptId"] = 2L, ["Salary"] = 80000L });

		return db;
	}

	// ─── Scalar Subquery ───

	[Fact]
	public void ScalarSubquery_InSelect_ReturnsValue()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name, (SELECT COUNT(*) FROM Employees) AS TotalEmp FROM Employees WHERE Id = 1");

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
		Convert.ToInt64(rows[0]["TotalEmp"]).Should().Be(3);
	}

	[Fact]
	public void ScalarSubquery_InWhere_FiltersCorrectly()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE Salary > (SELECT AVG(Salary) FROM Employees)");

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void ScalarSubquery_ReturnsNull_WhenEmpty()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });

		var rows = db.ExecuteQuery(
			"SELECT (SELECT Id FROM T WHERE Id = 999) AS Result");

		rows.Should().HaveCount(1);
		rows[0]["Result"].Should().BeNull();
	}

	// ─── EXISTS ───

	[Fact]
	public void Exists_ReturnsTrue_WhenRowsExist()
	{
		using var db = CreateDatabaseWithData();

		// Non-correlated EXISTS: check if Engineering department exists
		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE EXISTS (SELECT 1 FROM Departments WHERE DeptName = 'Engineering')");

		// EXISTS returns true (Engineering dept exists), so all employees are returned
		rows.Should().HaveCount(3);
	}

	[Fact]
	public void Exists_ReturnsFalse_WhenNoRows()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE EXISTS (SELECT 1 FROM Departments WHERE DeptName = 'Sales')");

		rows.Should().BeEmpty();
	}

	// ─── IN Subquery ───

	[Fact]
	public void InSubquery_FiltersCorrectly()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE DeptId IN (SELECT DeptId FROM Departments WHERE DeptName = 'Marketing')");

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Charlie");
	}

	[Fact]
	public void NotInSubquery_FiltersCorrectly()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE DeptId NOT IN (SELECT DeptId FROM Departments WHERE DeptName = 'Marketing')");

		rows.Should().HaveCount(2);
	}

	// ─── FROM Subquery ───

	[Fact]
	public void SubqueryInFrom_Works()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT sub.Name FROM (SELECT Name FROM Employees WHERE Salary >= 90000) AS sub");

		rows.Should().HaveCount(2);
	}

	// ─── ARRAY Subquery ───

	[Fact]
	public void ArraySubquery_CollectsValues()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT ARRAY(SELECT Name FROM Employees ORDER BY Name) AS Names");

		rows.Should().HaveCount(1);
		var names = rows[0]["Names"];
		names.Should().NotBeNull();
	}

	// ─── CTE ───

	[Fact]
	public void Cte_SingleDefinition_Works()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"WITH HighSalary AS (SELECT Name, Salary FROM Employees WHERE Salary >= 90000) SELECT Name FROM HighSalary ORDER BY Name");

		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[1]["Name"].Should().Be("Bob");
	}

	[Fact]
	public void Cte_MultipleCtes_Works()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(@"
			WITH Eng AS (SELECT Id, Name FROM Employees WHERE DeptId = 1),
			     Mkt AS (SELECT Id, Name FROM Employees WHERE DeptId = 2)
			SELECT Name FROM Eng
			UNION ALL
			SELECT Name FROM Mkt");

		rows.Should().HaveCount(3);
	}

	// ─── UNION ALL ───

	[Fact]
	public void UnionAll_CombinesAllRows()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE DeptId = 1 UNION ALL SELECT Name FROM Employees WHERE DeptId = 2");

		rows.Should().HaveCount(3);
	}

	// ─── UNION DISTINCT ───

	[Fact]
	public void UnionDistinct_RemovesDuplicates()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT DeptId FROM Employees UNION DISTINCT SELECT DeptId FROM Employees");

		rows.Should().HaveCount(2);
	}

	// ─── EXCEPT ALL ───

	[Fact]
	public void ExceptAll_SubtractsRows()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees EXCEPT ALL SELECT Name FROM Employees WHERE DeptId = 2");

		rows.Should().HaveCount(2); // Alice and Bob remain
	}

	// ─── INTERSECT ALL ───

	[Fact]
	public void IntersectAll_ReturnsCommonRows()
	{
		using var db = CreateDatabaseWithData();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Employees WHERE DeptId = 1 INTERSECT ALL SELECT Name FROM Employees WHERE Salary > 80000");

		rows.Should().HaveCount(2); // Alice and Bob are in both
	}
}
