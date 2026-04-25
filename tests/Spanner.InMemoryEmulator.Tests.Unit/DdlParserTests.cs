using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for DDL parsing and schema management via the direct API.
/// </summary>
public class DdlParserTests
{
	// ─── CREATE TABLE ───

	[Fact]
	public void CreateTable_BasicTableWithPrimaryKey_CreatesSchema()
	{
		// Arrange
		using var db = new InMemorySpannerDatabase();

		// Act
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");

		// Assert
		var table = db.GetTableDefinition("Singers");
		table.Name.Should().Be("Singers");
		table.Columns.Should().HaveCount(2);
		table.PrimaryKeyColumns.Should().Equal("SingerId");
	}

	[Fact]
	public void CreateTable_AllColumnTypes_CreatesCorrectSchema()
	{
		// Arrange
		using var db = new InMemorySpannerDatabase();
		var ddl = @"CREATE TABLE AllTypes (
			Id INT64 NOT NULL,
			BoolCol BOOL,
			Int64Col INT64,
			Float64Col FLOAT64,
			Float32Col FLOAT32,
			StringCol STRING(100),
			StringMaxCol STRING(MAX),
			BytesCol BYTES(256),
			BytesMaxCol BYTES(MAX),
			TimestampCol TIMESTAMP,
			DateCol DATE,
			NumericCol NUMERIC,
			JsonCol JSON
		) PRIMARY KEY (Id)";

		// Act
		db.ExecuteDdl(ddl);

		// Assert
		var table = db.GetTableDefinition("AllTypes");
		table.Columns.Should().HaveCount(13);

		table.Columns[0].Name.Should().Be("Id");
		table.Columns[0].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Int64);
		table.Columns[0].IsNullable.Should().BeFalse();

		table.Columns[1].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Bool);
		table.Columns[1].IsNullable.Should().BeTrue();

		table.Columns[5].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.String);
		table.Columns[5].MaxLength.Should().Be(100);

		table.Columns[6].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.String);
		table.Columns[6].MaxLength.Should().BeNull(); // MAX

		table.Columns[9].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Timestamp);
		table.Columns[10].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Date);
		table.Columns[11].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Numeric);
		table.Columns[12].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Json);
	}

	[Fact]
	public void CreateTable_ArrayColumn_CreatesCorrectType()
	{
		using var db = new InMemorySpannerDatabase();

		db.ExecuteDdl("CREATE TABLE WithArrays (Id INT64 NOT NULL, Tags ARRAY<STRING(MAX)>) PRIMARY KEY (Id)");

		var table = db.GetTableDefinition("WithArrays");
		table.Columns[1].SpannerType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.Array);
		table.Columns[1].ArrayElementType.Should().Be(Google.Cloud.Spanner.V1.TypeCode.String);
	}

	[Fact]
	public void CreateTable_CompositePrimaryKey_CreatesCorrectSchema()
	{
		using var db = new InMemorySpannerDatabase();

		db.ExecuteDdl("CREATE TABLE Albums (SingerId INT64 NOT NULL, AlbumId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (SingerId, AlbumId)");

		var table = db.GetTableDefinition("Albums");
		table.PrimaryKeyColumns.Should().Equal("SingerId", "AlbumId");
	}

	[Fact]
	public void CreateTable_DuplicateTable_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL) PRIMARY KEY (SingerId)");

		var act = () => db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL) PRIMARY KEY (SingerId)");

		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
	}

	[Fact]
	public void CreateTable_WithInterleaveInParent_SetsParentTable()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL) PRIMARY KEY (SingerId)");
		db.ExecuteDdl("CREATE TABLE Albums (SingerId INT64 NOT NULL, AlbumId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (SingerId, AlbumId), INTERLEAVE IN PARENT Singers ON DELETE CASCADE");

		var albums = db.GetTableDefinition("Albums");
		albums.ParentTable.Should().Be("Singers");
		albums.OnDeleteAction.Should().Be(OnDeleteAction.Cascade);
	}

	[Fact]
	public void CreateTable_MultipleDdlStatements_CreatesAllTables()
	{
		using var db = new InMemorySpannerDatabase();

		db.ExecuteDdl(
			"CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)",
			"CREATE TABLE Albums (AlbumId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (AlbumId)");

		db.GetTableNames().Should().HaveCount(2);
	}

	// ─── DROP TABLE ───

	[Fact]
	public void DropTable_ExistingTable_RemovesTable()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL) PRIMARY KEY (SingerId)");

		db.ExecuteDdl("DROP TABLE Singers");

		db.GetTableNames().Should().BeEmpty();
	}

	[Fact]
	public void DropTable_NonExistent_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ExecuteDdl("DROP TABLE NonExistent");

		act.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");
	}

	// ─── ALTER TABLE ───

	[Fact]
	public void AlterTable_AddColumn_AddsNewColumn()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL) PRIMARY KEY (SingerId)");

		db.ExecuteDdl("ALTER TABLE Singers ADD COLUMN Name STRING(MAX)");

		var table = db.GetTableDefinition("Singers");
		table.Columns.Should().HaveCount(2);
		table.Columns[1].Name.Should().Be("Name");
	}

	[Fact]
	public void AlterTable_DropColumn_RemovesColumn()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");

		db.ExecuteDdl("ALTER TABLE Singers DROP COLUMN Name");

		var table = db.GetTableDefinition("Singers");
		table.Columns.Should().HaveCount(1);
		table.Columns[0].Name.Should().Be("SingerId");
	}

	// ─── CREATE INDEX ───

	[Fact]
	public void CreateIndex_BasicIndex_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");

		// Act — should not throw
		db.ExecuteDdl("CREATE INDEX SingersByName ON Singers (Name)");
	}

	[Fact]
	public void CreateIndex_UniqueNullFilteredWithStoring_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Email STRING(MAX)) PRIMARY KEY (SingerId)");

		db.ExecuteDdl("CREATE UNIQUE NULL_FILTERED INDEX SingersByEmail ON Singers (Email) STORING (Name)");
	}

	// ─── DROP INDEX ───

	[Fact]
	public void DropIndex_ExistingIndex_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");
		db.ExecuteDdl("CREATE INDEX SingersByName ON Singers (Name)");

		db.ExecuteDdl("DROP INDEX SingersByName");
	}

	// ─── Schema introspection ───

	[Fact]
	public void GetTableNames_ReturnsAllTableNames()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE A (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE TABLE B (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		db.GetTableNames().Should().Contain("A").And.Contain("B");
	}

	// ─── Error cases ───

	[Fact]
	public void ExecuteDdl_EmptyString_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ExecuteDdl("");

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ExecuteDdl_InvalidSyntax_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ExecuteDdl("NOT VALID SQL");

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void CreateTable_PrimaryKeyNotInColumns_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();

		var act = () => db.ExecuteDdl("CREATE TABLE Singers (Name STRING(MAX)) PRIMARY KEY (SingerId)");

		act.Should().Throw<InvalidOperationException>().WithMessage("*not defined*");
	}
}
