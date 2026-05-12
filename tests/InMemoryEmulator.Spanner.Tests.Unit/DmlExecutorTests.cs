using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for DML statement execution (INSERT, UPDATE, DELETE via SQL).
/// </summary>
public class DmlExecutorTests
{
	private InMemorySpannerDatabase CreateDatabaseWithSingersTable()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		return db;
	}

	// ─── INSERT via DML ───

	[Fact]
	public void InsertDml_InsertsRow_ReturnsRowCount()
	{
		using var db = CreateDatabaseWithSingersTable();

		var count = db.ExecuteDml("INSERT INTO Singers (SingerId, Name, Age) VALUES (1, 'Alice', 30)");

		count.Should().Be(1);

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void InsertDml_MultipleRows_ReturnsCorrectCount()
	{
		using var db = CreateDatabaseWithSingersTable();

		var count = db.ExecuteDml("INSERT INTO Singers (SingerId, Name) VALUES (1, 'Alice'), (2, 'Bob'), (3, 'Charlie')");

		count.Should().Be(3);
		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public void InsertDml_DuplicateKey_ThrowsException()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.ExecuteDml("INSERT INTO Singers (SingerId, Name) VALUES (1, 'Alice')");

		var act = () => db.ExecuteDml("INSERT INTO Singers (SingerId, Name) VALUES (1, 'Bob')");

		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
	}

	[Fact]
	public void InsertDml_WithParameter_InsertsCorrectly()
	{
		using var db = CreateDatabaseWithSingersTable();

		var count = db.ExecuteDml(
			"INSERT INTO Singers (SingerId, Name) VALUES (@id, @name)",
			new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "Alice" });

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice");
	}

	// ─── UPDATE via DML ───

	[Fact]
	public void UpdateDml_UpdatesMatchingRows_ReturnsRowCount()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });

		var count = db.ExecuteDml("UPDATE Singers SET Age = 31 WHERE SingerId = 1");

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT Age FROM Singers WHERE SingerId = 1");
		rows[0]["Age"].Should().Be(31L);
	}

	[Fact]
	public void UpdateDml_NoMatchingRows_ReturnsZero()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		var count = db.ExecuteDml("UPDATE Singers SET Name = 'Nobody' WHERE SingerId = 999");

		count.Should().Be(0);
	}

	[Fact]
	public void UpdateDml_UpdateMultipleRows_ReturnsCorrectCount()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie", ["Age"] = 25L });

		var count = db.ExecuteDml("UPDATE Singers SET Age = 26 WHERE Age = 25");

		count.Should().Be(2);
	}

	[Fact]
	public void UpdateDml_MultipleSetClauses_UpdatesAllColumns()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		db.ExecuteDml("UPDATE Singers SET Name = 'Alice B.', Age = 31 WHERE SingerId = 1");

		var rows = db.ExecuteQuery("SELECT Name, Age FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice B.");
		rows[0]["Age"].Should().Be(31L);
	}

	[Fact]
	public void UpdateDml_WithParameter_UpdatesCorrectly()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		db.ExecuteDml(
			"UPDATE Singers SET Name = @name WHERE SingerId = @id",
			new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "Alice B." });

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice B.");
	}

	// ─── DELETE via DML ───

	[Fact]
	public void DeleteDml_DeletesMatchingRows_ReturnsRowCount()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });

		var count = db.ExecuteDml("DELETE FROM Singers WHERE SingerId = 1");

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().HaveCount(1);
		rows[0]["SingerId"].Should().Be(2L);
	}

	[Fact]
	public void DeleteDml_NoMatchingRows_ReturnsZero()
	{
		using var db = CreateDatabaseWithSingersTable();

		var count = db.ExecuteDml("DELETE FROM Singers WHERE SingerId = 999");

		count.Should().Be(0);
	}

	[Fact]
	public void DeleteDml_DeleteAllRows_ReturnsCorrectCount()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie" });

		var count = db.ExecuteDml("DELETE FROM Singers WHERE TRUE");

		count.Should().Be(3);
		db.ExecuteQuery("SELECT SingerId FROM Singers").Should().BeEmpty();
	}

	// ─── Batch DML ───

	[Fact]
	public void ExecuteBatchDml_MultipleStatements_ReturnsPerStatementCounts()
	{
		using var db = CreateDatabaseWithSingersTable();

		var counts = db.ExecuteBatchDml(
			("INSERT INTO Singers (SingerId, Name) VALUES (1, 'Alice')", null),
			("INSERT INTO Singers (SingerId, Name) VALUES (2, 'Bob')", null),
			("INSERT INTO Singers (SingerId, Name) VALUES (3, 'Charlie')", null));

		counts.Should().Equal(1, 1, 1);
		db.ExecuteQuery("SELECT SingerId FROM Singers").Should().HaveCount(3);
	}

	[Fact]
	public void ExecuteBatchDml_MixedStatements_ReturnsCorrectCounts()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });

		var counts = db.ExecuteBatchDml(
			("UPDATE Singers SET Age = 31 WHERE SingerId = 1", null),
			("DELETE FROM Singers WHERE SingerId = 2", null),
			("INSERT INTO Singers (SingerId, Name, Age) VALUES (3, 'Charlie', 35)", null));

		counts.Should().Equal(1, 1, 1);
	}

	// ─── DML referencing non-existent table ───

	[Fact]
	public void DmlOnNonExistentTable_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ExecuteDml("INSERT INTO NonExistent (Id) VALUES (1)");

		act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
	}
}
