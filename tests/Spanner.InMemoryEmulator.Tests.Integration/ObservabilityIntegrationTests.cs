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

	// ─── Observer Callbacks ───

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_OnRequestReceived_FiresForQuery()
	{
		var table = CreateTable("ObsReq");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		var events = new List<SpannerRequestEvent>();
		Service.OnRequestReceived = e => events.Add(e);
		try
		{
			using var connection = _fixture.CreateConnection();
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync()) { }

			events.Should().Contain(e => e.MethodName == "ExecuteStreamingSql");
		}
		finally
		{
			Service.OnRequestReceived = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_OnResponseSent_FiresForQuery_WithOkStatus()
	{
		var table = CreateTable("ObsResp");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Bob" });

		var events = new List<SpannerResponseEvent>();
		Service.OnResponseSent = e => events.Add(e);
		try
		{
			using var connection = _fixture.CreateConnection();
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync()) { }

			var queryEvent = events.FirstOrDefault(e => e.MethodName == "ExecuteStreamingSql");
			queryEvent.Should().NotBeNull();
			queryEvent!.StatusCode.Should().Be(Grpc.Core.StatusCode.OK);
			queryEvent.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
		}
		finally
		{
			Service.OnResponseSent = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_FiresForDml()
	{
		var table = CreateTable("ObsDml");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Init" });

		var requestEvents = new List<SpannerRequestEvent>();
		var responseEvents = new List<SpannerResponseEvent>();
		Service.OnRequestReceived = e => requestEvents.Add(e);
		Service.OnResponseSent = e => responseEvents.Add(e);
		try
		{
			using var connection = _fixture.CreateConnection();
			await connection.OpenAsync();
			using var txn = await connection.BeginTransactionAsync();
			using var cmd = connection.CreateDmlCommand($"UPDATE {table} SET Name = 'Updated' WHERE Id = 1");
			cmd.Transaction = txn;
			await cmd.ExecuteNonQueryAsync();
			await txn.CommitAsync();

			// Should have observed ExecuteSql or ExecuteStreamingSql (DML) and Commit
			requestEvents.Should().Contain(e => e.MethodName == "Commit");
			responseEvents.Should().Contain(e => e.MethodName == "Commit" && e.StatusCode == Grpc.Core.StatusCode.OK);
		}
		finally
		{
			Service.OnRequestReceived = null;
			Service.OnResponseSent = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_WithFaultInjector_BothActive()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
		//   All RPC methods can return standard gRPC error codes.
		var table = CreateTable("ObsFault");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		var requestEvents = new List<SpannerRequestEvent>();
		var responseEvents = new List<SpannerResponseEvent>();
		Service.OnRequestReceived = e => requestEvents.Add(e);
		Service.OnResponseSent = e => responseEvents.Add(e);
		Service.FaultInjector = FaultInjector.ForMethod("ExecuteStreamingSql", Grpc.Core.StatusCode.PermissionDenied, "denied");
		try
		{
			using var connection = _fixture.CreateConnection();
			await connection.OpenAsync();

			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			Func<Task> act = async () =>
			{
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync()) { }
			};
			await act.Should().ThrowAsync<SpannerException>();

			// OnRequestReceived should have fired for the faulted call
			requestEvents.Should().Contain(e => e.MethodName == "ExecuteStreamingSql");

			// OnResponseSent should have fired with the error status
			responseEvents.Should().Contain(e =>
				e.MethodName == "ExecuteStreamingSql" &&
				e.StatusCode == Grpc.Core.StatusCode.PermissionDenied);
		}
		finally
		{
			Service.OnRequestReceived = null;
			Service.OnResponseSent = null;
			Service.FaultInjector = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_ServerPassThrough_Works()
	{
		var table = CreateTable("ObsPass");
		_fixture.Database!.Insert(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		var events = new List<SpannerRequestEvent>();
		// Use the FakeSpannerServer pass-through instead of Service directly
		_fixture.Server!.OnRequestReceived = e => events.Add(e);
		try
		{
			using var connection = _fixture.CreateConnection();
			using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync()) { }

			events.Should().Contain(e => e.MethodName == "ExecuteStreamingSql");
		}
		finally
		{
			_fixture.Server!.OnRequestReceived = null;
		}
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	[Trait(TestTraits.Category, "ObserverCallbacks")]
	public async Task Observer_AllMethodsFire_TypicalWorkflow()
	{
		var table = CreateTable("ObsAll");

		var requestEvents = new List<SpannerRequestEvent>();
		var responseEvents = new List<SpannerResponseEvent>();
		Service.ClearLogs();
		Service.OnRequestReceived = e => requestEvents.Add(e);
		Service.OnResponseSent = e => responseEvents.Add(e);
		try
		{
			using var connection = _fixture.CreateConnection();
			await connection.OpenAsync();

			// DML in a transaction + commit
			using var txn = await connection.BeginTransactionAsync();
			using var insertCmd = connection.CreateInsertCommand(table);
			insertCmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
			insertCmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
			insertCmd.Transaction = txn;
			await insertCmd.ExecuteNonQueryAsync();
			await txn.CommitAsync();

			// Query
			using var queryCmd = connection.CreateSelectCommand($"SELECT Name FROM {table}");
			using var reader = await queryCmd.ExecuteReaderAsync();
			while (await reader.ReadAsync()) { }

			// Every request event should have a matching response event
			var requestMethodNames = requestEvents.Select(e => e.MethodName).ToList();
			var responseMethodNames = responseEvents.Select(e => e.MethodName).ToList();

			requestMethodNames.Should().BeEquivalentTo(responseMethodNames, opts => opts.WithStrictOrdering());
			responseEvents.Should().OnlyContain(e => e.StatusCode == Grpc.Core.StatusCode.OK);
		}
		finally
		{
			Service.OnRequestReceived = null;
			Service.OnResponseSent = null;
		}
	}
}
