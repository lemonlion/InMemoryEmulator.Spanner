using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for ROW DELETION POLICY DDL support and runtime enforcement.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#row_deletion_policy
///   ROW DELETION POLICY (OLDER_THAN(column, INTERVAL n DAY))
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

	// ═══════════════════════════════════════════════════════════════
	// Runtime enforcement — expired rows not visible
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Query_ExpiredRows_NotVisible()
	{
		// Ref: https://cloud.google.com/spanner/docs/ttl/working-with-ttl#ttl_supported_column_types
		//   "Spanner considers a row expired and eligible for deletion when the timestamp value
		//    in the column is older than the current time by the specified number of days."
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 1 DAY))");

		// Insert a fresh row (should be visible)
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow } });

		// Insert an expired row (31 days ago, well past the 1-day policy)
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 2L }, { "Created", DateTime.UtcNow.AddDays(-31) } });

		var rows = await QueryAsync($"SELECT K FROM {table} ORDER BY K");
		rows.Should().HaveCount(1);
		rows[0]["K"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Query_RowJustBeforeExpiry_StillVisible()
	{
		// A row with a timestamp just a few hours before the interval boundary should still be visible
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 30 DAY))");

		// Insert a row that is 29 days old (should still be visible)
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow.AddDays(-29) } });

		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Query_NullTimestamp_NotExpired()
	{
		// A row with NULL in the deletion policy column should not be treated as expired
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 1 DAY))");

		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", null } });

		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AlterTable_DropPolicy_ExpiredRowsVisibleAgain()
	{
		// After dropping the policy, previously-expired rows should be visible again
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 1 DAY))");

		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow.AddDays(-31) } });

		// Before drop: expired row not visible
		var rowsBefore = await QueryAsync($"SELECT K FROM {table}");
		rowsBefore.Should().BeEmpty();

		// Drop the policy
		await ExecuteDdlAsync($"ALTER TABLE {table} DROP ROW DELETION POLICY");

		// After drop: row is visible again
		var rowsAfter = await QueryAsync($"SELECT K FROM {table}");
		rowsAfter.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AlterTable_ReplacePolicy_ChangesExpiry()
	{
		// Replacing the policy interval changes which rows are expired
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 1 DAY))");

		// Insert row 15 days old — expired under 1-day policy
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow.AddDays(-15) } });

		var rowsBefore = await QueryAsync($"SELECT K FROM {table}");
		rowsBefore.Should().BeEmpty();

		// Replace with 30-day policy — row is now within range
		await ExecuteDdlAsync(
			$"ALTER TABLE {table} REPLACE ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 30 DAY))");

		var rowsAfter = await QueryAsync($"SELECT K FROM {table}");
		rowsAfter.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Dml_Update_ExpiredRows_NotAffected()
	{
		// UPDATE should not affect expired rows
		var table = $"RDP_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Created TIMESTAMP, Val INT64) PRIMARY KEY (K)" +
			$", ROW DELETION POLICY (OLDER_THAN(Created, INTERVAL 1 DAY))");

		// Fresh row
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Created", DateTime.UtcNow }, { "Val", 10L } });
		// Expired row
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 2L }, { "Created", DateTime.UtcNow.AddDays(-31) }, { "Val", 20L } });

		var affected = await ExecuteDmlAsync($"UPDATE {table} SET Val = 99 WHERE TRUE");
		affected.Should().Be(1); // Only the fresh row
	}
}
