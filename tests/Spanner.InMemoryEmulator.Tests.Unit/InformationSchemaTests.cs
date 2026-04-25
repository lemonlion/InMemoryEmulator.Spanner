using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for INFORMATION_SCHEMA virtual tables.
/// </summary>
public class InformationSchemaTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");
		db.ExecuteDdl("CREATE TABLE Albums (AlbumId INT64 NOT NULL, SingerId INT64 NOT NULL, Title STRING(100)) PRIMARY KEY (AlbumId)");
		return db;
	}

	// ─── TABLES ───

	[Fact]
	public void Tables_ReturnsAllTables()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME");
		rows.Should().HaveCount(2);
		rows[0]["TABLE_NAME"].Should().Be("Albums");
		rows[1]["TABLE_NAME"].Should().Be("Singers");
	}

	[Fact]
	public void Tables_IncludesTableType()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Singers'");
		rows.Should().ContainSingle();
		rows[0]["TABLE_TYPE"].Should().Be("BASE TABLE");
	}

	// ─── COLUMNS ───

	[Fact]
	public void Columns_ReturnsAllColumns()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery(
			"SELECT COLUMN_NAME, ORDINAL_POSITION, IS_NULLABLE, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Singers' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"].Should().Be("SingerId");
		rows[0]["ORDINAL_POSITION"].Should().Be(1L);
		rows[0]["IS_NULLABLE"].Should().Be("NO");
		rows[0]["SPANNER_TYPE"].Should().Be("INT64");
		rows[1]["COLUMN_NAME"].Should().Be("Name");
		rows[1]["IS_NULLABLE"].Should().Be("YES");
		rows[1]["SPANNER_TYPE"].Should().Be("STRING(MAX)");
	}

	[Fact]
	public void Columns_StringWithLength()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery(
			"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Albums' AND COLUMN_NAME = 'Title'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"].Should().Be("STRING(100)");
	}

	// ─── INDEXES ───

	[Fact]
	public void Indexes_ReturnsPrimaryKeys()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery(
			"SELECT INDEX_NAME, IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'Singers'");
		rows.Should().ContainSingle();
		rows[0]["INDEX_NAME"].Should().Be("PRIMARY_KEY");
		rows[0]["IS_UNIQUE"].Should().Be(true);
	}

	[Fact]
	public void Indexes_IncludesSecondaryIndex()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE UNIQUE INDEX IX_Name ON Singers(Name)");
		var rows = db.ExecuteQuery(
			"SELECT INDEX_NAME, IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'Singers' AND INDEX_NAME != 'PRIMARY_KEY'");
		rows.Should().ContainSingle();
		rows[0]["INDEX_NAME"].Should().Be("IX_Name");
		rows[0]["IS_UNIQUE"].Should().Be(true);
	}

	// ─── INDEX_COLUMNS ───

	[Fact]
	public void IndexColumns_ReturnsPkColumns()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery(
			"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = 'Singers' AND INDEX_NAME = 'PRIMARY_KEY'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_NAME"].Should().Be("SingerId");
		rows[0]["ORDINAL_POSITION"].Should().Be(1L);
	}

	// ─── TABLE_CONSTRAINTS ───

	[Fact]
	public void TableConstraints_ReturnsPks()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery(
			"SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'Singers'");
		rows.Should().HaveCount(1);
		rows[0]["CONSTRAINT_TYPE"].Should().Be("PRIMARY KEY");
	}

	[Fact]
	public void TableConstraints_IncludesCheckConstraints()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE CkTable (Id INT64 NOT NULL, Val INT64, CONSTRAINT CK_Val CHECK(Val > 0)) PRIMARY KEY (Id)");
		var rows = db.ExecuteQuery(
			"SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'CkTable' AND CONSTRAINT_TYPE = 'CHECK'");
		rows.Should().ContainSingle();
		rows[0]["CONSTRAINT_NAME"].Should().Be("CK_Val");
	}

	// ─── CHECK_CONSTRAINTS ───

	[Fact]
	public void CheckConstraints_ReturnsExpression()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE CkTable2 (Id INT64 NOT NULL, Val INT64, CONSTRAINT CK_V2 CHECK(Val > 0)) PRIMARY KEY (Id)");
		var rows = db.ExecuteQuery(
			"SELECT CHECK_CLAUSE FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS WHERE CONSTRAINT_NAME = 'CK_V2'");
		rows.Should().ContainSingle();
		((string)rows[0]["CHECK_CLAUSE"]!).Should().Contain("Val > 0");
	}

	// ─── SCHEMATA ───

	[Fact]
	public void Schemata_ReturnsDefaultAndInformationSchema()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME");
		rows.Should().HaveCount(2);
		rows[0]["SCHEMA_NAME"].Should().Be("");
		rows[1]["SCHEMA_NAME"].Should().Be("INFORMATION_SCHEMA");
	}

	// ─── INTERLEAVED TABLE PARENT ───

	[Fact]
	public void Tables_ShowsParentTable()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE ParentTbl (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE TABLE ChildTbl (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT ParentTbl ON DELETE CASCADE");
		var rows = db.ExecuteQuery(
			"SELECT TABLE_NAME, PARENT_TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChildTbl'");
		rows.Should().ContainSingle();
		rows[0]["PARENT_TABLE_NAME"].Should().Be("ParentTbl");
	}
}
