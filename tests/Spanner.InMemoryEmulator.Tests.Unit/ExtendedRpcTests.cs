using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for BatchWrite, PartitionQuery, and PartitionRead RPCs.
/// These use the FakeSpannerService directly since the SDK doesn't expose
/// these RPCs through the high-level SpannerCommand API.
/// </summary>
public class ExtendedRpcTests
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerService _service;
	private readonly string _sessionName;

	public ExtendedRpcTests()
	{
		_database = new InMemorySpannerDatabase();
		var options = new FakeSpannerServerOptions
		{
			ProjectId = "test-project",
			InstanceId = "test-instance",
			DatabaseId = "test-db"
		};
		_service = new FakeSpannerService(_database, options);

		// Create a session
		var session = _service.CreateSession(
			new CreateSessionRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db"
			},
			TestServerCallContext.Create()).GetAwaiter().GetResult();
		_sessionName = session.Name;
	}

	// ─── PartitionQuery ───

	[Fact]
	public async Task PartitionQuery_ReturnsSinglePartition()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionQuery
		//   "Creates a set of partition tokens that can be used to execute a query operation in parallel."
		_database.ExecuteDdl("CREATE TABLE PQ1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var request = new PartitionQueryRequest
		{
			Session = _sessionName,
			Sql = "SELECT Id FROM PQ1"
		};

		var response = await _service.PartitionQuery(request, TestServerCallContext.Create());

		response.Partitions.Should().HaveCount(1);
		response.Partitions[0].PartitionToken.Should().NotBeEmpty();
	}

	[Fact]
	public async Task PartitionQuery_InvalidSession_Throws()
	{
		var request = new PartitionQueryRequest
		{
			Session = "projects/test-project/instances/test-instance/databases/test-db/sessions/nonexistent",
			Sql = "SELECT 1"
		};

		var act = () => _service.PartitionQuery(request, TestServerCallContext.Create());

		await act.Should().ThrowAsync<RpcException>()
			.Where(ex => ex.StatusCode == StatusCode.NotFound);
	}

	// ─── PartitionRead ───

	[Fact]
	public async Task PartitionRead_ReturnsSinglePartition()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionRead
		//   "Creates a set of partition tokens that can be used to execute a read operation in parallel."
		_database.ExecuteDdl("CREATE TABLE PR1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var request = new PartitionReadRequest
		{
			Session = _sessionName,
			Table = "PR1"
		};

		var response = await _service.PartitionRead(request, TestServerCallContext.Create());

		response.Partitions.Should().HaveCount(1);
		response.Partitions[0].PartitionToken.Should().NotBeEmpty();
	}

	[Fact]
	public async Task PartitionRead_InvalidSession_Throws()
	{
		var request = new PartitionReadRequest
		{
			Session = "projects/test-project/instances/test-instance/databases/test-db/sessions/nonexistent",
			Table = "SomeTable"
		};

		var act = () => _service.PartitionRead(request, TestServerCallContext.Create());

		await act.Should().ThrowAsync<RpcException>()
			.Where(ex => ex.StatusCode == StatusCode.NotFound);
	}

	// ─── BatchWrite ───

	[Fact]
	public async Task BatchWrite_SingleMutationGroup_WritesData()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchWrite
		//   "Batches the supplied mutation groups in a collection of efficient transactions."
		_database.ExecuteDdl("CREATE TABLE BW1 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		var request = new BatchWriteRequest
		{
			Session = _sessionName,
		};

		var mutationGroup = new BatchWriteRequest.Types.MutationGroup();
		var mutation = new Mutation
		{
			InsertOrUpdate = new Mutation.Types.Write
			{
				Table = "BW1"
			}
		};
		mutation.InsertOrUpdate.Columns.Add("Id");
		mutation.InsertOrUpdate.Columns.Add("Name");
		var values = new ListValue();
		values.Values.Add(Value.ForString("1"));
		values.Values.Add(Value.ForString("Alice"));
		mutation.InsertOrUpdate.Values.Add(values);
		mutationGroup.Mutations.Add(mutation);
		request.MutationGroups.Add(mutationGroup);

		var responses = new List<BatchWriteResponse>();
		var responseStream = new TestServerStreamWriter<BatchWriteResponse>(responses);

		await _service.BatchWrite(request, responseStream, TestServerCallContext.Create());

		responses.Should().HaveCount(1);
		responses[0].Status.Code.Should().Be((int)Google.Rpc.Code.Ok);
		responses[0].CommitTimestamp.Should().NotBeNull();
		responses[0].Indexes.Should().Contain(0);

		// Verify data was written
		var rows = _database.ExecuteQuery("SELECT Name FROM BW1 WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public async Task BatchWrite_MultipleMutationGroups_WritesAll()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchWrite
		//   "Each mutation group is applied atomically."
		_database.ExecuteDdl("CREATE TABLE BW2 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		var request = new BatchWriteRequest { Session = _sessionName };

		// Group 1: insert row 1
		var group1 = new BatchWriteRequest.Types.MutationGroup();
		var m1 = new Mutation { InsertOrUpdate = new Mutation.Types.Write { Table = "BW2" } };
		m1.InsertOrUpdate.Columns.AddRange(new[] { "Id", "Name" });
		var v1 = new ListValue();
		v1.Values.Add(Value.ForString("1"));
		v1.Values.Add(Value.ForString("Alice"));
		m1.InsertOrUpdate.Values.Add(v1);
		group1.Mutations.Add(m1);
		request.MutationGroups.Add(group1);

		// Group 2: insert row 2
		var group2 = new BatchWriteRequest.Types.MutationGroup();
		var m2 = new Mutation { InsertOrUpdate = new Mutation.Types.Write { Table = "BW2" } };
		m2.InsertOrUpdate.Columns.AddRange(new[] { "Id", "Name" });
		var v2 = new ListValue();
		v2.Values.Add(Value.ForString("2"));
		v2.Values.Add(Value.ForString("Bob"));
		m2.InsertOrUpdate.Values.Add(v2);
		group2.Mutations.Add(m2);
		request.MutationGroups.Add(group2);

		var responses = new List<BatchWriteResponse>();
		await _service.BatchWrite(request, new TestServerStreamWriter<BatchWriteResponse>(responses), TestServerCallContext.Create());

		responses.Should().HaveCount(2);
		responses.Should().OnlyContain(r => r.Status.Code == (int)Google.Rpc.Code.Ok);

		var rows = _database.ExecuteQuery("SELECT Id FROM BW2 ORDER BY Id");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public async Task BatchWrite_FailedGroup_ReportsError()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.BatchWriteResponse
		//   "status: The status of the mutation group."
		_database.ExecuteDdl("CREATE TABLE BW3 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var request = new BatchWriteRequest { Session = _sessionName };

		// Group with a mutation targeting a non-existent table
		var group = new BatchWriteRequest.Types.MutationGroup();
		var m = new Mutation { InsertOrUpdate = new Mutation.Types.Write { Table = "NonExistentTable" } };
		m.InsertOrUpdate.Columns.Add("Id");
		var v = new ListValue();
		v.Values.Add(Value.ForString("1"));
		m.InsertOrUpdate.Values.Add(v);
		group.Mutations.Add(m);
		request.MutationGroups.Add(group);

		var responses = new List<BatchWriteResponse>();
		await _service.BatchWrite(request, new TestServerStreamWriter<BatchWriteResponse>(responses), TestServerCallContext.Create());

		responses.Should().HaveCount(1);
		responses[0].Status.Code.Should().NotBe((int)Google.Rpc.Code.Ok);
	}

	[Fact]
	public async Task BatchWrite_InvalidSession_Throws()
	{
		var request = new BatchWriteRequest
		{
			Session = "projects/test-project/instances/test-instance/databases/test-db/sessions/nonexistent",
		};
		request.MutationGroups.Add(new BatchWriteRequest.Types.MutationGroup());

		var responses = new List<BatchWriteResponse>();
		var act = () => _service.BatchWrite(request, new TestServerStreamWriter<BatchWriteResponse>(responses), TestServerCallContext.Create());

		await act.Should().ThrowAsync<RpcException>()
			.Where(ex => ex.StatusCode == StatusCode.NotFound);
	}

	[Fact]
	public async Task BatchWrite_EmptyMutationGroups_ReturnsNoResponses()
	{
		_database.ExecuteDdl("CREATE TABLE BW4 (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var request = new BatchWriteRequest { Session = _sessionName };
		// No mutation groups added

		var responses = new List<BatchWriteResponse>();
		await _service.BatchWrite(request, new TestServerStreamWriter<BatchWriteResponse>(responses), TestServerCallContext.Create());

		responses.Should().BeEmpty();
	}

	// ─── RequestLog for new RPCs ───

	[Fact]
	public async Task BatchWrite_IsLoggedInRequestLog()
	{
		_database.ExecuteDdl("CREATE TABLE BW5 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_service.ClearLogs();

		var request = new BatchWriteRequest { Session = _sessionName };
		var group = new BatchWriteRequest.Types.MutationGroup();
		request.MutationGroups.Add(group);

		var responses = new List<BatchWriteResponse>();
		await _service.BatchWrite(request, new TestServerStreamWriter<BatchWriteResponse>(responses), TestServerCallContext.Create());

		_service.RequestLog.Should().Contain(entry => entry.MethodName == "BatchWrite");
	}

	[Fact]
	public async Task PartitionQuery_IsLoggedInRequestLog()
	{
		_database.ExecuteDdl("CREATE TABLE PQ2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_service.ClearLogs();

		var request = new PartitionQueryRequest
		{
			Session = _sessionName,
			Sql = "SELECT Id FROM PQ2"
		};
		await _service.PartitionQuery(request, TestServerCallContext.Create());

		_service.RequestLog.Should().Contain(entry => entry.MethodName == "PartitionQuery");
	}
}

/// <summary>
/// Minimal test server call context for unit testing gRPC overrides.
/// </summary>
internal class TestServerCallContext : ServerCallContext
{
	private TestServerCallContext() { }

	public static ServerCallContext Create()
	{
		return new TestServerCallContext();
	}

	protected override string MethodCore => "test";
	protected override string HostCore => "localhost";
	protected override string PeerCore => "127.0.0.1";
	protected override DateTime DeadlineCore => DateTime.MaxValue;
	protected override Metadata RequestHeadersCore => new();
	protected override CancellationToken CancellationTokenCore => CancellationToken.None;
	protected override Metadata ResponseTrailersCore => new();
	protected override Status StatusCore { get; set; }
	protected override WriteOptions? WriteOptionsCore { get; set; }
	protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());

	protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
	protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}

/// <summary>
/// Test stream writer that collects written messages into a list.
/// </summary>
internal class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
	private readonly List<T> _messages;

	public TestServerStreamWriter(List<T> messages)
	{
		_messages = messages;
	}

	public WriteOptions? WriteOptions { get; set; }

	public Task WriteAsync(T message)
	{
		_messages.Add(message);
		return Task.CompletedTask;
	}
}
