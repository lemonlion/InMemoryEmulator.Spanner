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
public class ReadAndTimestampIntegrationTests : IntegrationTestBase
{
public ReadAndTimestampIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── READ RPC (via CreateReadCommand) ───

	[Fact]
	public async Task Read_AllRows()
	{
		await ExecuteDdlAsync("CREATE TABLE ReadT1 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync("ReadT1", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync("ReadT1", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT2 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync("ReadT2", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync("ReadT2", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		await InsertAsync("ReadT2", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Carol" });

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT3 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		for (long i = 1; i <= 5; i++)
			await InsertAsync("ReadT3", new Dictionary<string, object?> { ["Id"] = i, ["Name"] = $"Name{i}" });

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT4 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		for (long i = 1; i <= 10; i++)
			await InsertAsync("ReadT4", new Dictionary<string, object?> { ["Id"] = i });

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT5 (Id INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (Id)");
		await InsertAsync("ReadT5", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT6 (Id INT64 NOT NULL, CreatedAt TIMESTAMP OPTIONS (allow_commit_timestamp = true)) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE ReadT7 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateReadCommand("ReadT7",
			ReadOptions.FromColumns("Id"),
			KeySet.All);
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeFalse();
	}

	// ─── READ with PK column not first in column list ───
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Read
	//   Key values in the KeySet correspond to the primary key columns (not by column position in the schema).

	[Fact]
	public async Task Read_SpecificKey_WhenPkIsNotFirstColumn()
	{
		await ExecuteDdlAsync("CREATE TABLE ReadT8 (Name STRING(MAX), Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync("ReadT8", new Dictionary<string, object?> { ["Id"] = 42L, ["Name"] = "Alice" });
		await InsertAsync("ReadT8", new Dictionary<string, object?> { ["Id"] = 99L, ["Name"] = "Bob" });

		using var conn = Fixture.CreateConnection();
		var keySet = KeySet.FromKeys(new Key(42L));
		using var cmd = conn.CreateReadCommand("ReadT8",
			ReadOptions.FromColumns("Name"),
			keySet);
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("Alice");
		(await reader.ReadAsync()).Should().BeFalse();
	}

	[Fact]
	public async Task Read_KeyRange_WhenPkIsNotFirstColumn()
	{
		await ExecuteDdlAsync("CREATE TABLE ReadT9 (Label STRING(MAX), Seq INT64 NOT NULL) PRIMARY KEY (Seq)");
		for (long i = 1; i <= 5; i++)
			await InsertAsync("ReadT9", new Dictionary<string, object?> { ["Seq"] = i, ["Label"] = $"L{i}" });

		using var conn = Fixture.CreateConnection();
		var keySet = KeySet.FromRanges(KeyRange.ClosedClosed(new Key(2L), new Key(4L)));
		using var cmd = conn.CreateReadCommand("ReadT9",
			ReadOptions.FromColumns("Seq", "Label"),
			keySet);
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<(long Seq, string Label)>();
		while (await reader.ReadAsync())
			results.Add((reader.GetInt64(0), reader.GetString(1)));
		results.Should().BeEquivalentTo(new[] { (2L, "L2"), (3L, "L3"), (4L, "L4") });
	}
}
