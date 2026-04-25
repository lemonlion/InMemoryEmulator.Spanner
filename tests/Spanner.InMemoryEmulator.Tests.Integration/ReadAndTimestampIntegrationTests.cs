using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using KeySet = Google.Cloud.Spanner.Data.KeySet;
using KeyRange = Google.Cloud.Spanner.Data.KeyRange;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 14: Read/StreamingRead RPCs, Commit Timestamps.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ReadAndTimestampIntegrationTests
{
	private readonly ITestDatabaseFixture _fixture;

	public ReadAndTimestampIntegrationTests(EmulatorSession session)
	{
		_fixture = TestFixtureFactory.Create(session);
	}

	// ─── READ RPC (via CreateReadCommand) ───

	[Fact]
	public async Task Read_AllRows()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT1 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("ReadT1", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		db.Insert("ReadT1", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateReadCommand("ReadT1",
			ReadOptions.FromColumns("Id", "Name"),
			KeySet.All);
		using var reader = await cmd.ExecuteReaderAsync();
		var names = new List<string>();
		while (await reader.ReadAsync())
			names.Add(reader.GetString(reader.GetOrdinal("Name")));
		names.Should().HaveCount(2);
	}

	[Fact]
	public async Task Read_SpecificKeys()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT2 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("ReadT2", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		db.Insert("ReadT2", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		db.Insert("ReadT2", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Carol" });

		using var conn = _fixture.CreateConnection();
		var keySet = KeySet.FromKeys(new Key(1L), new Key(3L));
		using var cmd = conn.CreateReadCommand("ReadT2",
			ReadOptions.FromColumns("Name"),
			keySet);
		using var reader = await cmd.ExecuteReaderAsync();
		var names = new List<string>();
		while (await reader.ReadAsync())
			names.Add(reader.GetString(0));
		names.Should().BeEquivalentTo("Alice", "Carol");
	}

	[Fact]
	public async Task Read_KeyRange()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT3 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		for (long i = 1; i <= 5; i++)
			db.Insert("ReadT3", new Dictionary<string, object?> { ["Id"] = i, ["Name"] = $"Name{i}" });

		using var conn = _fixture.CreateConnection();
		var keySet = KeySet.FromRanges(KeyRange.ClosedClosed(new Key(2L), new Key(4L)));
		using var cmd = conn.CreateReadCommand("ReadT3",
			ReadOptions.FromColumns("Id"),
			keySet);
		using var reader = await cmd.ExecuteReaderAsync();
		var ids = new List<long>();
		while (await reader.ReadAsync())
			ids.Add(reader.GetInt64(0));
		ids.Should().BeEquivalentTo(new[] { 2L, 3L, 4L });
	}

	[Fact]
	public async Task Read_WithLimit()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT4 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		for (long i = 1; i <= 10; i++)
			db.Insert("ReadT4", new Dictionary<string, object?> { ["Id"] = i });

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateReadCommand("ReadT4",
			ReadOptions.FromColumns("Id"),
			KeySet.All);
		using var reader = await cmd.ExecuteReaderAsync();
		var ids = new List<long>();
		while (await reader.ReadAsync())
			ids.Add(reader.GetInt64(0));
		ids.Should().HaveCount(10); // All rows returned without limit
	}

	[Fact]
	public async Task Read_ColumnProjection()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT5 (Id INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (Id)");
		db.Insert("ReadT5", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateReadCommand("ReadT5",
			ReadOptions.FromColumns("Name"),
			KeySet.All);
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.FieldCount.Should().Be(1);
		reader.GetString(0).Should().Be("Alice");
	}

	// ─── COMMIT TIMESTAMPS ───

	[Fact]
	public async Task CommitTimestamp_InsertedViaSpannerMutation()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT6 (Id INT64 NOT NULL, CreatedAt TIMESTAMP OPTIONS (allow_commit_timestamp = true)) PRIMARY KEY (Id)");

		using var conn = _fixture.CreateConnection();
		var insertCmd = conn.CreateInsertCommand("ReadT6");
		insertCmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		insertCmd.Parameters.Add("CreatedAt", SpannerDbType.Timestamp, SpannerParameter.CommitTimestamp);
		await insertCmd.ExecuteNonQueryAsync();

		using var selectCmd = conn.CreateSelectCommand("SELECT CreatedAt FROM ReadT6 WHERE Id = 1");
		using var reader = await selectCmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		var createdAt = reader.GetDateTime(0);
		createdAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
	}

	// ─── EMPTY READ ───

	[Fact]
	public async Task Read_EmptyTable_ReturnsNoRows()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE ReadT7 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateReadCommand("ReadT7",
			ReadOptions.FromColumns("Id"),
			KeySet.All);
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeFalse();
	}
}
