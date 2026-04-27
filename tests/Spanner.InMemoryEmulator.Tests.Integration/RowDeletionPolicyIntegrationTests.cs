using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for ROW DELETION POLICY DDL support.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
///   ROW DELETION POLICY (OLDER_THAN(column, INTERVAL n DAY))
///   Parsed but not enforced at runtime — DDL compatibility only.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class RowDeletionPolicyIntegrationTests : IntegrationTestBase
{
	public RowDeletionPolicyIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// CREATE TABLE with ROW DELETION POLICY
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_WithRowDeletionPolicy_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
		//   "ROW DELETION POLICY (OLDER_THAN(column_name, INTERVAL num_days DAY))"
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 30 DAY))");

		// Verify table was created and is usable
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	public async Task CreateTable_WithRowDeletionPolicy_1Day_Succeeds()
	{
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, ExpiresAt TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(ExpiresAt, INTERVAL 1 DAY))");

		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "ExpiresAt", DateTime.UtcNow } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER TABLE — ADD / REPLACE / DROP ROW DELETION POLICY
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AlterTable_AddRowDeletionPolicy_Succeeds()
	{
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)");

		await ExecuteDdlAsync(
			$"ALTER TABLE {table} ADD ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 7 DAY))");

		// Table should still be usable
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	public async Task AlterTable_ReplaceRowDeletionPolicy_Succeeds()
	{
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 30 DAY))");

		await ExecuteDdlAsync(
			$"ALTER TABLE {table} REPLACE ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 90 DAY))");

		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	public async Task AlterTable_DropRowDeletionPolicy_Succeeds()
	{
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 30 DAY))");

		await ExecuteDdlAsync(
			$"ALTER TABLE {table} DROP ROW DELETION POLICY");

		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}
}
