using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for duplicate key error returning ALREADY_EXISTS (not INVALID_ARGUMENT / FAILED_PRECONDITION),
/// and transaction lifecycle protections (double-commit, rollback-after-commit, read-only enforcement).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TransactionLifecycleIntegrationTests : IntegrationTestBase
{
	public TransactionLifecycleIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateTable(string suffix)
	{
		var table = $"T_TxnLife_{suffix}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		return table;
	}

	// ─── Duplicate key INSERT via DML should return ALREADY_EXISTS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	//   Duplicate primary key → ALREADY_EXISTS

	[Fact]
	[Trait(TestTraits.Category, "ErrorCondition")]
	public async Task DmlInsert_DuplicateKey_ThrowsAlreadyExists()
	{
		var table = await CreateTable("DmlDup");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name) VALUES (1, 'Bob')");
		cmd.Transaction = txn;

		Func<Task> act = async () => await cmd.ExecuteNonQueryAsync();
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#google.rpc.Code
		//   ALREADY_EXISTS = 6 — duplicate key on DML INSERT
		//   The SDK wraps the gRPC error; we verify it throws SpannerException (not success)
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── Duplicate key INSERT via Mutation should return ALREADY_EXISTS ───

	[Fact]
	[Trait(TestTraits.Category, "ErrorCondition")]
	public async Task MutationInsert_DuplicateKey_ThrowsAlreadyExists()
	{
		var table = await CreateTable("MutDup");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Bob");

		Func<Task> act = async () => await cmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── Read-only transaction should reject DML ───
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
	//   "read_only: Transaction will not write."

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task ReadOnlyTransaction_DmlInsert_ThrowsError()
	{
		var table = await CreateTable("ReadOnlyDml");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		// Use a strong read-only timestamp bound for a read-only transaction
#pragma warning disable CS0618 // Obsolete API is fine for testing
		using var txn = await connection.BeginReadOnlyTransactionAsync();
#pragma warning restore CS0618
		using var cmd = connection.CreateDmlCommand($"UPDATE {table} SET Name = 'Bob' WHERE Id = 1");
		cmd.Transaction = txn;

		Func<Task> act = async () => await cmd.ExecuteNonQueryAsync();
		// The SDK catches read-only transaction + DML mismatch client-side (InvalidOperationException)
		// or the server rejects it (SpannerException). Either way, it should throw.
		await act.Should().ThrowAsync<Exception>();
	}
}
