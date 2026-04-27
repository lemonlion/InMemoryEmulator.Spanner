using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="FakeSpannerService.OnRequestReceived"/> and
/// <see cref="FakeSpannerService.OnResponseSent"/> observer callbacks.
/// </summary>
public class ObserverCallbackTests
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerService _service;
	private readonly string _sessionName;

	public ObserverCallbackTests()
	{
		_database = new InMemorySpannerDatabase();
		var options = new FakeSpannerServerOptions
		{
			ProjectId = "test-project",
			InstanceId = "test-instance",
			DatabaseId = "test-db"
		};
		_service = new FakeSpannerService(_database, options);

		var session = _service.CreateSession(
			new CreateSessionRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db"
			},
			TestServerCallContext.Create()).GetAwaiter().GetResult();
		_sessionName = session.Name;
	}

	// ─── OnRequestReceived ───

	[Fact]
	public async Task Unary_OnRequestReceived_Fires_WithCorrectData()
	{
		_database.ExecuteDdl("CREATE TABLE ReqObs1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		SpannerRequestEvent? captured = null;
		_service.OnRequestReceived = e => captured = e;

		await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		captured.Should().NotBeNull();
		captured!.MethodName.Should().Be("ExecuteSql");
		captured.Request.Should().BeOfType<ExecuteSqlRequest>();
		captured.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
	}

	[Fact]
	public async Task Streaming_OnRequestReceived_FiresOnce()
	{
		_database.ExecuteDdl("CREATE TABLE ReqObs2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var events = new List<SpannerRequestEvent>();
		_service.OnRequestReceived = e => events.Add(e);

		var stream = new TestServerStreamWriter<PartialResultSet>(new List<PartialResultSet>());
		await _service.ExecuteStreamingSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			stream, TestServerCallContext.Create());

		events.Should().ContainSingle(e => e.MethodName == "ExecuteStreamingSql");
	}

	// ─── OnResponseSent ───

	[Fact]
	public async Task Unary_OnResponseSent_Fires_WithCorrectData()
	{
		_database.ExecuteDdl("CREATE TABLE RespObs1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		SpannerResponseEvent? captured = null;
		_service.OnResponseSent = e => captured = e;

		await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		captured.Should().NotBeNull();
		captured!.MethodName.Should().Be("ExecuteSql");
		captured.Request.Should().BeOfType<ExecuteSqlRequest>();
		captured.Response.Should().BeOfType<ResultSet>();
		captured.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
		captured.StatusCode.Should().Be(Grpc.Core.StatusCode.OK);
		captured.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
	}

	[Fact]
	public async Task Streaming_OnResponseSent_FiresOnce_WithNullResponse()
	{
		_database.ExecuteDdl("CREATE TABLE RespObs2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var events = new List<SpannerResponseEvent>();
		_service.OnResponseSent = e => events.Add(e);

		var stream = new TestServerStreamWriter<PartialResultSet>(new List<PartialResultSet>());
		await _service.ExecuteStreamingSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			stream, TestServerCallContext.Create());

		events.Should().ContainSingle(e => e.MethodName == "ExecuteStreamingSql");
		events[0].Response.Should().BeNull();
		events[0].StatusCode.Should().Be(Grpc.Core.StatusCode.OK);
		events[0].Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
	}

	// ─── Null callbacks ───

	[Fact]
	public async Task NullCallbacks_MethodWorksNormally()
	{
		_database.ExecuteDdl("CREATE TABLE NullCb (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_service.OnRequestReceived = null;
		_service.OnResponseSent = null;

		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		result.Should().NotBeNull();
	}

	// ─── Exception isolation ───

	[Fact]
	public async Task OnRequestReceived_ThrowingObserver_DoesNotBreakMethod()
	{
		_database.ExecuteDdl("CREATE TABLE ExIso1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_service.OnRequestReceived = _ => throw new InvalidOperationException("Observer blew up");

		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		result.Should().NotBeNull();
	}

	[Fact]
	public async Task OnResponseSent_ThrowingObserver_DoesNotBreakMethod()
	{
		_database.ExecuteDdl("CREATE TABLE ExIso2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_service.OnResponseSent = _ => throw new InvalidOperationException("Observer blew up");

		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		result.Should().NotBeNull();
	}

	// ─── Fault injection interaction ───

	[Fact]
	public void FaultInjection_OnRequestReceived_StillFires()
	{
		SpannerRequestEvent? captured = null;
		_service.OnRequestReceived = e => captured = e;
		_service.FaultInjector = FaultInjector.Always(Grpc.Core.StatusCode.Unavailable);

		var act = () => _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		act.Should().ThrowAsync<RpcException>();
		captured.Should().NotBeNull();
		captured!.MethodName.Should().Be("ExecuteSql");
	}

	[Fact]
	public void FaultInjection_OnResponseSent_FiresWithErrorStatus()
	{
		SpannerResponseEvent? captured = null;
		_service.OnResponseSent = e => captured = e;
		_service.FaultInjector = FaultInjector.Always(Grpc.Core.StatusCode.Unavailable);

		var act = () => _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		act.Should().ThrowAsync<RpcException>();
		captured.Should().NotBeNull();
		captured!.StatusCode.Should().Be(Grpc.Core.StatusCode.Unavailable);
		captured.Response.Should().BeNull();
	}

	// ─── All methods fire ───

	[Fact]
	public async Task AllUnaryMethods_FireBothCallbacks()
	{
		_database.ExecuteDdl("CREATE TABLE AllMethods (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		_database.Insert("AllMethods", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		var requestEvents = new List<SpannerRequestEvent>();
		var responseEvents = new List<SpannerResponseEvent>();
		_service.OnRequestReceived = e => requestEvents.Add(e);
		_service.OnResponseSent = e => responseEvents.Add(e);

		var dbName = "projects/test-project/instances/test-instance/databases/test-db";

		// CreateSession (already have one, create another)
		var session2 = await _service.CreateSession(
			new CreateSessionRequest { Database = dbName },
			TestServerCallContext.Create());

		// BatchCreateSessions
		await _service.BatchCreateSessions(
			new BatchCreateSessionsRequest { Database = dbName, SessionCount = 1 },
			TestServerCallContext.Create());

		// GetSession
		await _service.GetSession(
			new GetSessionRequest { Name = _sessionName },
			TestServerCallContext.Create());

		// ListSessions
		await _service.ListSessions(
			new ListSessionsRequest { Database = dbName },
			TestServerCallContext.Create());

		// BeginTransaction
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Commit
		await _service.Commit(
			new CommitRequest { Session = _sessionName, TransactionId = txn.Id },
			TestServerCallContext.Create());

		// BeginTransaction again for rollback
		var txn2 = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Rollback
		await _service.Rollback(
			new RollbackRequest { Session = _sessionName, TransactionId = txn2.Id },
			TestServerCallContext.Create());

		// ExecuteSql
		await _service.ExecuteSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT 1" },
			TestServerCallContext.Create());

		// ExecuteBatchDml
		var batchDmlReq = new ExecuteBatchDmlRequest { Session = _sessionName };
		batchDmlReq.Transaction = new TransactionSelector
		{
			Begin = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
		};
		batchDmlReq.Statements.Add(new ExecuteBatchDmlRequest.Types.Statement
		{
			Sql = "UPDATE AllMethods SET Name = 'Updated' WHERE Id = 1"
		});
		await _service.ExecuteBatchDml(batchDmlReq, TestServerCallContext.Create());

		// Read
		await _service.Read(
			new ReadRequest
			{
				Session = _sessionName,
				Table = "AllMethods",
				Columns = { "Id", "Name" },
				KeySet = new KeySet { All = true }
			},
			TestServerCallContext.Create());

		// PartitionQuery
		await _service.PartitionQuery(
			new PartitionQueryRequest { Session = _sessionName, Sql = "SELECT Id FROM AllMethods" },
			TestServerCallContext.Create());

		// PartitionRead
		await _service.PartitionRead(
			new PartitionReadRequest
			{
				Session = _sessionName,
				Table = "AllMethods",
				KeySet = new KeySet { All = true },
				Columns = { "Id" }
			},
			TestServerCallContext.Create());

		// DeleteSession (use session2)
		await _service.DeleteSession(
			new DeleteSessionRequest { Name = session2.Name },
			TestServerCallContext.Create());

		var expectedMethods = new[]
		{
			"CreateSession", "BatchCreateSessions", "GetSession", "ListSessions",
			"BeginTransaction", "Commit", "BeginTransaction", "Rollback",
			"ExecuteSql", "ExecuteBatchDml", "Read",
			"PartitionQuery", "PartitionRead", "DeleteSession"
		};

		requestEvents.Select(e => e.MethodName).Should().BeEquivalentTo(expectedMethods, opts => opts.WithStrictOrdering());
		responseEvents.Select(e => e.MethodName).Should().BeEquivalentTo(expectedMethods, opts => opts.WithStrictOrdering());
		responseEvents.Should().OnlyContain(e => e.StatusCode == Grpc.Core.StatusCode.OK);
	}

	[Fact]
	public async Task AllStreamingMethods_FireBothCallbacks()
	{
		_database.ExecuteDdl("CREATE TABLE StreamAll (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		_database.Insert("StreamAll", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Test" });

		var requestEvents = new List<SpannerRequestEvent>();
		var responseEvents = new List<SpannerResponseEvent>();
		_service.OnRequestReceived = e => requestEvents.Add(e);
		_service.OnResponseSent = e => responseEvents.Add(e);

		// ExecuteStreamingSql
		var streamSql = new TestServerStreamWriter<PartialResultSet>(new List<PartialResultSet>());
		await _service.ExecuteStreamingSql(
			new ExecuteSqlRequest { Session = _sessionName, Sql = "SELECT Id FROM StreamAll" },
			streamSql, TestServerCallContext.Create());

		// StreamingRead
		var streamRead = new TestServerStreamWriter<PartialResultSet>(new List<PartialResultSet>());
		await _service.StreamingRead(
			new ReadRequest
			{
				Session = _sessionName,
				Table = "StreamAll",
				Columns = { "Id", "Name" },
				KeySet = new KeySet { All = true }
			},
			streamRead, TestServerCallContext.Create());

		// BatchWrite
		var streamBatch = new TestServerStreamWriter<BatchWriteResponse>(new List<BatchWriteResponse>());
		var bwReq = new BatchWriteRequest { Session = _sessionName };
		var group = new BatchWriteRequest.Types.MutationGroup();
		group.Mutations.Add(new Mutation
		{
			InsertOrUpdate = new Mutation.Types.Write
			{
				Table = "StreamAll",
				Columns = { "Id", "Name" },
				Values = { new Google.Protobuf.WellKnownTypes.ListValue
				{
					Values = { Google.Protobuf.WellKnownTypes.Value.ForString("2"), Google.Protobuf.WellKnownTypes.Value.ForString("BatchTest") }
				}}
			}
		});
		bwReq.MutationGroups.Add(group);
		await _service.BatchWrite(bwReq, streamBatch, TestServerCallContext.Create());

		var expectedMethods = new[] { "ExecuteStreamingSql", "StreamingRead", "BatchWrite" };

		requestEvents.Select(e => e.MethodName).Should().BeEquivalentTo(expectedMethods, opts => opts.WithStrictOrdering());
		responseEvents.Select(e => e.MethodName).Should().BeEquivalentTo(expectedMethods, opts => opts.WithStrictOrdering());
		responseEvents.Should().OnlyContain(e => e.Response == null);
		responseEvents.Should().OnlyContain(e => e.StatusCode == Grpc.Core.StatusCode.OK);
	}

	// ─── FakeSpannerServer pass-through ───

	[Fact]
	public void Server_PassThrough_OnRequestReceived_DelegatesToService()
	{
		var options = new FakeSpannerServerOptions();
		var server = new FakeSpannerServer(new InMemorySpannerDatabase(), options);

		Action<SpannerRequestEvent> handler = _ => { };
		server.OnRequestReceived = handler;

		server.Service.OnRequestReceived.Should().BeSameAs(handler);
		server.OnRequestReceived.Should().BeSameAs(handler);
	}

	[Fact]
	public void Server_PassThrough_OnResponseSent_DelegatesToService()
	{
		var options = new FakeSpannerServerOptions();
		var server = new FakeSpannerServer(new InMemorySpannerDatabase(), options);

		Action<SpannerResponseEvent> handler = _ => { };
		server.OnResponseSent = handler;

		server.Service.OnResponseSent.Should().BeSameAs(handler);
		server.OnResponseSent.Should().BeSameAs(handler);
	}
}
