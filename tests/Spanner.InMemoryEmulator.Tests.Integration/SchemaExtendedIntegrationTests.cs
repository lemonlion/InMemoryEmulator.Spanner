using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Edge-case tests for schema operations: DDL statements, INFORMATION_SCHEMA queries,
/// ALTER TABLE, CREATE/DROP INDEX, and schema introspection.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
/// Ref: https://cloud.google.com/spanner/docs/information-schema
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SchemaExtendedIntegrationTests : IntegrationTestBase
{
	public SchemaExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// CREATE TABLE with various column types
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_AllScalarTypes()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE SchAllTypes (
				Id INT64 NOT NULL,
				ColString STRING(100),
				ColBool BOOL,
				ColInt64 INT64,
				ColFloat64 FLOAT64,
				ColDate DATE,
				ColTimestamp TIMESTAMP,
				ColBytes BYTES(256)
			) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchAllTypes' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(8);
		rows[0]["COLUMN_NAME"].Should().Be("Id");
		rows[1]["COLUMN_NAME"].Should().Be("ColString");
	}

	[Fact]
	public async Task CreateTable_WithArrayColumn()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchArrayCol (Id INT64 NOT NULL, Tags ARRAY<STRING(50)>) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchArrayCol' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
	}

	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Fact]
	public async Task CreateTable_CompositePrimaryKey()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchCompositePk (A INT64 NOT NULL, B INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (A, B)");
		}
		catch { }

		// Verify PK columns
		var rows = await QueryAsync(@"
			SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS
			WHERE TABLE_NAME = 'SchCompositePk' AND INDEX_NAME = 'PRIMARY_KEY'
			ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"].Should().Be("A");
		rows[1]["COLUMN_NAME"].Should().Be("B");
	}

	[Fact]
	public async Task CreateTable_StringMaxLength()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchMaxStr (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchMaxStr' AND COLUMN_NAME = 'Val'");
		rows.Should().ContainSingle();
	}

	// ═══════════════════════════════════════════════════════════════
	// CREATE INDEX / DROP INDEX
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_index
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateIndex_Basic()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchIndexTest (Id INT64 NOT NULL, Name STRING(100), Val INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE INDEX IX_SchIndexTest_Name ON SchIndexTest(Name)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'SchIndexTest' AND INDEX_NAME = 'IX_SchIndexTest_Name'");
		rows.Should().ContainSingle();
	}

	[Fact]
	public async Task CreateIndex_Unique()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchUniqueIdx (Id INT64 NOT NULL, Code STRING(50)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE UNIQUE INDEX IX_SchUniqueIdx_Code ON SchUniqueIdx(Code)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT IS_UNIQUE FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'SchUniqueIdx' AND INDEX_NAME = 'IX_SchUniqueIdx_Code'");
		rows.Should().ContainSingle();
		rows[0]["IS_UNIQUE"].Should().Be(true);
	}

	[Fact]
	public async Task CreateIndex_NullFiltered()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchNullFilIdx (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE NULL_FILTERED INDEX IX_SchNullFilIdx_Val ON SchNullFilIdx(Val)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'SchNullFilIdx' AND INDEX_NAME = 'IX_SchNullFilIdx_Val'");
		rows.Should().ContainSingle();
	}

	[Fact]
	public async Task CreateIndex_CompositeIndex()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchCompIdx (Id INT64 NOT NULL, A STRING(50), B INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE INDEX IX_SchCompIdx_AB ON SchCompIdx(A, B)");
		}
		catch { }

		var rows = await QueryAsync(@"
			SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS
			WHERE TABLE_NAME = 'SchCompIdx' AND INDEX_NAME = 'IX_SchCompIdx_AB'
			ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
		rows[0]["COLUMN_NAME"].Should().Be("A");
		rows[1]["COLUMN_NAME"].Should().Be("B");
	}

	[Fact]
	public async Task DropIndex()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchDropIdx (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE INDEX IX_SchDropIdx_Val ON SchDropIdx(Val)");
		}
		catch { }

		await ExecuteDdlAsync("DROP INDEX IX_SchDropIdx_Val");

		var rows = await QueryAsync(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'SchDropIdx' AND INDEX_NAME = 'IX_SchDropIdx_Val'");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER TABLE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AlterTable_AddColumn()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchAlterAdd (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDdlAsync("ALTER TABLE SchAlterAdd ADD COLUMN NewCol INT64");

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchAlterAdd' ORDER BY ORDINAL_POSITION");
		rows.Select(r => r["COLUMN_NAME"]).Should().Contain("NewCol");
	}

	[Fact]
	public async Task AlterTable_DropColumn()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchAlterDrop (Id INT64 NOT NULL, ToBeDropped STRING(100), Keep STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDdlAsync("ALTER TABLE SchAlterDrop DROP COLUMN ToBeDropped");

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchAlterDrop'");
		rows.Select(r => r["COLUMN_NAME"]).Should().NotContain("ToBeDropped");
		rows.Select(r => r["COLUMN_NAME"]).Should().Contain("Keep");
	}

	// ═══════════════════════════════════════════════════════════════
	// DROP TABLE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#drop_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DropTable_ThenGone()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchDropMe (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDdlAsync("DROP TABLE SchDropMe");

		var rows = await QueryAsync(
			"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchDropMe'");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA queries
	// Ref: https://cloud.google.com/spanner/docs/information-schema
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InformationSchema_Tables_ContainsCreatedTable()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchInfoTbl (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchInfoTbl'");
		rows.Should().ContainSingle().Which["TABLE_NAME"].Should().Be("SchInfoTbl");
	}

	[Fact]
	public async Task InformationSchema_Columns_ForTable()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchInfoCol (Id INT64 NOT NULL, Name STRING(100), Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME, SPANNER_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SchInfoCol' ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(3);
		rows[0]["COLUMN_NAME"].Should().Be("Id");
		rows[0]["IS_NULLABLE"].Should().Be("NO");
		rows[1]["COLUMN_NAME"].Should().Be("Name");
		rows[1]["IS_NULLABLE"].Should().Be("YES");
	}

	[Fact]
	public async Task InformationSchema_Indexes_ForTable()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchInfoIdx (Id INT64 NOT NULL, Code STRING(50)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE INDEX IX_SchInfoIdx_Code ON SchInfoIdx(Code)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'SchInfoIdx' ORDER BY INDEX_NAME");
		rows.Select(r => r["INDEX_NAME"]).Should().Contain("IX_SchInfoIdx_Code");
	}

	[Fact]
	public async Task InformationSchema_IndexColumns_ForIndex()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchInfoIdxCol (Id INT64 NOT NULL, A STRING(50), B INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE INDEX IX_SchInfoIdxCol_AB ON SchInfoIdxCol(A, B)");
		}
		catch { }

		var rows = await QueryAsync(@"
			SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.INDEX_COLUMNS
			WHERE TABLE_NAME = 'SchInfoIdxCol' AND INDEX_NAME = 'IX_SchInfoIdxCol_AB'
			ORDER BY ORDINAL_POSITION");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// CHECK constraints via DDL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_WithCheckConstraint()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE SchCheck (
				Id INT64 NOT NULL,
				Age INT64,
				CONSTRAINT CK_Age CHECK (Age >= 0 AND Age <= 150)
			) PRIMARY KEY (Id)");
		}
		catch { }

		// Valid insert
		await InsertAsync("SchCheck", new Dictionary<string, object?> { ["Id"] = 1L, ["Age"] = 25L });
		var rows = await QueryAsync("SELECT Age FROM SchCheck WHERE Id = 1");
		rows[0]["Age"].Should().Be(25L);
	}

	[Fact]
	public async Task CreateTable_WithCheckConstraint_ViolationThrows()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE SchCheckViolation (
				Id INT64 NOT NULL,
				Val INT64,
				CONSTRAINT CK_Pos CHECK (Val > 0)
			) PRIMARY KEY (Id)");
		}
		catch { }

		var act = () => ExecuteDmlAsync(
			"INSERT INTO SchCheckViolation (Id, Val) VALUES (1, -1)");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// FOREIGN KEY constraints via DDL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_WithForeignKey()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchFkParent (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(@"CREATE TABLE SchFkChild (
				Id INT64 NOT NULL,
				ParentId INT64 NOT NULL,
				CONSTRAINT FK_SchFk FOREIGN KEY (ParentId) REFERENCES SchFkParent(Id)
			) PRIMARY KEY (Id)");
		}
		catch { }

		// Valid: insert parent then child
		await InsertAsync("SchFkParent", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "P1" });
		await InsertAsync("SchFkChild", new Dictionary<string, object?> { ["Id"] = 1L, ["ParentId"] = 1L });

		var rows = await QueryAsync("SELECT ParentId FROM SchFkChild WHERE Id = 1");
		rows[0]["ParentId"].Should().Be(1L);
	}

	[Fact]
	public async Task CreateTable_WithForeignKey_Violation()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchFkParent2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(@"CREATE TABLE SchFkChild2 (
				Id INT64 NOT NULL,
				ParentId INT64 NOT NULL,
				CONSTRAINT FK_SchFk2 FOREIGN KEY (ParentId) REFERENCES SchFkParent2(Id)
			) PRIMARY KEY (Id)");
		}
		catch { }

		var act = () => InsertAsync("SchFkChild2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["ParentId"] = 999L });
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// DDL with multiple statements
	// ═══════════════════════════════════════════════════════════════

	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Fact]
	public async Task DdlBatch_MultipleStatements()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchBatchA (Id INT64 NOT NULL) PRIMARY KEY (Id)",
				"CREATE TABLE SchBatchB (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		var rows = await QueryAsync(
			"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('SchBatchA', 'SchBatchB') ORDER BY TABLE_NAME");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// Table with interleaving (parent-child tables)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_Interleaved()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SchInterleavedParent (ParentId INT64 NOT NULL) PRIMARY KEY (ParentId)");
			await ExecuteDdlAsync(
				"CREATE TABLE SchInterleavedChild (ParentId INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (ParentId, ChildId), INTERLEAVE IN PARENT SchInterleavedParent ON DELETE CASCADE");
		}
		catch { }

		await InsertAsync("SchInterleavedParent", new Dictionary<string, object?> { ["ParentId"] = 1L });
		await InsertAsync("SchInterleavedChild", new Dictionary<string, object?> { ["ParentId"] = 1L, ["ChildId"] = 1L });
		await InsertAsync("SchInterleavedChild", new Dictionary<string, object?> { ["ParentId"] = 1L, ["ChildId"] = 2L });

		var rows = await QueryAsync("SELECT ChildId FROM SchInterleavedChild WHERE ParentId = 1 ORDER BY ChildId");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER TABLE ADD COLUMN — querying existing rows after schema change
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   "Existing rows have NULL for the new column."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AlterTable_AddColumn_ExistingRows_ReturnNull()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE SchAddColExist (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync("SchAddColExist", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync("SchAddColExist", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		// Add a new column after data exists
		await ExecuteDdlAsync("ALTER TABLE SchAddColExist ADD COLUMN Score FLOAT64");

		// Query the new column — should return NULL for all existing rows
		var rows = await QueryAsync("SELECT Id, Score FROM SchAddColExist ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Score"].Should().BeNull();
		rows[1]["Score"].Should().BeNull();
	}

	[Fact]
	public async Task AlterTable_AddColumn_WhereIsNull()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE SchAddColNull (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync("SchAddColNull", new Dictionary<string, object?> { ["Id"] = 1L });

		// Add a new column after data exists
		await ExecuteDdlAsync("ALTER TABLE SchAddColNull ADD COLUMN Tag STRING(MAX)");

		// WHERE clause on the new column should treat it as NULL
		var rows = await QueryAsync("SELECT Id FROM SchAddColNull WHERE Tag IS NULL");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(1L);
	}
}
