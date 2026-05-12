using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for advanced DML: INSERT OR UPDATE, INSERT OR IGNORE, INSERT FROM SELECT (Phase 10).
/// </summary>
public class AdvancedDmlTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithTable()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 10L });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Val"] = 20L });
		return db;
	}

	// ─── INSERT OR UPDATE ───

	[Fact]
	public void InsertOrUpdate_InsertsNewRow()
	{
		using var db = CreateDatabaseWithTable();

		var count = db.ExecuteDml("INSERT OR UPDATE INTO T (Id, Name, Val) VALUES (3, 'Charlie', 30)");

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 3");
		rows[0]["Name"].Should().Be("Charlie");
	}

	[Fact]
	public void InsertOrUpdate_UpdatesExistingRow()
	{
		using var db = CreateDatabaseWithTable();

		var count = db.ExecuteDml("INSERT OR UPDATE INTO T (Id, Name, Val) VALUES (1, 'Alice Updated', 999)");

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT Name, Val FROM T WHERE Id = 1");
		rows[0]["Name"].Should().Be("Alice Updated");
		Convert.ToInt64(rows[0]["Val"]).Should().Be(999L);
	}

	// ─── INSERT OR IGNORE ───

	[Fact]
	public void InsertOrIgnore_SkipsDuplicateKey()
	{
		using var db = CreateDatabaseWithTable();

		var count = db.ExecuteDml("INSERT OR IGNORE INTO T (Id, Name, Val) VALUES (1, 'Duplicate', 0)");

		count.Should().Be(0); // Skipped
		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 1");
		rows[0]["Name"].Should().Be("Alice"); // Unchanged
	}

	[Fact]
	public void InsertOrIgnore_InsertsNewRow()
	{
		using var db = CreateDatabaseWithTable();

		var count = db.ExecuteDml("INSERT OR IGNORE INTO T (Id, Name, Val) VALUES (3, 'Charlie', 30)");

		count.Should().Be(1);
	}

	// ─── INSERT FROM SELECT ───

	[Fact]
	public void InsertFromSelect_CopiesRows()
	{
		using var db = CreateDatabaseWithTable();
		db.ExecuteDdl("CREATE TABLE T2 (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");

		var count = db.ExecuteDml("INSERT INTO T2 (Id, Name, Val) SELECT Id, Name, Val FROM T");

		count.Should().Be(2);
		var rows = db.ExecuteQuery("SELECT * FROM T2 ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void InsertFromSelect_WithWhere_FiltersRows()
	{
		using var db = CreateDatabaseWithTable();
		db.ExecuteDdl("CREATE TABLE T2 (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");

		var count = db.ExecuteDml("INSERT INTO T2 (Id, Name, Val) SELECT Id, Name, Val FROM T WHERE Val > 15");

		count.Should().Be(1);
		var rows = db.ExecuteQuery("SELECT Name FROM T2");
		rows[0]["Name"].Should().Be("Bob");
	}

	// ─── Multiple value rows ───

	[Fact]
	public void InsertOrUpdate_MultipleRows()
	{
		using var db = CreateDatabaseWithTable();

		var count = db.ExecuteDml(
			"INSERT OR UPDATE INTO T (Id, Name, Val) VALUES (1, 'Updated', 100), (3, 'New', 300)");

		count.Should().Be(2);
		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		Convert.ToInt64(rows[0]["C"]).Should().Be(3);
	}
}
