using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for additional RPC validation:
/// - ExecuteBatchDml transaction metadata propagation
/// - DeleteSession NOT_FOUND for non-existent sessions
/// - BeginTransaction validation of TransactionOptions
/// </summary>
public class RpcValidationTests
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerService _service;
	private readonly string _sessionName;

	public RpcValidationTests()
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

		_database.ExecuteDdl("CREATE TABLE TestTable (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
	}

	// ─── ExecuteBatchDml with Begin selector returns transaction metadata ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlResponse
	//   "Only the first ResultSet in the response contains valid ResultSetMetadata."
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetMetadata
	//   "If the read or SQL query began a transaction as a side-effect, the information
	//    about the new transaction is yielded here."
	[Fact]
	public async Task ExecuteBatchDml_WithBeginSelector_FirstResultSetContainsTransactionMetadata()
	{
		// Act
		var response = await _service.ExecuteBatchDml(
			new ExecuteBatchDmlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector
				{
					Begin = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
				},
				Statements =
				{
					new ExecuteBatchDmlRequest.Types.Statement
					{
						Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'Alice')"
					}
				}
			},
			TestServerCallContext.Create());

		// Assert
		response.Status.Code.Should().Be((int)Google.Rpc.Code.Ok);
		response.ResultSets.Should().HaveCount(1);
		response.ResultSets[0].Metadata.Should().NotBeNull();
		response.ResultSets[0].Metadata.Transaction.Should().NotBeNull();
		response.ResultSets[0].Metadata.Transaction.Id.Should().NotBeEmpty();
	}

	// ─── DeleteSession with non-existent session returns NOT_FOUND ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.DeleteSession
	//   Per Google AIP-135 and standard resource semantics, deleting a non-existent resource
	//   should return NOT_FOUND.
	[Fact]
	public async Task DeleteSession_NonExistent_ReturnsNotFound()
	{
		// Act
		var act = () => _service.DeleteSession(
			new DeleteSessionRequest
			{
				Name = "projects/test-project/instances/test-instance/databases/test-db/sessions/nonexistent"
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	// ─── DeleteSession with existing session returns OK ───

	[Fact]
	public async Task DeleteSession_Existing_ReturnsOk()
	{
		// Arrange: create a session
		var session = await _service.CreateSession(
			new CreateSessionRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db"
			},
			TestServerCallContext.Create());

		// Act
		var act = () => _service.DeleteSession(
			new DeleteSessionRequest { Name = session.Name },
			TestServerCallContext.Create());

		// Assert: should not throw
		await act.Should().NotThrowAsync();
	}

	// ─── BeginTransaction with null/empty Options returns INVALID_ARGUMENT ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.BeginTransactionRequest
	//   "options: Required. Options for the new transaction."
	[Fact]
	public async Task BeginTransaction_NoOptions_ReturnsInvalidArgument()
	{
		// Act: no Options set
		var act = () => _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions() // ModeCase == None
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
	}

	// ─── BeginTransaction with valid ReadWrite Options succeeds ───

	[Fact]
	public async Task BeginTransaction_WithReadWriteOptions_Succeeds()
	{
		// Act
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Assert
		txn.Id.Should().NotBeEmpty();
	}

	// ─── ExecuteBatchDml with Id selector on committed txn returns FAILED_PRECONDITION ───

	[Fact]
	public async Task ExecuteBatchDml_WithCommittedTransactionId_ReturnsFailedPrecondition()
	{
		// Arrange: begin and commit a transaction
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.Commit(
			new CommitRequest { Session = _sessionName, TransactionId = txn.Id },
			TestServerCallContext.Create());

		// Act
		var act = () => _service.ExecuteBatchDml(
			new ExecuteBatchDmlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Statements =
				{
					new ExecuteBatchDmlRequest.Types.Statement { Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'X')" }
				}
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── ExecuteSql DML with SingleUse returns INVALID_ARGUMENT ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest
	//   "Standard DML statements require a read-write transaction. To protect against replays,
	//    single-use transactions are not supported."
	[Fact]
	public async Task ExecuteSql_DmlWithSingleUse_ReturnsInvalidArgument()
	{
		// Act
		var act = () => _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector
				{
					SingleUse = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
				},
				Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'Alice')"
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
	}

	// ─── ExecuteSql query with SingleUse is allowed ───

	[Fact]
	public async Task ExecuteSql_QueryWithSingleUse_Succeeds()
	{
		// Act: SELECT queries are allowed with SingleUse
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector
				{
					SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly { Strong = true } }
				},
				Sql = "SELECT 1"
			},
			TestServerCallContext.Create());

		// Assert
		result.Should().NotBeNull();
	}

	// ─── ExecuteBatchDml with SingleUse returns INVALID_ARGUMENT ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
	//   "Must be a read-write transaction. To protect against replays,
	//    single-use transactions are not supported."
	[Fact]
	public async Task ExecuteBatchDml_WithSingleUse_ReturnsInvalidArgument()
	{
		// Act
		var act = () => _service.ExecuteBatchDml(
			new ExecuteBatchDmlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector
				{
					SingleUse = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
				},
				Statements =
				{
					new ExecuteBatchDmlRequest.Types.Statement { Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'Alice')" }
				}
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
	}

	// ─── BatchCreateSessions with count=0 creates at least one session ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.BatchCreateSessionsRequest
	//   "At least one session is created."
	[Fact]
	public async Task BatchCreateSessions_CountZero_CreatesAtLeastOne()
	{
		// Act
		var response = await _service.BatchCreateSessions(
			new BatchCreateSessionsRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db",
				SessionCount = 0
			},
			TestServerCallContext.Create());

		// Assert
		response.Session.Should().HaveCountGreaterOrEqualTo(1);
	}

	// ─── DML with PROFILE QueryMode preserves RowCountExact ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSet
	//   "DML statements always produce stats containing the number of rows modified,
	//    unless executed using the ExecuteSqlRequest.QueryMode.PLAN."
	[Fact]
	public async Task ExecuteSql_DmlWithProfileMode_PreservesRowCountExact()
	{
		// Arrange: begin a transaction and insert a row
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Act: DML with PROFILE mode
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'Alice')",
				QueryMode = ExecuteSqlRequest.Types.QueryMode.Profile
			},
			TestServerCallContext.Create());

		// Assert: Stats should contain both QueryStats AND RowCountExact
		result.Stats.Should().NotBeNull();
		result.Stats.QueryStats.Should().NotBeNull();
		result.Stats.RowCountExact.Should().Be(1);
	}

	// ─── Replace mutation cascades to interleaved children ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
	//   "In an interleaved table, if you create the child table with the ON DELETE CASCADE
	//    annotation, then replacing a parent row also deletes the child rows."
	[Fact]
	public async Task Replace_WithInterleavedChild_CascadesDelete()
	{
		// Arrange: create parent + child tables with ON DELETE CASCADE
		_database.ExecuteDdl("CREATE TABLE ParentT (PId INT64 NOT NULL) PRIMARY KEY (PId)");
		_database.ExecuteDdl("CREATE TABLE ChildT (PId INT64 NOT NULL, CId INT64 NOT NULL) PRIMARY KEY (PId, CId), INTERLEAVE IN PARENT ParentT ON DELETE CASCADE");

		// Insert parent + child
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id,
				Mutations =
				{
					new Mutation
					{
						Insert = new Mutation.Types.Write
						{
							Table = "ParentT",
							Columns = { "PId" },
							Values = { new ListValue { Values = { Value.ForString("1") } } }
						}
					},
					new Mutation
					{
						Insert = new Mutation.Types.Write
						{
							Table = "ChildT",
							Columns = { "PId", "CId" },
							Values = { new ListValue { Values = { Value.ForString("1"), Value.ForString("10") } } }
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Act: Replace the parent row
		var txn2 = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn2.Id,
				Mutations =
				{
					new Mutation
					{
						Replace = new Mutation.Types.Write
						{
							Table = "ParentT",
							Columns = { "PId" },
							Values = { new ListValue { Values = { Value.ForString("1") } } }
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Assert: Child row should have been cascade-deleted
		var txn3 = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() }
			},
			TestServerCallContext.Create());

		var childResult = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn3.Id },
				Sql = "SELECT CId FROM ChildT WHERE PId = 1"
			},
			TestServerCallContext.Create());

		childResult.Rows.Should().BeEmpty();
	}

	// ─── CommitStats mutation_count counts columns not rows ───

	// Ref: https://cloud.google.com/spanner/quotas
	//   "A mutation is counted for each column value written."
	[Fact]
	public async Task CommitStats_MutationCount_CountsColumnsNotRows()
	{
		// Arrange: begin a transaction
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Act: Commit with a single row insert (2 columns: Id and Name)
		var response = await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id,
				ReturnCommitStats = true,
				Mutations =
				{
					new Mutation
					{
						Insert = new Mutation.Types.Write
						{
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values = { new ListValue { Values = { Value.ForString("99"), Value.ForString("Test") } } }
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Assert: 1 row × 2 columns = 2 mutations
		response.CommitStats.Should().NotBeNull();
		response.CommitStats.MutationCount.Should().Be(2);
	}
}
