using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Grpc.Core;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for observability features (Phase 16):
/// fault injection, request logging, SQL logging, state persistence.
/// These tests exercise the features through the full gRPC pipeline.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ObservabilityIntegrationTests
{
	private readonly ITestDatabaseFixture _fixture;

	public ObservabilityIntegrationTests(EmulatorSession session)
	{
		_fixture = TestFixtureFactory.Create(session);
	}

	private FakeSpannerService Service => _fixture.Server!.Service;

	private string CreateTable(string suffix)
	{
		var table = $"Obs_{suffix}";
		_fixture.Database!.ExecuteDdl(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		return table;
	}

	// ─── Fault Injection ───

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public async Task FaultInjector_ForQuery_CausesRpcException()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
		//   All RPC methods can return standard gRPC error codes.
		//   PermissionDenied is non-retriable, so the SDK surfaces it immediately.
		var table = CreateTable("FaultQuery");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		// Open connection first (creates sessions), then inject faults on queries only.
		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		Service.FaultInjector = FaultInjector.ForMethod("ExecuteStreamingSql", StatusCode.PermissionDenied, "query denied");
		try
		{
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			// The SDK may not throw on ExecuteReaderAsync() for streaming calls;
			// the exception surfaces when reading the stream.
			Func<Task> act = async () =>
			{
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync()) { }
			};
			await act.Should().ThrowAsync<SpannerException>();
		}
		finally
		{
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public async Task FaultInjector_ForCommit_DoesNotAffectQueries()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
		//   Faults can target specific RPCs.
		var table = CreateTable("FaultCommitOnly");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		Service.FaultInjector = FaultInjector.ForMethod("Commit", StatusCode.PermissionDenied, "commit denied");
		try
		{
			// Query should succeed (Commit not involved in reads)
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader = await cmd.ExecuteReaderAsync();
			(await reader.ReadAsync()).Should().BeTrue();
			reader.GetString(reader.GetOrdinal("Name")).Should().Be("Alice");
		}
		finally
		{
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public async Task FaultInjector_PermissionDenied_CommitFails()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
		//   PERMISSION_DENIED is non-retriable: surfaces immediately.
		var table = CreateTable("FaultPD");

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		Service.FaultInjector = FaultInjector.ForMethod("Commit", StatusCode.PermissionDenied, "no permission");
		try
		{
			using var cmd = connection.CreateInsertCommand(table);
			cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
			cmd.Parameters.Add("Name", SpannerDbType.String, "Test");

			var act = () => cmd.ExecuteNonQueryAsync();
			await act.Should().ThrowAsync<SpannerException>();
		}
		finally
		{
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public async Task FaultInjector_InvalidArgument_CausesError()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
		//   INVALID_ARGUMENT is non-retriable: surfaces immediately.
		var table = CreateTable("FaultInvArg");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		Service.FaultInjector = FaultInjector.ForMethod("ExecuteStreamingSql", StatusCode.InvalidArgument, "bad query");
		try
		{
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			Func<Task> act = async () =>
			{
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync()) { }
			};
			await act.Should().ThrowAsync<SpannerException>();
		}
		finally
		{
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public async Task FaultInjector_NTimes_FirstCallFails_SecondSucceeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
		//   PermissionDenied once, then allow through. Non-retriable ensures deterministic behaviour.
		var table = CreateTable("FaultNTimes");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		// Fail only the first streaming read call (non-retriable), then succeed
		Service.FaultInjector = FaultInjector.ForMethodNTimes("ExecuteStreamingSql", 1, StatusCode.PermissionDenied);
		try
		{
			// First call should fail
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			Func<Task> act = async () =>
			{
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync()) { }
			};
			await act.Should().ThrowAsync<SpannerException>();

			// Second call should succeed
			using var cmd2 = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader2 = await cmd2.ExecuteReaderAsync();
			(await reader2.ReadAsync()).Should().BeTrue();
			reader2.GetString(reader2.GetOrdinal("Name")).Should().Be("Alice");
		}
		finally
		{
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "FaultInjection")]
	public void FaultInjector_CanBeCleared()
	{
		Service.FaultInjector = FaultInjector.Always(StatusCode.Unavailable);
		Service.FaultInjector = null;

		Service.FaultInjector.Should().BeNull();
	}

	// ─── Request Log ───

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "RequestLog")]
	public async Task RequestLog_CapturesQueryRequests()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteStreamingSql
		//   Each RPC call is logged with method name and timestamp.
		var table = CreateTable("LogQuery");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		Service.ClearLogs();

		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync()) { }

		Service.RequestLog.Should().Contain(entry =>
			entry.MethodName == "ExecuteStreamingSql");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "RequestLog")]
	public async Task RequestLog_CapturesMutationRequests()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
		//   Commit calls are logged.
		var table = CreateTable("LogMutate");

		Service.ClearLogs();

		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
		await cmd.ExecuteNonQueryAsync();

		Service.RequestLog.Should().Contain(entry =>
			entry.MethodName == "Commit");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "RequestLog")]
	public async Task RequestLog_CapturesSessionCreation()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchCreateSessions
		//   Session creation is the first RPC call from the SDK.
		//   Note: We don't ClearLogs because the SDK may reuse cached sessions
		//   and not create new ones for this test.

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();

		using var cmd = connection.CreateSelectCommand("SELECT 1");
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync()) { }

		// The session creation should have been logged at some point during the server's lifetime
		Service.RequestLog.Should().Contain(entry =>
			entry.MethodName == "BatchCreateSessions" || entry.MethodName == "CreateSession");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "RequestLog")]
	public void RequestLog_ClearLogs_ResetsLogs()
	{
		Service.ClearLogs();
		Service.RequestLog.Should().BeEmpty();
		Service.SqlLog.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "RequestLog")]
	public async Task RequestLog_EntriesHaveTimestamps()
	{
		Service.ClearLogs();
		var beforeUtc = DateTimeOffset.UtcNow;

		var table = CreateTable("LogTs");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync()) { }

		Service.RequestLog.Should().NotBeEmpty();
		Service.RequestLog.Should().Contain(entry =>
			entry.Timestamp >= beforeUtc);
	}

	// ─── SQL Log ───

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "SqlLog")]
	public async Task SqlLog_CapturesSqlStatements()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest
		//   "A single SQL query string."
		var table = CreateTable("SqlLog");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		Service.ClearLogs();

		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync()) { }

		Service.SqlLog.Should().Contain(entry =>
			entry.Sql.Contains(table));
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "SqlLog")]
	public async Task SqlLog_CapturesDmlStatements()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteSql
		//   DML statements also go through ExecuteSql or ExecuteBatchDml.
		var table = CreateTable("SqlLogDml");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Old" });

		Service.ClearLogs();

		using var connection = _fixture.CreateConnection();
		await connection.OpenAsync();
		using var transaction = await connection.BeginTransactionAsync();

		using var cmd = connection.CreateDmlCommand($"UPDATE {table} SET Name = 'New' WHERE Id = 1");
		cmd.Transaction = transaction;
		await cmd.ExecuteNonQueryAsync();
		await transaction.CommitAsync();

		Service.SqlLog.Should().Contain(entry =>
			entry.Sql.Contains("UPDATE") && entry.Sql.Contains(table));
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "SqlLog")]
	public async Task SqlLog_HasTimestamps()
	{
		Service.ClearLogs();
		var beforeUtc = DateTimeOffset.UtcNow;

		var table = CreateTable("SqlLogTs");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync()) { }

		Service.SqlLog.Should().NotBeEmpty();
		Service.SqlLog.Should().Contain(entry =>
			entry.Timestamp >= beforeUtc);
	}

	// ─── Export/Import through SDK ───

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "Persistence")]
	public async Task ExportImport_DataSurvivesRoundTrip()
	{
		var table = CreateTable("Persist");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		// Export state
		var json = _fixture.Database!.ExportState();

		// Clear and re-import
		_fixture.Database!.ClearAll();
		_fixture.Database!.ImportState(json);

		// Verify through SDK pipeline
		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Id, Name FROM {table} ORDER BY Id");
		using var reader = await cmd.ExecuteReaderAsync();

		var names = new List<string>();
		while (await reader.ReadAsync())
		{
			names.Add(reader.GetString(reader.GetOrdinal("Name")));
		}

		names.Should().BeEquivalentTo(["Alice", "Bob"]);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "Persistence")]
	public async Task ClearAllData_RemovesDataKeepsSchema()
	{
		var table = CreateTable("ClearData");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		_fixture.Database!.ClearAllData();

		// Schema still exists — query should work
		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();

		(await reader.ReadAsync()).Should().BeFalse(); // No data
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "Persistence")]
	public async Task ExportImport_PreservesSchemaForSdkQueries()
	{
		var table = CreateTable("PersistSchema");
		var json = _fixture.Database!.ExportState();

		_fixture.Database!.ClearAll();
		_fixture.Database!.ImportState(json);

		// Insert through SDK should work on restored schema
		using var connection = _fixture.CreateConnection();
		using var cmd = connection.CreateInsertCommand(table);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 99L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "NewEntry");
		await cmd.ExecuteNonQueryAsync();

		var rows = _fixture.Database!.ExecuteQuery($"SELECT Name FROM {table} WHERE Id = 99");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("NewEntry");
	}
}
