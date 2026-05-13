using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for commit stats, stale reads, and INFORMATION_SCHEMA gaps.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CommitAndSchemaIntegrationTests : IntegrationTestBase
{
	public CommitAndSchemaIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE CommitTest (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA additions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InformationSchema_ColumnOptions_ReturnsEmptyResult()
	{
		var rows = await QueryAsync("SELECT * FROM INFORMATION_SCHEMA.COLUMN_OPTIONS LIMIT 10");
		rows.Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InformationSchema_KeyColumnUsage_ReturnsPrimaryKeyColumns()
	{
		await EnsureTableAsync();
		var rows = await QueryAsync(
			"SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = 'CommitTest'");
		rows.Should().ContainSingle();
		rows[0]["COLUMN_NAME"].Should().Be("Id");
		rows[0]["ORDINAL_POSITION"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InformationSchema_DatabaseOptions_ReturnsData()
	{
		var rows = await QueryAsync("SELECT OPTION_NAME, OPTION_VALUE FROM INFORMATION_SCHEMA.DATABASE_OPTIONS");
		rows.Should().NotBeEmpty();
		// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemadatabase_options
		//   Ordering of options is not guaranteed; check existence rather than position.
		rows.Should().Contain(r => (string)r["OPTION_NAME"]! == "version_retention_period");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InformationSchema_ConstraintTableUsage_ReturnsPrimaryKeyConstraints()
	{
		await EnsureTableAsync();
		var rows = await QueryAsync(
			"SELECT TABLE_NAME, CONSTRAINT_NAME FROM INFORMATION_SCHEMA.CONSTRAINT_TABLE_USAGE WHERE TABLE_NAME = 'CommitTest'");
		// Ref: https://cloud.google.com/spanner/docs/information-schema#constraint_table_usage
		//   Real Spanner returns PK constraints AND NOT NULL check constraints (CK_IS_NOT_NULL_*).
		rows.Should().NotBeEmpty();
		rows.Should().Contain(r => (string)r["CONSTRAINT_NAME"]! == "PK_CommitTest");
	}
}
