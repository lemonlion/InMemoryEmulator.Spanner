using FluentAssertions;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for state export/import (JSON persistence).
/// </summary>
public class StatePersistenceTests
{
	private InMemorySpannerDatabase CreateDatabaseWithSingersTable()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		return db;
	}

	// ─── ExportState / ImportState round-trip ───

	[Fact]
	public void ExportState_EmptyDatabase_ReturnsValidJson()
	{
		using var db = new InMemorySpannerDatabase();

		var json = db.ExportState();

		json.Should().NotBeNullOrEmpty();
		json.Should().Contain("\"tables\"");
	}

	[Fact]
	public void ExportImport_EmptyTable_PreservesSchema()
	{
		using var db = CreateDatabaseWithSingersTable();

		var json = db.ExportState();
		db.ClearAll();
		db.GetTableNames().Should().BeEmpty();

		db.ImportState(json);

		db.GetTableNames().Should().Contain("Singers");
		var tableDef = db.GetTableDefinition("Singers");
		tableDef.Columns.Should().HaveCount(3);
		tableDef.PrimaryKeyColumns.Should().Contain("SingerId");
	}

	[Fact]
	public void ExportImport_WithData_PreservesRows()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });

		var json = db.ExportState();
		db.ClearAll();

		db.ImportState(json);

		var rows = db.ExecuteQuery("SELECT SingerId, Name, Age FROM Singers ORDER BY SingerId");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[1]["Name"].Should().Be("Bob");
	}

	[Fact]
	public void ExportImport_PreservesColumnTypes()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 42L, ["Name"] = "Test", ["Age"] = 99L });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var tableDef = db.GetTableDefinition("Singers");
		tableDef.Columns.First(c => c.Name == "SingerId").SpannerType.Should().Be(TypeCode.Int64);
		tableDef.Columns.First(c => c.Name == "Name").SpannerType.Should().Be(TypeCode.String);
		tableDef.Columns.First(c => c.Name == "Age").SpannerType.Should().Be(TypeCode.Int64);
	}

	[Fact]
	public void ExportImport_PreservesNullability()
	{
		using var db = CreateDatabaseWithSingersTable();

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var tableDef = db.GetTableDefinition("Singers");
		tableDef.Columns.First(c => c.Name == "SingerId").IsNullable.Should().BeFalse();
		tableDef.Columns.First(c => c.Name == "Name").IsNullable.Should().BeTrue();
	}

	[Fact]
	public void ExportImport_PreservesNullValues()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = null, ["Age"] = null });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var rows = db.ExecuteQuery("SELECT SingerId, Name, Age FROM Singers");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().BeNull();
		rows[0]["Age"].Should().BeNull();
	}

	[Fact]
	public void ExportImport_MultipleTables()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE A (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE TABLE B (Id INT64 NOT NULL, Value STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("A", new Dictionary<string, object?> { ["Id"] = 1L });
		db.Insert("B", new Dictionary<string, object?> { ["Id"] = 2L, ["Value"] = "hello" });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		db.GetTableNames().Should().Contain("A").And.Contain("B");
		db.ExecuteQuery("SELECT Id FROM A").Should().HaveCount(1);
		db.ExecuteQuery("SELECT Value FROM B")[0]["Value"].Should().Be("hello");
	}

	[Fact]
	public void ImportState_ReplacesExistingState()
	{
		using var db = CreateDatabaseWithSingersTable();
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		// Export state of database with one table
		var json = db.ExportState();

		// Add a second table and more data
		db.ExecuteDdl("CREATE TABLE Albums (AlbumId INT64 NOT NULL) PRIMARY KEY (AlbumId)");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });

		// Import should replace everything
		db.ImportState(json);

		db.GetTableNames().Should().Contain("Singers");
		db.GetTableNames().Should().NotContain("Albums");
		var rows = db.ExecuteQuery("SELECT Name FROM Singers");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void ImportState_InvalidJson_Throws()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ImportState("not valid json");

		act.Should().Throw<Exception>();
	}

	// ─── Indexes ───

	[Fact]
	public void ExportImport_PreservesIndexes()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Items (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE INDEX IX_Items_Name ON Items(Name)");

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		// Verify the table and data can still be used
		db.GetTableNames().Should().Contain("Items");
		// The index should have been restored — check by querying INFORMATION_SCHEMA
		var indexes = db.ExecuteQuery(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'Items' AND INDEX_NAME = 'IX_Items_Name'");
		indexes.Should().HaveCount(1);
	}

	[Fact]
	public void ExportImport_PreservesUniqueIndex()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Users (Id INT64 NOT NULL, Email STRING(MAX)) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE UNIQUE INDEX UX_Users_Email ON Users(Email)");

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		// Unique constraint should be enforced after import
		db.Insert("Users", new Dictionary<string, object?> { ["Id"] = 1L, ["Email"] = "a@b.com" });
		var act = () => db.Insert("Users", new Dictionary<string, object?> { ["Id"] = 2L, ["Email"] = "a@b.com" });

		act.Should().Throw<InvalidOperationException>();
	}

	// ─── Views ───

	[Fact]
	public void ExportImport_PreservesViews()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE VIEW V SQL SECURITY INVOKER AS SELECT Id, Val FROM T");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "hello" });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var rows = db.ExecuteQuery("SELECT Val FROM V");
		rows.Should().HaveCount(1);
		rows[0]["Val"].Should().Be("hello");
	}

	// ─── Sequences ───

	[Fact]
	public void ExportImport_PreservesSequences()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE SEQUENCE MySeq OPTIONS (sequence_kind = 'bit_reversed_positive', start_with_counter = 100)");
		// Use the sequence to advance its counter
		var val1 = db.ExecuteQuery("SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE MySeq) AS val");

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		// Sequence should still work after import
		var val2 = db.ExecuteQuery("SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE MySeq) AS val");
		val2.Should().HaveCount(1);
		val2[0]["val"].Should().NotBeNull();
	}

	// ─── File-based persistence ───

	[Fact]
	public void ExportToFile_ImportFromFile_RoundTrips()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"spanner_test_{Guid.NewGuid():N}.json");
		try
		{
			using var db = CreateDatabaseWithSingersTable();
			db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

			db.ExportStateToFile(tempFile);
			File.Exists(tempFile).Should().BeTrue();

			db.ClearAll();
			db.ImportStateFromFile(tempFile);

			var rows = db.ExecuteQuery("SELECT Name FROM Singers");
			rows.Should().HaveCount(1);
			rows[0]["Name"].Should().Be("Alice");
		}
		finally
		{
			if (File.Exists(tempFile)) File.Delete(tempFile);
		}
	}

	// ─── String column with max length ───

	[Fact]
	public void ExportImport_PreservesMaxLength()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Code STRING(10)) PRIMARY KEY (Id)");

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var tableDef = db.GetTableDefinition("T");
		tableDef.Columns.First(c => c.Name == "Code").MaxLength.Should().Be(10);
	}

	// ─── Disposed database ───

	[Fact]
	public void ExportState_DisposedDatabase_Throws()
	{
		var db = CreateDatabaseWithSingersTable();
		db.Dispose();

		var act = () => db.ExportState();

		act.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public void ImportState_DisposedDatabase_Throws()
	{
		var db = new InMemorySpannerDatabase();
		db.Dispose();

		var act = () => db.ImportState("{}");

		act.Should().Throw<ObjectDisposedException>();
	}

	// ─── Boolean and various value types ───

	[Fact]
	public void ExportImport_PreservesBoolValues()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Flags (Id INT64 NOT NULL, Active BOOL) PRIMARY KEY (Id)");
		db.Insert("Flags", new Dictionary<string, object?> { ["Id"] = 1L, ["Active"] = true });
		db.Insert("Flags", new Dictionary<string, object?> { ["Id"] = 2L, ["Active"] = false });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var rows = db.ExecuteQuery("SELECT Id, Active FROM Flags ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Active"].Should().Be(true);
		rows[1]["Active"].Should().Be(false);
	}

	[Fact]
	public void ExportImport_PreservesFloat64Values()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Metrics (Id INT64 NOT NULL, Score FLOAT64) PRIMARY KEY (Id)");
		db.Insert("Metrics", new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = 3.14 });

		var json = db.ExportState();
		db.ClearAll();
		db.ImportState(json);

		var rows = db.ExecuteQuery("SELECT Score FROM Metrics");
		rows.Should().HaveCount(1);
		Convert.ToDouble(rows[0]["Score"]).Should().BeApproximately(3.14, 0.001);
	}
}
