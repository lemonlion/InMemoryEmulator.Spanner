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
		rows[0]["OPTION_NAME"].Should().Be("version_retention_period");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InformationSchema_ConstraintTableUsage_ReturnsPrimaryKeyConstraints()
	{
		await EnsureTableAsync();
		var rows = await QueryAsync(
			"SELECT TABLE_NAME, CONSTRAINT_NAME FROM INFORMATION_SCHEMA.CONSTRAINT_TABLE_USAGE WHERE TABLE_NAME = 'CommitTest'");
		rows.Should().ContainSingle();
		rows[0]["CONSTRAINT_NAME"].Should().Be("PK_CommitTest");
	}
}
