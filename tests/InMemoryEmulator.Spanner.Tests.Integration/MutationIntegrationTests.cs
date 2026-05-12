using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for mutations flowing through the full gRPC pipeline:
/// SpannerConnection → gRPC → FakeSpannerService → Commit → MutationExecutor
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MutationIntegrationTests : IntegrationTestBase
{
public MutationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateTable(string suffix)
	{
		var table = $"M_{suffix}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		return table;
	}

	// ─── INSERT ───

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task InsertCommand_InsertsRow()
	{
		var table = await CreateTable("Insert");

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
		cmd.Parameters.Add("Age", SpannerDbType.Int64, 30L);

		var rowsAffected = await cmd.ExecuteNonQueryAsync();

		// Verify the row was inserted
		var rows = await QueryAsync($"SELECT Name, Age FROM {table} WHERE SingerId = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task InsertDuplicateKey_ThrowsException()
	{
		var table = await CreateTable("InsertDup");
		await InsertAsync(table, new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Bob");

		var act = () => cmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── UPDATE ───

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task UpdateCommand_UpdatesExistingRow()
	{
		var table = await CreateTable("Update");
		await InsertAsync(table, new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateUpdateCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Updated Alice");

		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Updated Alice");
	}

	// ─── DELETE ───

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task DeleteCommand_RemovesRow()
	{
		var table = await CreateTable("Delete");
		await InsertAsync(table, new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateDeleteCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);

		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT * FROM {table}");
		rows.Should().BeEmpty();
	}

	// ─── INSERT OR UPDATE (Upsert) ───

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task InsertOrUpdateCommand_InsertsNewRow()
	{
		var table = await CreateTable("Upsert1");

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateInsertOrUpdateCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
		cmd.Parameters.Add("Age", SpannerDbType.Int64, 30L);

		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE SingerId = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task InsertOrUpdateCommand_UpdatesExistingRow()
	{
		var table = await CreateTable("Upsert2");
		await InsertAsync(table, new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateInsertOrUpdateCommand(table);
		cmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Bob");

		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name FROM {table} WHERE SingerId = 1");
		rows[0]["Name"].Should().Be("Bob");
	}

	// ─── Multiple mutations in single commit ───

	[Fact]
	[Trait(TestTraits.Category, "Mutation")]
	public async Task InsertThenQuery_EndToEndPipeline()
	{
		var table = await CreateTable("E2E");

		// Insert via SDK mutation
		using var connection = Fixture.CreateConnection();
		using var insertCmd = connection.CreateInsertCommand(table);
		insertCmd.Parameters.Add("SingerId", SpannerDbType.Int64, 1L);
		insertCmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
		insertCmd.Parameters.Add("Age", SpannerDbType.Int64, 30L);
		await insertCmd.ExecuteNonQueryAsync();

		// Query via SDK
		using var queryCmd = connection.CreateSelectCommand($"SELECT Name, Age FROM {table} WHERE SingerId = 1");
		using var reader = await queryCmd.ExecuteReaderAsync();
		await reader.ReadAsync();

		reader.GetString(reader.GetOrdinal("Name")).Should().Be("Alice");
		reader.GetInt64(reader.GetOrdinal("Age")).Should().Be(30);
	}
}
