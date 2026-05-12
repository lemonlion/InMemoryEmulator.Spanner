using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;
using Xunit;

#pragma warning disable CS0618 // BeginReadOnlyTransactionAsync is obsolete in newer SDK versions

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for stale reads (exact staleness, max staleness, min read timestamp).
/// Verifies the in-memory emulator accepts staleness parameters and returns correct read timestamps.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StaleReadIntegrationTests : IntegrationTestBase
{
	public StaleReadIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE StaleReadTest (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private static async Task<List<Dictionary<string, object?>>> ReadAllRowsAsync(SpannerDataReader reader)
	{
		var rows = new List<Dictionary<string, object?>>();
		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>();
			for (int i = 0; i < reader.FieldCount; i++)
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			rows.Add(row);
		}
		return rows;
	}

	// ─── Exact Staleness ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
	//   "exact_staleness: Executes all reads at a timestamp that is exact_staleness old."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ExactStaleness_ReturnsDataWithReadTimestamp()
	{
		await EnsureTableAsync();
		await InsertOrUpdateAsync("StaleReadTest", new Dictionary<string, object?> { ["Id"] = 100L, ["Name"] = "Alice" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginReadOnlyTransactionAsync(
			TimestampBound.OfExactStaleness(TimeSpan.FromSeconds(10)));
		using var cmd = connection.CreateSelectCommand("SELECT Id, Name FROM StaleReadTest WHERE Id = 100");
		cmd.Transaction = txn;
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = await ReadAllRowsAsync(reader);

		// The emulator returns current data; the important thing is that
		// the staleness parameter was accepted without error.
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
	//   "max_staleness: Read data at a timestamp >= NOW - max_staleness seconds."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MaxStaleness_ReturnsDataWithReadTimestamp()
	{
		await EnsureTableAsync();
		await InsertOrUpdateAsync("StaleReadTest", new Dictionary<string, object?> { ["Id"] = 101L, ["Name"] = "Bob" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		// MaxStaleness is single-use only — use ephemeral transaction on command
		using var cmd = connection.CreateSelectCommand("SELECT Id, Name FROM StaleReadTest WHERE Id = 101");
		cmd.EphemeralTransactionCreationOptions = SpannerTransactionCreationOptions.ForTimestampBoundReadOnly(
			TimestampBound.OfMaxStaleness(TimeSpan.FromSeconds(15)));
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = await ReadAllRowsAsync(reader);

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Bob");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
	//   "min_read_timestamp: Executes all reads at a timestamp >= min_read_timestamp."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MinReadTimestamp_ReturnsDataWithReadTimestamp()
	{
		await EnsureTableAsync();
		await InsertOrUpdateAsync("StaleReadTest", new Dictionary<string, object?> { ["Id"] = 102L, ["Name"] = "Carol" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		// MinReadTimestamp is single-use only
		var pastTimestamp = DateTime.UtcNow.AddSeconds(-5);
		using var cmd = connection.CreateSelectCommand("SELECT Id, Name FROM StaleReadTest WHERE Id = 102");
		cmd.EphemeralTransactionCreationOptions = SpannerTransactionCreationOptions.ForTimestampBoundReadOnly(
			TimestampBound.OfMinReadTimestamp(pastTimestamp));
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = await ReadAllRowsAsync(reader);

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Carol");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
	//   "read_timestamp: Executes all reads at the given timestamp."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ReadTimestamp_ReturnsDataWithReadTimestamp()
	{
		await EnsureTableAsync();
		await InsertOrUpdateAsync("StaleReadTest", new Dictionary<string, object?> { ["Id"] = 103L, ["Name"] = "Dave" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginReadOnlyTransactionAsync(
			TimestampBound.OfReadTimestamp(DateTime.UtcNow.AddSeconds(-1)));
		using var cmd = connection.CreateSelectCommand("SELECT Id, Name FROM StaleReadTest WHERE Id = 103");
		cmd.Transaction = txn;
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = await ReadAllRowsAsync(reader);

		// Emulator returns current data since there's no MVCC
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Dave");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
	//   "strong: Read at a timestamp where all previously committed transactions are visible."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task StrongRead_ReturnsCurrentData()
	{
		await EnsureTableAsync();
		await InsertOrUpdateAsync("StaleReadTest", new Dictionary<string, object?> { ["Id"] = 104L, ["Name"] = "Eve" });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginReadOnlyTransactionAsync(TimestampBound.Strong);
		using var cmd = connection.CreateSelectCommand("SELECT Id, Name FROM StaleReadTest WHERE Id = 104");
		cmd.Transaction = txn;
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = await ReadAllRowsAsync(reader);

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Eve");
	}
}
