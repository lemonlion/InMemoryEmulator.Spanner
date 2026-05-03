using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive schema DDL tests: CREATE TABLE, ALTER TABLE, DROP TABLE, 
/// CREATE INDEX, DROP INDEX, constraints, INFORMATION_SCHEMA queries.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SchemaExhaustiveIntegrationTests : IntegrationTestBase
{
	public SchemaExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private string UniqName(string prefix) => $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

	// ─── CREATE TABLE with various column types ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateTable_AllColumnTypes()
	{
		var t = UniqName("SchAll");
		await ExecuteDdlAsync(
			$@"CREATE TABLE {t} (
				Id INT64 NOT NULL,
				Str STRING(100),
				StrMax STRING(MAX),
				I64 INT64,
				F32 FLOAT32,
				F64 FLOAT64,
				B BOOL,
				Byt BYTES(256),
				BytMax BYTES(MAX),
				D DATE,
				Ts TIMESTAMP,
				J JSON,
				Num NUMERIC
			) PRIMARY KEY (Id)");
		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCountGreaterOrEqualTo(13);
		rows[0]["COLUMN_NAME"].Should().Be("Id");
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateTable_CompositePrimaryKey()
	{
		var t = UniqName("SchComp");
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (K1 INT64 NOT NULL, K2 STRING(MAX) NOT NULL, Val INT64) PRIMARY KEY (K1, K2)");
		await ExecuteDmlAsync($"INSERT INTO {t} (K1, K2, Val) VALUES (1, 'a', 10), (1, 'b', 20)");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateTable_DescPrimaryKey()
	{
		var t = UniqName("SchDesc");
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id DESC)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		var rows = await QueryAsync($"SELECT Id FROM {t}");
		rows.Should().HaveCount(3);
	}

	// ─── ALTER TABLE: ADD COLUMN ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task AlterTable_AddColumn()
	{
		var t = UniqName("SchAdd");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {t} ADD COLUMN Name STRING(MAX)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'Alice')");
		var rows = await QueryAsync($"SELECT Name FROM {t}");
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task AlterTable_AddMultipleColumns()
	{
		var t = UniqName("SchAdd2");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {t} ADD COLUMN Val INT64");
		await ExecuteDdlAsync($"ALTER TABLE {t} ADD COLUMN Name STRING(MAX)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val, Name) VALUES (1, 100, 'test')");
		var rows = await QueryAsync($"SELECT Val, Name FROM {t}");
		rows[0]["Val"].Should().Be(100L);
		rows[0]["Name"].Should().Be("test");
	}

	// ─── ALTER TABLE: DROP COLUMN ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task AlterTable_DropColumn()
	{
		var t = UniqName("SchDrp");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {t} DROP COLUMN Name");
		var cols = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' ORDER BY ORDINAL_POSITION");
		cols.Select(r => (string)r["COLUMN_NAME"]!).Should().NotContain("Name");
	}

	// ─── DROP TABLE ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task DropTable()
	{
		var t = UniqName("SchDrop");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"DROP TABLE {t}");
		var tables = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{t}'");
		tables.Should().BeEmpty();
	}

	// ─── CREATE INDEX ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateIndex_Simple()
	{
		var t = UniqName("SchIdx");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX Idx_{t} ON {t} (Name)");
		var indexes = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'Idx_{t}'");
		indexes.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateIndex_Composite()
	{
		var t = UniqName("SchIdx2");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX Idx_{t} ON {t} (A, B)");
		var indexes = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'Idx_{t}'");
		indexes.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateUniqueIndex()
	{
		var t = UniqName("SchUniq");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Email STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE UNIQUE INDEX UIdx_{t} ON {t} (Email)");
		var indexes = await QueryAsync(
			$"SELECT IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'UIdx_{t}'");
		indexes.Should().HaveCount(1);
		indexes[0]["IS_UNIQUE"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateNullFilteredIndex()
	{
		var t = UniqName("SchNF");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE NULL_FILTERED INDEX NFIdx_{t} ON {t} (Val)");
		var indexes = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'NFIdx_{t}'");
		indexes.Should().HaveCount(1);
	}

	// ─── DROP INDEX ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task DropIndex()
	{
		var t = UniqName("SchDI");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX DIdx_{t} ON {t} (Name)");
		await ExecuteDdlAsync($"DROP INDEX DIdx_{t}");
		var indexes = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'DIdx_{t}'");
		indexes.Should().BeEmpty();
	}

	// ─── INFORMATION_SCHEMA ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task InformationSchema_Tables()
	{
		var t = UniqName("SchIs");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var rows = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{t}'");
		rows.Should().HaveCount(1);
		rows[0]["TABLE_NAME"].Should().Be(t);
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task InformationSchema_Columns()
	{
		var t = UniqName("SchIsc");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX), Val FLOAT64) PRIMARY KEY (Id)");
		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(3);
		rows[0]["COLUMN_NAME"].Should().Be("Id");
		rows[1]["COLUMN_NAME"].Should().Be("Name");
		rows[2]["COLUMN_NAME"].Should().Be("Val");
	}

	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task InformationSchema_Indexes()
	{
		var t = UniqName("SchIsi");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX ISIdx_{t} ON {t} (Name)");
		var rows = await QueryAsync(
			$"SELECT INDEX_NAME, INDEX_TYPE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' ORDER BY INDEX_NAME");
		rows.Should().HaveCountGreaterOrEqualTo(1);
	}

	// ─── NOT NULL constraint ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task NotNull_Constraint()
	{
		var t = UniqName("SchNN");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX) NOT NULL) PRIMARY KEY (Id)");
		var cols = await QueryAsync(
			$"SELECT COLUMN_NAME, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' ORDER BY ORDINAL_POSITION");
		cols[0]["IS_NULLABLE"].Should().Be("NO");
		cols[1]["IS_NULLABLE"].Should().Be("NO");
	}

	// ─── Interleaved tables ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task InterleavedTable_CreateAndInsert()
	{
		var parent = UniqName("SchPar");
		var child = UniqName("SchChi");
		await ExecuteDdlAsync($"CREATE TABLE {parent} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync(
			$"CREATE TABLE {child} (Id INT64 NOT NULL, ChildId INT64 NOT NULL, Val INT64) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT {parent} ON DELETE CASCADE");
		await ExecuteDmlAsync($"INSERT INTO {parent} (Id) VALUES (1)");
		await ExecuteDmlAsync($"INSERT INTO {child} (Id, ChildId, Val) VALUES (1, 1, 100), (1, 2, 200)");
		var rows = await QueryAsync($"SELECT Val FROM {child} WHERE Id = 1 ORDER BY ChildId");
		rows.Should().HaveCount(2);
	}

	// ─── CREATE TABLE with ARRAY column ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateTable_ArrayColumn()
	{
		var t = UniqName("SchArr");
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Tags ARRAY<STRING(MAX)>) PRIMARY KEY (Id)");
		var cols = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' AND COLUMN_NAME = 'Tags'");
		cols.Should().HaveCount(1);
		((string)cols[0]["SPANNER_TYPE"]!).Should().Contain("ARRAY");
	}

	// ─── Verify table visibility after DDL ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task InformationSchema_AfterAlter()
	{
		var t = UniqName("SchVis");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {t} ADD COLUMN NewCol STRING(100)");
		var cols = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t}' ORDER BY ORDINAL_POSITION");
		cols.Select(r => (string)r["COLUMN_NAME"]!).Should().Contain("NewCol");
	}

	// ─── Create index with storing clause ───
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task CreateIndex_WithStoring()
	{
		var t = UniqName("SchStore");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX SIdx_{t} ON {t} (Name) STORING (Val)");
		var indexes = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t}' AND INDEX_NAME = 'SIdx_{t}'");
		indexes.Should().HaveCount(1);
	}

	// ─── ALTER COLUMN SET OPTIONS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
	[Fact]
	[Trait(TestTraits.Category, "SchemaExhaustive")]
	public async Task AlterTable_AlterColumn_AllowCommitTimestamp()
	{
		var t = UniqName("SchOpt");
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Ts TIMESTAMP) PRIMARY KEY (Id)");
		// Enable allow_commit_timestamp
		await ExecuteDdlAsync($"ALTER TABLE {t} ALTER COLUMN Ts SET OPTIONS (allow_commit_timestamp = true)");
		// Disable allow_commit_timestamp
		await ExecuteDdlAsync($"ALTER TABLE {t} ALTER COLUMN Ts SET OPTIONS (allow_commit_timestamp = false)");
		// Verify table still exists and is functional
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Ts) VALUES (1, TIMESTAMP '2024-01-01T00:00:00Z')");
		var rows = await QueryAsync($"SELECT Id FROM {t}");
		rows.Should().HaveCount(1);
	}
}
