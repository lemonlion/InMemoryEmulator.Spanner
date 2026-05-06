using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended integration tests for INFORMATION_SCHEMA queries covering tables, columns,
/// indexes, index columns, table constraints, schema lifecycle (ALTER, DROP), and
/// interleaved tables.
/// Ref: https://cloud.google.com/spanner/docs/information-schema
/// </summary>
[Collection(IntegrationCollection.Name)]
public class InformationSchemaExtendedIntegrationTests : IntegrationTestBase
{
	public InformationSchemaExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA.TABLES
	// Ref: https://cloud.google.com/spanner/docs/information-schema#tables
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_TableAppearsAfterCreate()
	{
		var table = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{table}'");
		rows.Should().ContainSingle();
		rows[0]["TABLE_NAME"]!.ToString().Should().Be(table);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_TableTypeIsBaseTable()
	{
		var table = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{table}'");
		rows.Should().ContainSingle();
		rows[0]["TABLE_TYPE"]!.ToString().Should().Be("BASE TABLE");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_TableDisappearsAfterDrop()
	{
		var table = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"DROP TABLE {table}");

		var rows = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{table}'");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_MultipleTablesAllAppear()
	{
		var t1 = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		var t2 = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		var t3 = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t3} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('{t1}', '{t2}', '{t3}') ORDER BY TABLE_NAME");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_ParentTableNameIsNullForStandaloneTable()
	{
		var table = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT PARENT_TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{table}'");
		rows.Should().ContainSingle();
		// Standalone tables have NULL or empty parent
		var parent = rows[0]["PARENT_TABLE_NAME"];
		(parent == null || parent.ToString() == "").Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA.COLUMNS
	// Ref: https://cloud.google.com/spanner/docs/information-schema#columns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Int64()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("INT64");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Float64()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val FLOAT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("FLOAT64");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Bool()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val BOOL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("BOOL");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Date()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val DATE) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("DATE");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Timestamp()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val TIMESTAMP) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("TIMESTAMP");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Bytes()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val BYTES(256)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("BYTES(256)");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Numeric()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val NUMERIC) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("NUMERIC");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_Json()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val JSON) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("JSON");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_StringMax()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("STRING(MAX)");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_String100()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("STRING(100)");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_ArrayStringMax()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val ARRAY<STRING(MAX)>) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().ContainEquivalentOf("ARRAY");
		rows[0]["SPANNER_TYPE"]!.ToString().Should().ContainEquivalentOf("STRING");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_AllColumnTypes_ArrayInt64()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val ARRAY<INT64>) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().ContainEquivalentOf("ARRAY");
		rows[0]["SPANNER_TYPE"]!.ToString().Should().ContainEquivalentOf("INT64");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_NullableColumn_IsNullableIsYes()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["IS_NULLABLE"]!.ToString().Should().Be("YES");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_NotNullColumn_IsNullableIsNo()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Id'");
		rows.Should().ContainSingle();
		rows[0]["IS_NULLABLE"]!.ToString().Should().Be("NO");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_OrdinalPositionIsSequential()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B FLOAT64, C BOOL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(4);
		Convert.ToInt64(rows[0]["ORDINAL_POSITION"]).Should().Be(1);
		Convert.ToInt64(rows[1]["ORDINAL_POSITION"]).Should().Be(2);
		Convert.ToInt64(rows[2]["ORDINAL_POSITION"]).Should().Be(3);
		Convert.ToInt64(rows[3]["ORDINAL_POSITION"]).Should().Be(4);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Id");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("A");
		rows[2]["COLUMN_NAME"]!.ToString().Should().Be("B");
		rows[3]["COLUMN_NAME"]!.ToString().Should().Be("C");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_ManyTypes_FullTable()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($@"CREATE TABLE {table} (
			Id INT64 NOT NULL,
			ColString STRING(MAX),
			ColBool BOOL,
			ColFloat64 FLOAT64,
			ColDate DATE,
			ColTimestamp TIMESTAMP,
			ColBytes BYTES(MAX),
			ColNumeric NUMERIC,
			ColJson JSON
		) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(9);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Id");
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("INT64");
		rows[1]["SPANNER_TYPE"]!.ToString().Should().Be("STRING(MAX)");
		rows[2]["SPANNER_TYPE"]!.ToString().Should().Be("BOOL");
		rows[3]["SPANNER_TYPE"]!.ToString().Should().Be("FLOAT64");
		rows[4]["SPANNER_TYPE"]!.ToString().Should().Be("DATE");
		rows[5]["SPANNER_TYPE"]!.ToString().Should().Be("TIMESTAMP");
		rows[6]["SPANNER_TYPE"]!.ToString().Should().Contain("BYTES");
		rows[7]["SPANNER_TYPE"]!.ToString().Should().Be("NUMERIC");
		rows[8]["SPANNER_TYPE"]!.ToString().Should().Be("JSON");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_StringMaxVsString100_DifferentSpannerType()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B STRING(100)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME IN ('A', 'B') ORDER BY COLUMN_NAME");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("A");
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("STRING(MAX)");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("B");
		rows[1]["SPANNER_TYPE"]!.ToString().Should().Be("STRING(100)");
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA.INDEXES
	// Ref: https://cloud.google.com/spanner/docs/information-schema#indexes
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_PrimaryKeyIndex_Exists()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME, IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = 'PRIMARY_KEY'");
		rows.Should().ContainSingle();
		rows[0]["INDEX_NAME"]!.ToString().Should().Be("PRIMARY_KEY");
		rows[0]["IS_UNIQUE"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_SecondaryIndex_Appears()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Name";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(Name)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["INDEX_NAME"]!.ToString().Should().Be(idx);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_UniqueIndex_IsUniqueTrue()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Code";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Code STRING(50)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE UNIQUE INDEX {idx} ON {table}(Code)");

		var rows = await QueryAsync(
			$"SELECT IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["IS_UNIQUE"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_NonUniqueIndex_IsUniqueFalse()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Name";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(Name)");

		var rows = await QueryAsync(
			$"SELECT IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["IS_UNIQUE"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_NullFilteredIndex_Appears()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Val";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE NULL_FILTERED INDEX {idx} ON {table}(Val)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["INDEX_NAME"]!.ToString().Should().Be(idx);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_MultipleIndexes_AllAppear()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx1 = $"IX_{table}_A";
		var idx2 = $"IX_{table}_B";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx1} ON {table}(A)");
		await ExecuteDdlAsync($"CREATE INDEX {idx2} ON {table}(B)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' ORDER BY INDEX_NAME");
		rows.Should().HaveCountGreaterThanOrEqualTo(3); // PRIMARY_KEY + idx1 + idx2
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_IndexDisappearsAfterDrop()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Name";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(Name)");
		await ExecuteDdlAsync($"DROP INDEX {idx}");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA.INDEX_COLUMNS
	// Ref: https://cloud.google.com/spanner/docs/information-schema#index_columns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task IndexColumns_PrimaryKey_SingleColumn()
	{
		var table = $"ISIC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = 'PRIMARY_KEY'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Id");
		Convert.ToInt64(rows[0]["ORDINAL_POSITION"]).Should().Be(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task IndexColumns_CompositePrimaryKey_MultipleColumns()
	{
		var table = $"ISIC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (A INT64 NOT NULL, B INT64 NOT NULL, C STRING(MAX)) PRIMARY KEY (A, B)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = 'PRIMARY_KEY' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("A");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("B");
		Convert.ToInt64(rows[0]["ORDINAL_POSITION"]).Should().Be(1);
		Convert.ToInt64(rows[1]["ORDINAL_POSITION"]).Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task IndexColumns_SecondaryIndex_ShowsColumns()
	{
		var table = $"ISIC_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_AB";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(A, B)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("A");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task IndexColumns_DescSortOrder()
	{
		var table = $"ISIC_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Val";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(Val DESC)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, COLUMN_ORDERING FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Val");
		rows[0]["COLUMN_ORDERING"]!.ToString().Should().Be("DESC");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task IndexColumns_AscSortOrder()
	{
		var table = $"ISIC_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_Val";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(Val ASC)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, COLUMN_ORDERING FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Val");
		rows[0]["COLUMN_ORDERING"]!.ToString().Should().Be("ASC");
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA.TABLE_CONSTRAINTS
	// Ref: https://cloud.google.com/spanner/docs/information-schema#table_constraints
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task TableConstraints_PrimaryKeyConstraint_Exists()
	{
		var table = $"ISTC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = '{table}'");
		rows.Should().Contain(r => r["CONSTRAINT_TYPE"]!.ToString() == "PRIMARY KEY");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task TableConstraints_PrimaryKeyConstraint_NameIsPK()
	{
		var table = $"ISTC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT CONSTRAINT_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = '{table}' AND CONSTRAINT_TYPE = 'PRIMARY KEY'");
		rows.Should().ContainSingle();
		// PK constraint name contains "PK" or the table name
		rows[0]["CONSTRAINT_NAME"]!.ToString().Should().NotBeNullOrEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task TableConstraints_MultipleTablesEachHavePK()
	{
		var t1 = $"ISTC_{Guid.NewGuid():N}".Substring(0, 30);
		var t2 = $"ISTC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows1 = await QueryAsync(
			$"SELECT CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = '{t1}' AND CONSTRAINT_TYPE = 'PRIMARY KEY'");
		var rows2 = await QueryAsync(
			$"SELECT CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = '{t2}' AND CONSTRAINT_TYPE = 'PRIMARY KEY'");
		rows1.Should().ContainSingle();
		rows2.Should().ContainSingle();
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER TABLE ADD COLUMN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task AlterTableAddColumn_NewColumnAppearsInSchema()
	{
		var table = $"ISA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {table} ADD COLUMN Age INT64");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(3);
		rows.Select(r => r["COLUMN_NAME"]!.ToString()).Should().Contain("Age");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task AlterTableAddColumn_OrdinalPositionUpdated()
	{
		var table = $"ISA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {table} ADD COLUMN Col1 STRING(MAX)");
		await ExecuteDdlAsync($"ALTER TABLE {table} ADD COLUMN Col2 FLOAT64");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(3);
		Convert.ToInt64(rows[0]["ORDINAL_POSITION"]).Should().Be(1);
		Convert.ToInt64(rows[1]["ORDINAL_POSITION"]).Should().Be(2);
		Convert.ToInt64(rows[2]["ORDINAL_POSITION"]).Should().Be(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task AlterTableAddColumn_CorrectType()
	{
		var table = $"ISA_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {table} ADD COLUMN Score FLOAT64");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Score'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("FLOAT64");
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER TABLE DROP COLUMN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task AlterTableDropColumn_ColumnRemovedFromSchema()
	{
		var table = $"ISD_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Extra STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {table} DROP COLUMN Extra");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows.Select(r => r["COLUMN_NAME"]!.ToString()).Should().NotContain("Extra");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task AlterTableDropColumn_OrdinalPositionsStillValid()
	{
		var table = $"ISD_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B STRING(MAX), C STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"ALTER TABLE {table} DROP COLUMN B");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(3);
		rows.Select(r => r["COLUMN_NAME"]!.ToString()).Should().BeEquivalentTo(new[] { "Id", "A", "C" });
	}

	// ═══════════════════════════════════════════════════════════════
	// CREATE INDEX / DROP INDEX lifecycle
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task CreateIndex_AppearsInSchema()
	{
		var table = $"ISIX_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_N";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, N INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(N)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().ContainSingle();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task DropIndex_RemovedFromSchema()
	{
		var table = $"ISIX_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_N";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, N INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(N)");
		await ExecuteDdlAsync($"DROP INDEX {idx}");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task DropIndex_IndexColumnsAlsoRemoved()
	{
		var table = $"ISIX_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_N";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, N INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(N)");
		await ExecuteDdlAsync($"DROP INDEX {idx}");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}'");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Interleaved tables
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task InterleavedTable_ShowsParentTableName()
	{
		var parent = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		var child = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {parent} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {child} (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT {parent} ON DELETE CASCADE");

		var rows = await QueryAsync(
			$"SELECT PARENT_TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{child}'");
		rows.Should().ContainSingle();
		rows[0]["PARENT_TABLE_NAME"]!.ToString().Should().Be(parent);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task InterleavedTable_ParentHasNoParent()
	{
		var parent = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		var child = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {parent} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {child} (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT {parent} ON DELETE CASCADE");

		var rows = await QueryAsync(
			$"SELECT PARENT_TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{parent}'");
		rows.Should().ContainSingle();
		var parentVal = rows[0]["PARENT_TABLE_NAME"];
		(parentVal == null || parentVal.ToString() == "").Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task InterleavedTable_BothTablesAppearInSchema()
	{
		var parent = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		var child = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {parent} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {child} (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT {parent} ON DELETE CASCADE");

		var rows = await QueryAsync(
			$"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('{parent}', '{child}') ORDER BY TABLE_NAME");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task InterleavedTable_ChildInheritsParentPkColumn()
	{
		var parent = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		var child = $"ISP_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {parent} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {child} (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), INTERLEAVE IN PARENT {parent} ON DELETE CASCADE");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{child}' AND INDEX_NAME = 'PRIMARY_KEY' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("Id");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("ChildId");
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple tables
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task MultipleTables_ColumnsFromDifferentTablesFiltered()
	{
		var t1 = $"ISM_{Guid.NewGuid():N}".Substring(0, 30);
		var t2 = $"ISM_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL, A STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, B FLOAT64, C BOOL) PRIMARY KEY (Id)");

		var rows1 = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t1}' ORDER BY ORDINAL_POSITION");
		var rows2 = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{t2}' ORDER BY ORDINAL_POSITION");
		rows1.Should().HaveCount(2);
		rows2.Should().HaveCount(3);
		rows1.Select(r => r["COLUMN_NAME"]!.ToString()).Should().BeEquivalentTo(new[] { "Id", "A" });
		rows2.Select(r => r["COLUMN_NAME"]!.ToString()).Should().BeEquivalentTo(new[] { "Id", "B", "C" });
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task MultipleTables_IndexesFilteredByTable()
	{
		var t1 = $"ISM_{Guid.NewGuid():N}".Substring(0, 30);
		var t2 = $"ISM_{Guid.NewGuid():N}".Substring(0, 30);
		var idx1 = $"IX_{t1}_A";
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL, A STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, B INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx1} ON {t1}(A)");

		var rowsT1 = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t1}' AND INDEX_NAME != 'PRIMARY_KEY'");
		var rowsT2 = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{t2}' AND INDEX_NAME != 'PRIMARY_KEY'");
		rowsT1.Should().ContainSingle();
		rowsT2.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_NonExistentTable_ReturnsEmpty()
	{
		var rows = await QueryAsync(
			"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ThisTableDoesNotExist_XYZ_999'");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_NonExistentTable_ReturnsEmpty()
	{
		var rows = await QueryAsync(
			"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ThisTableDoesNotExist_XYZ_999'");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_NonExistentTable_ReturnsEmpty()
	{
		var rows = await QueryAsync(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'ThisTableDoesNotExist_XYZ_999'");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_BytesMaxColumn_ShowsCorrectType()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val BYTES(MAX)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("BYTES(MAX)");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_BytesWithLength_ShowsCorrectType()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val BYTES(512)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"]!.ToString().Should().Be("BYTES(512)");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_CompositeSecondaryIndex_ShowsAllColumns()
	{
		var table = $"ISIX_{Guid.NewGuid():N}".Substring(0, 30);
		var idx = $"IX_{table}_AB";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE INDEX {idx} ON {table}(A, B)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = '{table}' AND INDEX_NAME = '{idx}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"]!.ToString().Should().Be("A");
		rows[1]["COLUMN_NAME"]!.ToString().Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Tables_DropAndRecreate_ShowsNewSchema()
	{
		var table = $"IST_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, OldCol STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"DROP TABLE {table}");
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, NewCol FLOAT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows.Select(r => r["COLUMN_NAME"]!.ToString()).Should().Contain("NewCol");
		rows.Select(r => r["COLUMN_NAME"]!.ToString()).Should().NotContain("OldCol");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_NotNullNonPkColumn()
	{
		var table = $"ISC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(MAX) NOT NULL) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["IS_NULLABLE"]!.ToString().Should().Be("NO");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Indexes_PrimaryKeyAlwaysPresent()
	{
		var table = $"ISI_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = '{table}' AND INDEX_NAME = 'PRIMARY_KEY'");
		rows.Should().ContainSingle();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task TableConstraints_DropTable_ConstraintsRemoved()
	{
		var table = $"ISTC_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"DROP TABLE {table}");

		var rows = await QueryAsync(
			$"SELECT CONSTRAINT_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = '{table}'");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Generated / Default columns metadata
	// Ref: https://cloud.google.com/spanner/docs/information-schema#columns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_GeneratedColumn_IsGeneratedAlways()
	{
		var table = $"ISG_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64, Doubled INT64 AS (Val * 2) STORED) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT IS_GENERATED, GENERATION_EXPRESSION, IS_STORED FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Doubled'");
		rows.Should().ContainSingle();
		rows[0]["IS_GENERATED"]!.ToString().Should().Be("ALWAYS");
		rows[0]["GENERATION_EXPRESSION"]!.ToString().Should().NotBeNullOrEmpty();
		rows[0]["IS_STORED"]!.ToString().Should().Be("YES");
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_NonGeneratedColumn_IsGeneratedNever()
	{
		var table = $"ISG2_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT IS_GENERATED, GENERATION_EXPRESSION, IS_STORED FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
		rows[0]["IS_GENERATED"]!.ToString().Should().Be("NEVER");
		rows[0]["GENERATION_EXPRESSION"].Should().BeNull();
		rows[0]["IS_STORED"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "InformationSchemaExtended")]
	public async Task Columns_DefaultColumn_ShowsDefault()
	{
		var table = $"ISD_{Guid.NewGuid():N}".Substring(0, 30);
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Status STRING(MAX) DEFAULT ('active')) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Status'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_DEFAULT"].Should().NotBeNull();
	}
}
