using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for FOREIGN KEY constraints (Phase 11).
/// </summary>
public class ForeignKeyTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithFk()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Departments (DeptId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (DeptId)");
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
		db.ExecuteDdl("CREATE TABLE Employees (EmpId INT64 NOT NULL, DeptId INT64, CONSTRAINT FK_Dept FOREIGN KEY (DeptId) REFERENCES Departments (DeptId)) PRIMARY KEY (EmpId)");
		db.Insert("Departments", new Dictionary<string, object?> { ["DeptId"] = 1L, ["Name"] = "Engineering" });
		return db;
	}

	[Fact]
	public void ForeignKey_Insert_WithValidReference_Succeeds()
	{
		using var db = CreateDatabaseWithFk();

		db.Insert("Employees", new Dictionary<string, object?> { ["EmpId"] = 100L, ["DeptId"] = 1L });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM Employees");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	public void ForeignKey_Insert_WithInvalidReference_Throws()
	{
		using var db = CreateDatabaseWithFk();

		var act = () => db.Insert("Employees", new Dictionary<string, object?> { ["EmpId"] = 100L, ["DeptId"] = 999L });

		act.Should().Throw<InvalidOperationException>().WithMessage("*Foreign key*FK_Dept*");
	}

	[Fact]
	public void ForeignKey_Insert_WithNull_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
		//   "If any column of the foreign key is NULL, the constraint is satisfied."
		using var db = CreateDatabaseWithFk();

		db.Insert("Employees", new Dictionary<string, object?> { ["EmpId"] = 100L, ["DeptId"] = null });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM Employees");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	public void ForeignKey_NotEnforced_SkipsValidation()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Departments (DeptId INT64 NOT NULL) PRIMARY KEY (DeptId)");
		db.ExecuteDdl("CREATE TABLE Employees (EmpId INT64 NOT NULL, DeptId INT64, CONSTRAINT FK_Dept FOREIGN KEY (DeptId) REFERENCES Departments (DeptId) NOT ENFORCED) PRIMARY KEY (EmpId)");

		// Should succeed even with no matching department
		db.Insert("Employees", new Dictionary<string, object?> { ["EmpId"] = 100L, ["DeptId"] = 999L });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM Employees");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	public void ForeignKey_Update_ToInvalidReference_Throws()
	{
		using var db = CreateDatabaseWithFk();
		db.Insert("Employees", new Dictionary<string, object?> { ["EmpId"] = 100L, ["DeptId"] = 1L });

		var act = () => db.ExecuteDml("UPDATE Employees SET DeptId = 999 WHERE EmpId = 100");

		act.Should().Throw<InvalidOperationException>().WithMessage("*Foreign key*FK_Dept*");
	}
}
