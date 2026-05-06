using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for transaction lifecycle validation:
/// - Double-commit should return FAILED_PRECONDITION
/// - Commit after rollback should return FAILED_PRECONDITION
/// - Rollback of committed transaction should not undo committed data
/// - ExecuteSql with committed/rolled-back transaction should return FAILED_PRECONDITION
/// - Commit with single_use_transaction should create a temporary transaction and apply mutations
/// </summary>
public class TransactionValidationTests
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerService _service;
	private readonly string _sessionName;

	public TransactionValidationTests()
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

		// Create a table for testing
		_database.ExecuteDdl("CREATE TABLE TestTable (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
	}

	// ─── Commit after Rollback ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	//   "Commit a previously-started transaction." — implies transaction must be active
	[Fact]
	public async Task Commit_AfterRollback_ReturnsFailedPrecondition()
	{
		// Arrange: begin and rollback a transaction
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.Rollback(
			new RollbackRequest { Session = _sessionName, TransactionId = txn.Id },
			TestServerCallContext.Create());

		// Act: try to commit the rolled-back transaction
		var act = () => _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create());

		// Assert: should fail with FAILED_PRECONDITION
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── Double Commit ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	//   Transaction must be in active state to commit
	[Fact]
	public async Task Commit_AlreadyCommitted_ReturnsFailedPrecondition()
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
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create());

		// Act: try to commit again
		var act = () => _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create());

		// Assert: should fail with FAILED_PRECONDITION
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── Rollback of committed transaction should not undo data ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
	//   "Rollback returns OK if it successfully aborts the transaction, the transaction was
	//    already aborted, or the transaction isn't found."
	//   A committed transaction cannot be aborted — rolling it back should not undo its changes.
	[Fact]
	public async Task Rollback_CommittedTransaction_DoesNotUndoData()
	{
		// Arrange: begin a transaction and do DML to insert a row
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Insert a row via DML
		await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Sql = "INSERT INTO TestTable (Id, Name) VALUES (1, 'Alice')"
			},
			TestServerCallContext.Create());

		// Commit the transaction
		await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create());

		// Act: attempt to rollback the committed transaction
		await _service.Rollback(
			new RollbackRequest { Session = _sessionName, TransactionId = txn.Id },
			TestServerCallContext.Create());

		// Assert: data should still be present (the rollback should NOT undo committed data)
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Sql = "SELECT Name FROM TestTable WHERE Id = 1"
			},
			TestServerCallContext.Create());

		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("Alice");
	}

	// ─── ExecuteSql with committed transaction ID ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionSelector
	//   "id: Execute the read or SQL query in a previously-started transaction."
	//   A committed transaction is no longer "started" — it should be rejected.
	[Fact]
	public async Task ExecuteSql_WithCommittedTransactionId_ReturnsFailedPrecondition()
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
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create());

		// Act: try to execute SQL with the committed transaction
		var act = () => _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Sql = "SELECT 1"
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── ExecuteSql with rolled-back transaction ID ───

	[Fact]
	public async Task ExecuteSql_WithRolledBackTransactionId_ReturnsFailedPrecondition()
	{
		// Arrange: begin and rollback a transaction
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.Rollback(
			new RollbackRequest { Session = _sessionName, TransactionId = txn.Id },
			TestServerCallContext.Create());

		// Act: try to execute SQL with the rolled-back transaction
		var act = () => _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Sql = "SELECT 1"
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── Commit with SingleUseTransaction ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
	//   "single_use_transaction: Execute mutations in a temporary transaction."
	[Fact]
	public async Task Commit_WithSingleUseTransaction_AppliesMutations()
	{
		// Act: commit with single_use_transaction (no prior BeginTransaction needed)
		var response = await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				SingleUseTransaction = new TransactionOptions
				{
					ReadWrite = new TransactionOptions.Types.ReadWrite()
				},
				Mutations =
				{
					new Mutation
					{
						InsertOrUpdate = new Mutation.Types.Write
						{
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values = { new Google.Protobuf.WellKnownTypes.ListValue
							{
								Values = { Value.ForString("1"), Value.ForString("Bob") }
							}}
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Assert: commit should succeed with a timestamp
		response.CommitTimestamp.Should().NotBeNull();

		// Verify data was written
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Sql = "SELECT Name FROM TestTable WHERE Id = 1"
			},
			TestServerCallContext.Create());

		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("Bob");
	}

	// ─── Commit with SingleUseTransaction returns commit stats ───

	[Fact]
	public async Task Commit_WithSingleUseTransaction_ReturnsCommitStats()
	{
		// Act
		var response = await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				SingleUseTransaction = new TransactionOptions
				{
					ReadWrite = new TransactionOptions.Types.ReadWrite()
				},
				ReturnCommitStats = true,
				Mutations =
				{
					new Mutation
					{
						InsertOrUpdate = new Mutation.Types.Write
						{
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values = { new Google.Protobuf.WellKnownTypes.ListValue
							{
								Values = { Value.ForString("1"), Value.ForString("Stats") }
							}}
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Assert
		response.CommitStats.Should().NotBeNull();
		response.CommitStats.MutationCount.Should().Be(1);
	}

	// ─── Commit with neither transaction_id nor single_use_transaction ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
	//   "Required. The transaction in which to commit." — one of the two must be set
	[Fact]
	public async Task Commit_WithNoTransaction_ReturnsInvalidArgument()
	{
		// Act: commit without specifying any transaction
		var act = () => _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				Mutations =
				{
					new Mutation
					{
						InsertOrUpdate = new Mutation.Types.Write
						{
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values = { new Google.Protobuf.WellKnownTypes.ListValue
							{
								Values = { Value.ForString("1"), Value.ForString("NoTxn") }
							}}
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Assert
		var ex = await act.Should().ThrowAsync<RpcException>();
		ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
	}
}
