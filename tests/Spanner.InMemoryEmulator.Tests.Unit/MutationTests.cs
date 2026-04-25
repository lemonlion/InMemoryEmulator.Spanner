using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for the direct mutation API (Insert, Update, Delete, InsertOrUpdate, Replace).
/// </summary>
public class MutationTests
{
	private InMemorySpannerDatabase CreateDatabaseWithSingersTable()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		return db;
	}

	// ─── INSERT ───

	[Fact]
	public void Insert_NewRow_Succeeds()
	{
		using var db = CreateDatabaseWithSingersTable();

		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		var rows = db.ExecuteQuery("SELECT SingerId, Name, Age FROM Singers");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void Insert_DuplicateKey_ThrowsException()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		var act = () => db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Bob" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
	}

	[Fact]
	public void Insert_NotNullViolation_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Required (Id INT64 NOT NULL, Value STRING(MAX) NOT NULL) PRIMARY KEY (Id)");

		var act = () => db.Insert("Required", new Dictionary<string, object?> { ["Id"] = 1L, ["Value"] = null });

		act.Should().Throw<InvalidOperationException>().WithMessage("*NOT NULL*");
	}

	[Fact]
	public void Insert_MissingPrimaryKey_ThrowsException()
	{
		using var db = CreateDatabaseWithSingersTable();

		var act = () => db.Insert("Singers", new Dictionary<string, object?> { ["Name"] = "Alice" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*Primary key*");
	}

	[Fact]
	public void Insert_NonExistentTable_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.Insert("NonExistent", new Dictionary<string, object?> { ["Id"] = 1L });

		act.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");
	}

	// ─── UPDATE ───

	[Fact]
	public void Update_ExistingRow_UpdatesValues()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		db.Update("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice B.", ["Age"] = 31L });

		var rows = db.ExecuteQuery("SELECT Name, Age FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice B.");
		rows[0]["Age"].Should().Be(31L);
	}

	[Fact]
	public void Update_NonExistentRow_ThrowsException()
	{
		using var db = CreateDatabaseWithSingersTable();

		var act = () => db.Update("Singers", new Dictionary<string, object?> { ["SingerId"] = 999L, ["Name"] = "Nobody" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");
	}

	[Fact]
	public void Update_PartialColumns_OnlyUpdatesSpecifiedColumns()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		db.Update("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Age"] = 31L });

		var rows = db.ExecuteQuery("SELECT Name, Age FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice"); // Unchanged
		rows[0]["Age"].Should().Be(31L);
	}

	// ─── INSERT OR UPDATE (UPSERT) ───

	[Fact]
	public void InsertOrUpdate_NewRow_InsertsRow()
	{
		using var db = CreateDatabaseWithSingersTable();

		db.InsertOrUpdate("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void InsertOrUpdate_ExistingRow_UpdatesRow()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		db.InsertOrUpdate("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice B." });

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Alice B.");
	}

	// ─── REPLACE ───

	[Fact]
	public void Replace_ExistingRow_ReplacesEntireRow()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		db.Replace("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Bob" });

		var rows = db.ExecuteQuery("SELECT Name, Age FROM Singers WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Bob");
		rows[0]["Age"].Should().BeNull(); // Age was not specified in the replace, so it should be null
	}

	[Fact]
	public void Replace_NewRow_InsertsRow()
	{
		using var db = CreateDatabaseWithSingersTable();

		db.Replace("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");
		rows.Should().HaveCount(1);
	}

	// ─── DELETE ───

	[Fact]
	public void Delete_ExistingRow_RemovesRow()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		db.Delete("Singers", 1L);

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().BeEmpty();
	}

	[Fact]
	public void Delete_NonExistentRow_DoesNotThrow()
	{
		using var db = CreateDatabaseWithSingersTable();

		// A delete of a non-existent row should be a no-op
		db.Delete("Singers", 999L);
	}

	// ─── DELETE RANGE ───

	[Fact]
	public void DeleteRange_RemovesRowsInRange()
	{
		using var db = CreateDatabaseWithSingersTable();
		for (long i = 1; i <= 5; i++)
			db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = i, ["Name"] = $"Singer {i}" });

		db.DeleteRange("Singers", new object[] { 2L }, new object[] { 4L }, startInclusive: true, endInclusive: true);

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers ORDER BY SingerId");
		rows.Should().HaveCount(2);
		rows[0]["SingerId"].Should().Be(1L);
		rows[1]["SingerId"].Should().Be(5L);
	}

	[Fact]
	public void DeleteRange_ExclusiveEnd_DoesNotDeleteEndKey()
	{
		using var db = CreateDatabaseWithSingersTable();
		for (long i = 1; i <= 3; i++)
			db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = i, ["Name"] = $"Singer {i}" });

		db.DeleteRange("Singers", new object[] { 1L }, new object[] { 3L }, startInclusive: true, endInclusive: false);

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().HaveCount(1);
		rows[0]["SingerId"].Should().Be(3L);
	}

	// ─── Multiple rows ───

	[Fact]
	public void Insert_MultipleRows_AllPersisted()
	{
		using var db = CreateDatabaseWithSingersTable();

		for (long i = 1; i <= 10; i++)
		{
			db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = i, ["Name"] = $"Singer {i}" });
		}

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().HaveCount(10);
	}

	// ─── ClearAllData / ClearAll ───

	[Fact]
	public void ClearAllData_ClearsRowsButKeepsSchema()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		db.ClearAllData();

		db.GetTableNames().Should().Contain("Singers"); // Schema still exists
		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers");
		rows.Should().BeEmpty(); // Data is gone
	}

	[Fact]
	public void ClearAll_ClearsSchemaAndData()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		db.ClearAll();

		db.GetTableNames().Should().BeEmpty();
	}
}
