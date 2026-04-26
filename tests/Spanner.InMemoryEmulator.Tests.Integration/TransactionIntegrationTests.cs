using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for transaction support through the real SDK pipeline.
/// Tests: BeginTransaction, Commit, Rollback, DML within transactions,
/// and the RunInTransactionAsync retry pattern.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TransactionIntegrationTests : IntegrationTestBase
{
public TransactionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateTable(string suffix)
	{
		var table = $"T_{suffix}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Value INT64) PRIMARY KEY (Id)");
		return table;
	}

	// â”€â”€â”€ RunInTransactionAsync (retry pattern) â”€â”€â”€

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task RunInTransactionAsync_CommitsSuccessfully()
	{
		var table = await CreateTable("Commit");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Value"] = 100L });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			var cmd = transaction.CreateBatchDmlCommand();
			cmd.Add($"UPDATE {table} SET Value = 200 WHERE Id = 1");
			await cmd.ExecuteNonQueryAsync();
		});

		// Verify the update was committed
		var rows = await QueryAsync($"SELECT Value FROM {table} WHERE Id = 1");
		Convert.ToInt64(rows[0]["Value"]).Should().Be(200L);
	}

	// â”€â”€â”€ DML within transaction â”€â”€â”€

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task DmlInsert_WithinTransaction_Succeeds()
	{
		var table = await CreateTable("DmlIns");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateDmlCommand($"INSERT INTO {table} (Id, Name, Value) VALUES (1, 'Alice', 100)");
			cmd.Transaction = transaction;
			await cmd.ExecuteNonQueryAsync();
		});

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task DmlUpdate_WithinTransaction_Succeeds()
	{
		var table = await CreateTable("DmlUpd");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Value"] = 100L });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateDmlCommand($"UPDATE {table} SET Name = 'Bob' WHERE Id = 1");
			cmd.Transaction = transaction;
			await cmd.ExecuteNonQueryAsync();
		});

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task DmlDelete_WithinTransaction_Succeeds()
	{
		var table = await CreateTable("DmlDel");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateDmlCommand($"DELETE FROM {table} WHERE Id = 1");
			cmd.Transaction = transaction;
			await cmd.ExecuteNonQueryAsync();
		});

		var rows = await QueryAsync($"SELECT * FROM {table}");
		rows.Should().BeEmpty();
	}

	// â”€â”€â”€ Mutations within transaction â”€â”€â”€

	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task MutationInsert_WithinTransaction_CommitsOnSuccess()
	{
		var table = await CreateTable("MutTxn");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateInsertCommand(table);
			cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
			cmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
			cmd.Parameters.Add("Value", SpannerDbType.Int64, 42L);
			cmd.Transaction = transaction;
			await cmd.ExecuteNonQueryAsync();
		});

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	// â”€â”€â”€ Read within transaction â”€â”€â”€

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task SelectWithinTransaction_ReadsData()
	{
		var table = await CreateTable("SelTxn");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Value"] = 100L });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		string? name = null;
		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table} WHERE Id = 1");
			cmd.Transaction = transaction;
			using var reader = await cmd.ExecuteReaderAsync();
			if (await reader.ReadAsync())
			{
				name = reader.GetString(0);
			}
		});

		name.Should().Be("Alice");
	}

	// â”€â”€â”€ DML returns row count â”€â”€â”€

	[Fact]
	[Trait(TestTraits.Category, "Transaction")]
	public async Task DmlReturnsRowCount()
	{
		var table = await CreateTable("DmlCnt");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Value"] = 100L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Value"] = 200L });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		long rowCount = 0;
		await connection.RunWithRetriableTransactionAsync(async transaction =>
		{
			using var cmd = connection.CreateDmlCommand($"UPDATE {table} SET Value = 0 WHERE Value > 50");
			cmd.Transaction = transaction;
			rowCount = await cmd.ExecuteNonQueryAsync();
		});

		rowCount.Should().Be(2);
	}
}
