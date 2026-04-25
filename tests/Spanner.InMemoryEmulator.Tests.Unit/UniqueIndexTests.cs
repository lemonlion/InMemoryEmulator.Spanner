using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for unique index constraint enforcement (Phase 11).
/// </summary>
public class UniqueIndexTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithUniqueIndex()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Email STRING(MAX), Name STRING(MAX)) PRIMARY KEY (Id)");
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
		db.ExecuteDdl("CREATE UNIQUE INDEX IX_Email ON T (Email)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Email"] = "alice@test.com", ["Name"] = "Alice" });
		return db;
	}

	[Fact]
	public void Insert_DuplicateUniqueIndexValue_Throws()
	{
		using var db = CreateDatabaseWithUniqueIndex();

		var act = () => db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "alice@test.com", ["Name"] = "Bob" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Email*");
	}

	[Fact]
	public void Insert_DifferentUniqueIndexValue_Succeeds()
	{
		using var db = CreateDatabaseWithUniqueIndex();

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "bob@test.com", ["Name"] = "Bob" });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	public void Update_ToConflictingUniqueValue_Throws()
	{
		using var db = CreateDatabaseWithUniqueIndex();
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "bob@test.com", ["Name"] = "Bob" });

		var act = () => db.ExecuteDml("UPDATE T SET Email = 'alice@test.com' WHERE Id = 2");

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Email*");
	}

	[Fact]
	public void Update_SameRowSameValue_Succeeds()
	{
		using var db = CreateDatabaseWithUniqueIndex();

		// Updating a row to keep its own value should not trigger unique violation
		db.ExecuteDml("UPDATE T SET Name = 'Alice Updated' WHERE Id = 1");

		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 1");
		rows[0]["Name"].Should().Be("Alice Updated");
	}

	[Fact]
	public void Insert_NullInUniqueColumn_AllowsMultiple()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
		//   "UNIQUE constraint allows multiple NULLs"
		using var db = CreateDatabaseWithUniqueIndex();

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = null, ["Name"] = "Bob" });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 3L, ["Email"] = null, ["Name"] = "Charlie" });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		rows[0]["C"].Should().Be(3L);
	}

	[Fact]
	public void NullFiltered_UniqueIndex_SkipsNullRows()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Code STRING(MAX)) PRIMARY KEY (Id)");
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
		//   "NULL_FILTERED: Excludes NULL values from the index."
		db.ExecuteDdl("CREATE UNIQUE NULL_FILTERED INDEX IX_Code ON T (Code)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Code"] = null });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Code"] = null });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	public void NullFiltered_UniqueIndex_EnforcesDuplicateNonNull()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Code STRING(MAX)) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE UNIQUE NULL_FILTERED INDEX IX_Code ON T (Code)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Code"] = "ABC" });

		var act = () => db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Code"] = "ABC" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Code*");
	}

	[Fact]
	public void Mutation_InsertOrUpdate_ViolatesUniqueIndex_Throws()
	{
		using var db = CreateDatabaseWithUniqueIndex();
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "bob@test.com", ["Name"] = "Bob" });

		// InsertOrUpdate row 2 with email that conflicts with row 1
		var act = () => db.InsertOrUpdate("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "alice@test.com", ["Name"] = "Bob" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Email*");
	}

	[Fact]
	public void Mutation_Replace_ViolatesUniqueIndex_Throws()
	{
		using var db = CreateDatabaseWithUniqueIndex();
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "bob@test.com", ["Name"] = "Bob" });

		var act = () => db.Replace("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "alice@test.com", ["Name"] = "Bob" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Email*");
	}

	[Fact]
	public void DmlInsertOrUpdate_ViolatesUniqueIndex_Throws()
	{
		using var db = CreateDatabaseWithUniqueIndex();
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "bob@test.com", ["Name"] = "Bob" });

		var act = () => db.ExecuteDml("INSERT OR UPDATE INTO T (Id, Email, Name) VALUES (2, 'alice@test.com', 'Bob')");

		act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE index*IX_Email*");
	}
}
