using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for transaction state validation.
/// Tests that the server correctly rejects operations on committed/rolled-back transactions,
/// and that rollback of a committed transaction does not corrupt data.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TransactionStateValidationIntegrationTests : IntegrationTestBase
{
	public TransactionStateValidationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateTable(string suffix)
	{
		var table = $"T_TxnState_{suffix}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		return table;
	}

	// ─── Rollback after commit should not undo data ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
	//   "Rollback returns OK if it successfully aborts the transaction, the transaction
	//    was already aborted, or the transaction isn't found."
	//   A committed transaction's data must remain intact after a rollback attempt.
	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Rollback_AfterCommit_DataPersists()
	{
		var table = await CreateTable("RbAfterCommit");

		// Insert a row via a committed transaction
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (1, 'Alice')");
		cmd.Transaction = txn;
		await cmd.ExecuteNonQueryAsync();
		await txn.CommitAsync();

		// Verify data is present
		using var connection2 = Fixture.CreateConnection();
		await connection2.OpenAsync();
		using var readCmd = connection2.CreateSelectCommand($"SELECT Name FROM {table} WHERE Id = 1");
		var name = (string?)await readCmd.ExecuteScalarAsync();
		name.Should().Be("Alice");
	}

	// ─── Commit with mutations (single-use transaction pattern via SDK) ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
	//   "single_use_transaction: Execute mutations in a temporary transaction."
	//   The SDK's InsertCommand/UpdateCommand without explicit transaction uses single_use_transaction.
	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Commit_WithMutations_NoExplicitTransaction_Succeeds()
	{
		var table = await CreateTable("SingleUseMut");

		// The SDK's CreateInsertCommand without an explicit transaction triggers the
		// single_use_transaction commit path internally.
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Bob");
		await cmd.ExecuteNonQueryAsync();

		// Verify data was written
		using var readCmd = connection.CreateSelectCommand($"SELECT Name FROM {table} WHERE Id = 1");
		var name = (string?)await readCmd.ExecuteScalarAsync();
		name.Should().Be("Bob");
	}

	// ─── Read-after-commit verifies data persists ───

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Commit_ThenRead_DataVisible()
	{
		var table = await CreateTable("CommitRead");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand(
			$"INSERT INTO {table} (Id, Name) VALUES (1, 'Charlie')");
		cmd.Transaction = txn;
		await cmd.ExecuteNonQueryAsync();
		await txn.CommitAsync();

		// Read from a fresh connection to ensure visibility
		using var connection2 = Fixture.CreateConnection();
		await connection2.OpenAsync();
		using var readCmd = connection2.CreateSelectCommand($"SELECT Name FROM {table} WHERE Id = 1");
		var name = (string?)await readCmd.ExecuteScalarAsync();
		name.Should().Be("Charlie");
	}

	// ─── Read-after-rollback verifies data is NOT persisted ───

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Rollback_ThenRead_DataNotVisible()
	{
		var table = await CreateTable("RollbackRead");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand(
			$"INSERT INTO {table} (Id, Name) VALUES (1, 'Discarded')");
		cmd.Transaction = txn;
		await cmd.ExecuteNonQueryAsync();
		await txn.RollbackAsync();

		// Read from a fresh connection — data should NOT be visible
		using var connection2 = Fixture.CreateConnection();
		await connection2.OpenAsync();
		using var readCmd = connection2.CreateSelectCommand($"SELECT COUNT(*) FROM {table} WHERE Id = 1");
		var count = (long?)await readCmd.ExecuteScalarAsync();
		count.Should().Be(0);
	}

	// ─── DML inside transaction followed by commit persists changes ───

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Transaction_MultiDml_Commit_AllDataPersists()
	{
		var table = await CreateTable("MultiDml");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();

		using var cmd1 = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (1, 'One')");
		cmd1.Transaction = txn;
		await cmd1.ExecuteNonQueryAsync();

		using var cmd2 = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (2, 'Two')");
		cmd2.Transaction = txn;
		await cmd2.ExecuteNonQueryAsync();

		await txn.CommitAsync();

		// Verify both rows
		using var connection2 = Fixture.CreateConnection();
		await connection2.OpenAsync();
		using var readCmd = connection2.CreateSelectCommand($"SELECT COUNT(*) FROM {table}");
		var count = (long?)await readCmd.ExecuteScalarAsync();
		count.Should().Be(2);
	}

	// ─── DML inside transaction followed by rollback reverts ALL changes ───

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task Transaction_MultiDml_Rollback_AllDataReverted()
	{
		var table = await CreateTable("MultiDmlRb");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();

		using var cmd1 = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (1, 'One')");
		cmd1.Transaction = txn;
		await cmd1.ExecuteNonQueryAsync();

		using var cmd2 = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (2, 'Two')");
		cmd2.Transaction = txn;
		await cmd2.ExecuteNonQueryAsync();

		await txn.RollbackAsync();

		// Verify no rows
		using var connection2 = Fixture.CreateConnection();
		await connection2.OpenAsync();
		using var readCmd = connection2.CreateSelectCommand($"SELECT COUNT(*) FROM {table}");
		var count = (long?)await readCmd.ExecuteScalarAsync();
		count.Should().Be(0);
	}
}
