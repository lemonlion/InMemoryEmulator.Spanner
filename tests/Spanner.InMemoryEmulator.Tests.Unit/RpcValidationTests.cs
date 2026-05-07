using FluentAssertions;
using Google.Cloud.Spanner.Admin.Database.V1;
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

	// ─── CAST to JSON support ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	//   "CAST(expr AS JSON): STRING values are parsed as JSON; other types are wrapped as JSON scalars."
	[Fact]
	public async Task ExecuteSql_CastStringToJson_ReturnsJsonValue()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT CAST('{\"key\": \"value\"}' AS JSON) AS j"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("{\"key\": \"value\"}");
	}

	[Fact]
	public async Task ExecuteSql_CastInt64ToJson_ReturnsNumericString()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT CAST(42 AS JSON) AS j"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("42");
	}

	// ─── INFORMATION_SCHEMA.COLUMN_OPTIONS returns data for commit timestamp columns ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#column_options
	//   "Contains information about the options set on columns."
	[Fact]
	public async Task InformationSchema_ColumnOptions_ReturnsAllowCommitTimestamp()
	{
		// Arrange: Create a table with a commit timestamp column
		_database.ExecuteDdl("CREATE TABLE TsTable (Id INT64 NOT NULL, CreatedAt TIMESTAMP NOT NULL OPTIONS (allow_commit_timestamp = true)) PRIMARY KEY (Id)");

		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT TABLE_NAME, COLUMN_NAME, OPTION_NAME, OPTION_TYPE, OPTION_VALUE FROM INFORMATION_SCHEMA.COLUMN_OPTIONS WHERE TABLE_NAME = 'TsTable'"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("TsTable");
		result.Rows[0].Values[1].StringValue.Should().Be("CreatedAt");
		result.Rows[0].Values[2].StringValue.Should().Be("allow_commit_timestamp");
		result.Rows[0].Values[3].StringValue.Should().Be("BOOL");
		result.Rows[0].Values[4].StringValue.Should().Be("TRUE");
	}

	// ─── INFORMATION_SCHEMA.COLUMNS includes new metadata columns ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#columns
	//   "Contains columns: IS_GENERATED, GENERATION_EXPRESSION, IS_STORED, SPANNER_STATE, COLUMN_DEFAULT"
	[Fact]
	public async Task InformationSchema_Columns_IncludesIsGeneratedAndSpannerState()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT COLUMN_NAME, IS_GENERATED, SPANNER_STATE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestTable' AND COLUMN_NAME = 'Id'"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("Id");
		result.Rows[0].Values[1].StringValue.Should().Be("NEVER");
		result.Rows[0].Values[2].StringValue.Should().Be("COMMITTED");
	}

	// ─── ALTER TABLE IF EXISTS / ADD COLUMN IF NOT EXISTS / DROP COLUMN IF EXISTS ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   "ALTER TABLE IF EXISTS — no error if the table does not exist."
	[Fact]
	public void AlterTable_IfExists_NonExistentTable_DoesNotThrow()
	{
		// Act & Assert — should not throw
		_database.ExecuteDdl("ALTER TABLE IF EXISTS NonExistentTable ADD COLUMN Foo INT64");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   "ADD COLUMN IF NOT EXISTS — no error if the column already exists."
	[Fact]
	public void AlterTable_AddColumn_IfNotExists_ExistingColumn_DoesNotThrow()
	{
		// Act & Assert — Name column already exists; should not throw
		_database.ExecuteDdl("ALTER TABLE TestTable ADD COLUMN IF NOT EXISTS Name STRING(MAX)");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	//   "DROP COLUMN IF EXISTS — no error if the column does not exist."
	[Fact]
	public void AlterTable_DropColumn_IfExists_NonExistentColumn_DoesNotThrow()
	{
		// Act & Assert — NonExistent column doesn't exist; should not throw
		_database.ExecuteDdl("ALTER TABLE TestTable DROP COLUMN IF EXISTS NonExistent");
	}

	// ─── ALTER DATABASE SET OPTIONS as no-op ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_database
	//   "ALTER DATABASE db SET OPTIONS (optimizer_version = 5)"
	[Fact]
	public void AlterDatabase_SetOptions_DoesNotThrow()
	{
		// Act & Assert — should be accepted as no-op
		_database.ExecuteDdl("ALTER DATABASE mydb SET OPTIONS (optimizer_version = 5, version_retention_period = '7d')");
	}

	// ─── CREATE INDEX INTERLEAVE IN clause ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-index
	//   "CREATE INDEX ... ON ... (...) [STORING (...)] [, INTERLEAVE IN table_name]"
	[Fact]
	public void CreateIndex_WithInterleaveIn_DoesNotThrow()
	{
		// Arrange: create parent/child tables
		_database.ExecuteDdl("CREATE TABLE IdxParent (PId INT64 NOT NULL) PRIMARY KEY (PId)");
		_database.ExecuteDdl("CREATE TABLE IdxChild (PId INT64 NOT NULL, CId INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (PId, CId), INTERLEAVE IN PARENT IdxParent ON DELETE CASCADE");

		// Act & Assert — INTERLEAVE IN clause should be accepted
		_database.ExecuteDdl("CREATE INDEX IdxChildByVal ON IdxChild (Val), INTERLEAVE IN IdxParent");
	}

	// ─── Commit rejects read-only and partitioned DML transactions ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
	//   "Read-only transactions do not support commit."
	[Fact]
	public async Task Commit_ReadOnlyTransaction_ReturnsFailedPrecondition()
	{
		// Arrange
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() }
			},
			TestServerCallContext.Create());

		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
	//   "Partitioned DML transactions auto-commit; explicit Commit is not supported."
	[Fact]
	public async Task Commit_PartitionedDmlTransaction_ReturnsFailedPrecondition()
	{
		// Arrange
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { PartitionedDml = new TransactionOptions.Types.PartitionedDml() }
			},
			TestServerCallContext.Create());

		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn.Id
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── ExecuteBatchDml validates empty statements ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
	//   "Required. The list of statements... At least one statement must be provided."
	[Fact]
	public async Task ExecuteBatchDml_EmptyStatements_ReturnsInvalidArgument()
	{
		// Arrange
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.ExecuteBatchDml(
			new ExecuteBatchDmlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id }
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
	}

	// ─── ExecuteBatchDml rejects partitioned DML ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
	//   "Must be a read-write transaction." — partitioned DML is not read-write.
	[Fact]
	public async Task ExecuteBatchDml_PartitionedDml_ReturnsFailedPrecondition()
	{
		// Arrange
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { PartitionedDml = new TransactionOptions.Types.PartitionedDml() }
			},
			TestServerCallContext.Create());

		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.ExecuteBatchDml(
			new ExecuteBatchDmlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn.Id },
				Statements = { new ExecuteBatchDmlRequest.Types.Statement { Sql = "DELETE FROM TestTable WHERE true" } }
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// ─── Read API returns proper error codes ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
	//   "Required. The name of the table in the database to be read."
	[Fact]
	public async Task Read_NonExistentTable_ReturnsNotFound()
	{
		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.Read(
			new ReadRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Table = "NonExistentTable",
				Columns = { "Id" },
				KeySet = new KeySet { All = true }
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.NotFound);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
	//   "Required. The columns of table to be returned for each row matching this request."
	[Fact]
	public async Task Read_NonExistentColumn_ReturnsNotFound()
	{
		// Act & Assert
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.Read(
			new ReadRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Table = "TestTable",
				Columns = { "NonExistentColumn" },
				KeySet = new KeySet { All = true }
			},
			TestServerCallContext.Create()));

		ex.StatusCode.Should().Be(StatusCode.NotFound);
	}

	// ─── Partitioned DML returns RowCountLowerBound ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetStats
	//   "Partitioned DML doesn't offer exactly-once semantics, so it returns a lower bound."
	[Fact]
	public async Task ExecuteSql_PartitionedDml_ReturnsRowCountLowerBound()
	{
		// Arrange: insert a row first
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
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values = { new ListValue { Values = { Value.ForString("1"), Value.ForString("Alice") } } }
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Begin partitioned DML transaction
		var pdmlTxn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { PartitionedDml = new TransactionOptions.Types.PartitionedDml() }
			},
			TestServerCallContext.Create());

		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = pdmlTxn.Id },
				Sql = "UPDATE TestTable SET Name = 'Updated' WHERE Id = 1"
			},
			TestServerCallContext.Create());

		// Assert
		result.Stats.Should().NotBeNull();
		result.Stats.RowCountCase.Should().Be(ResultSetStats.RowCountOneofCase.RowCountLowerBound);
		result.Stats.RowCountLowerBound.Should().Be(1);
	}

	// ─── TIMESTAMP_DIFF with NANOSECOND ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	//   NANOSECOND is a supported date part.
	[Fact]
	public async Task ExecuteSql_TimestampDiff_Nanosecond_Works()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', NANOSECOND) AS diff"
			},
			TestServerCallContext.Create());

		// Assert: 1 second = 1,000,000,000 nanoseconds
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("1000000000");
	}

	// ─── UPDATE SET evaluates expressions against original row ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
	//   "All SET clause column value expressions are evaluated before any are assigned."
	[Fact]
	public async Task ExecuteSql_UpdateSwapColumns_EvaluatesAgainstOriginal()
	{
		// Arrange: create table and insert a row
		_database.ExecuteDdl("CREATE TABLE SwapTable (Id INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (Id)");
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
							Table = "SwapTable",
							Columns = { "Id", "A", "B" },
							Values = { new ListValue { Values = { Value.ForString("1"), Value.ForString("10"), Value.ForString("20") } } }
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Act: swap A and B
		var txn2 = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() }
			},
			TestServerCallContext.Create());

		await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn2.Id },
				Sql = "UPDATE SwapTable SET A = B, B = A WHERE Id = 1"
			},
			TestServerCallContext.Create());

		await _service.Commit(
			new CommitRequest
			{
				Session = _sessionName,
				TransactionId = txn2.Id
			},
			TestServerCallContext.Create());

		// Assert: A should be 20, B should be 10 (swapped)
		var readTxn = await _service.BeginTransaction(
			new BeginTransactionRequest
			{
				Session = _sessionName,
				Options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() }
			},
			TestServerCallContext.Create());

		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = readTxn.Id },
				Sql = "SELECT A, B FROM SwapTable WHERE Id = 1"
			},
			TestServerCallContext.Create());

		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].StringValue.Should().Be("20"); // A = original B
		result.Rows[0].Values[1].StringValue.Should().Be("10"); // B = original A
	}

	// ─── IN subquery with NULL returns NULL (three-valued logic) ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	//   "x IN (a, b, NULL): If x doesn't match any non-null value but NULL is present, result is NULL."
	[Fact]
	public async Task ExecuteSql_InSubquery_NullValueReturnsNull()
	{
		// Act: NULL IN (SELECT 1) should return NULL
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT CAST(NULL AS INT64) IN (SELECT 1) AS result"
			},
			TestServerCallContext.Create());

		// Assert: should be NULL
		result.Rows.Should().HaveCount(1);
		result.Rows[0].Values[0].HasNullValue.Should().BeTrue();
	}

	// ─── FK ON DELETE enforcement ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	//   "ON DELETE NO ACTION: If referencing rows exist, the referenced row cannot be deleted."
	[Fact]
	public async Task Delete_ForeignKey_NoAction_BlocksDeleteWithReferencingRows()
	{
		// Arrange: create parent and child tables with FK
		_database.ExecuteDdl("CREATE TABLE FkParent (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_database.ExecuteDdl(@"CREATE TABLE FkChild (
			ChildId INT64 NOT NULL, ParentId INT64,
			CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES FkParent(Id)
		) PRIMARY KEY (ChildId)");

		// Insert parent and child rows
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest { Session = _sessionName, Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() } },
			TestServerCallContext.Create());
		await _service.Commit(new CommitRequest
		{
			Session = _sessionName, TransactionId = txn.Id,
			Mutations =
			{
				new Mutation { Insert = new Mutation.Types.Write { Table = "FkParent", Columns = { "Id" }, Values = { new ListValue { Values = { Value.ForString("1") } } } } },
				new Mutation { Insert = new Mutation.Types.Write { Table = "FkChild", Columns = { "ChildId", "ParentId" }, Values = { new ListValue { Values = { Value.ForString("10"), Value.ForString("1") } } } } }
			}
		}, TestServerCallContext.Create());

		// Act: Try to delete the parent row
		var txn2 = await _service.BeginTransaction(
			new BeginTransactionRequest { Session = _sessionName, Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() } },
			TestServerCallContext.Create());
		var ex = await Assert.ThrowsAsync<RpcException>(() => _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn2.Id },
				Sql = "DELETE FROM FkParent WHERE Id = 1"
			},
			TestServerCallContext.Create()));

		// Assert: should fail
		ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	//   "ON DELETE CASCADE: Deletes referencing rows when the referenced row is deleted."
	[Fact]
	public async Task Delete_ForeignKey_Cascade_DeletesReferencingRows()
	{
		// Arrange: create parent and child tables with FK CASCADE
		_database.ExecuteDdl("CREATE TABLE FkParent2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		_database.ExecuteDdl(@"CREATE TABLE FkChild2 (
			ChildId INT64 NOT NULL, ParentId INT64,
			CONSTRAINT FK_Child_Parent2 FOREIGN KEY (ParentId) REFERENCES FkParent2(Id) ON DELETE CASCADE
		) PRIMARY KEY (ChildId)");

		// Insert parent and child rows
		var txn = await _service.BeginTransaction(
			new BeginTransactionRequest { Session = _sessionName, Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() } },
			TestServerCallContext.Create());
		await _service.Commit(new CommitRequest
		{
			Session = _sessionName, TransactionId = txn.Id,
			Mutations =
			{
				new Mutation { Insert = new Mutation.Types.Write { Table = "FkParent2", Columns = { "Id" }, Values = { new ListValue { Values = { Value.ForString("1") } } } } },
				new Mutation { Insert = new Mutation.Types.Write { Table = "FkChild2", Columns = { "ChildId", "ParentId" }, Values = { new ListValue { Values = { Value.ForString("10"), Value.ForString("1") } } } } },
				new Mutation { Insert = new Mutation.Types.Write { Table = "FkChild2", Columns = { "ChildId", "ParentId" }, Values = { new ListValue { Values = { Value.ForString("20"), Value.ForString("1") } } } } }
			}
		}, TestServerCallContext.Create());

		// Act: Delete the parent row
		var txn2 = await _service.BeginTransaction(
			new BeginTransactionRequest { Session = _sessionName, Options = new TransactionOptions { ReadWrite = new TransactionOptions.Types.ReadWrite() } },
			TestServerCallContext.Create());
		await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { Id = txn2.Id },
				Sql = "DELETE FROM FkParent2 WHERE Id = 1"
			},
			TestServerCallContext.Create());
		await _service.Commit(new CommitRequest { Session = _sessionName, TransactionId = txn2.Id },
			TestServerCallContext.Create());

		// Assert: child rows should be cascade-deleted
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT ChildId FROM FkChild2"
			},
			TestServerCallContext.Create());
		result.Rows.Should().BeEmpty();
	}

	// ─── WITH RECURSIVE ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#recursive_keyword
	//   "RECURSIVE allows named references back to the same CTE."
	[Fact]
	public async Task ExecuteSql_WithRecursive_GeneratesHierarchy()
	{
		// Act: generate numbers 1..5 using recursive CTE
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = @"
					WITH RECURSIVE nums AS (
						SELECT 1 AS n
						UNION ALL
						SELECT n + 1 FROM nums WHERE n < 5
					)
					SELECT n FROM nums ORDER BY n"
			},
			TestServerCallContext.Create());

		// Assert: should produce 1, 2, 3, 4, 5
		result.Rows.Should().HaveCount(5);
		result.Rows.Select(r => long.Parse(r.Values[0].StringValue)).Should().Equal(1L, 2L, 3L, 4L, 5L);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#recursive_keyword
	//   "Recursive CTE with base case returning no rows produces empty result."
	[Fact]
	public async Task ExecuteSql_WithRecursive_BaseReturnsNoRows_EmptyResult()
	{
		// Act: base case returns no rows (empty UNNEST), so recursion never starts
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#where_clause
		//   WHERE without FROM is invalid on real Spanner; use UNNEST of empty array.
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = @"
					WITH RECURSIVE empty AS (
						SELECT CAST(NULL AS INT64) AS n FROM UNNEST(ARRAY<INT64>[]) AS _x
						UNION ALL
						SELECT n + 1 FROM empty WHERE n < 5
					)
					SELECT n FROM empty"
			},
			TestServerCallContext.Create());

		// Assert: should produce no rows
		result.Rows.Should().BeEmpty();
	}

	// ─── SELECT AS STRUCT ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_as_struct
	//   "SELECT AS STRUCT produces a value table with a STRUCT row type."
	[Fact]
	public async Task ExecuteSql_SelectAsStruct_ReturnsStructColumn()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT AS STRUCT 1 AS x, 'hello' AS y"
			},
			TestServerCallContext.Create());

		// Assert: should have a single row with a single STRUCT column
		result.Rows.Should().HaveCount(1);
		// The struct should be represented as a list value containing the field values
		var structVal = result.Rows[0].Values[0];
		structVal.KindCase.Should().Be(Value.KindOneofCase.ListValue);
		structVal.ListValue.Values.Should().HaveCount(2);
		structVal.ListValue.Values[0].StringValue.Should().Be("1");
		structVal.ListValue.Values[1].StringValue.Should().Be("hello");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_as_struct
	//   "ARRAY(SELECT AS STRUCT col1, col2 FROM ...) produces an array of structs."
	[Fact]
	public async Task ExecuteSql_ArraySelectAsStruct_ReturnsArrayOfStructs()
	{
		// Arrange: insert data
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
							Table = "TestTable",
							Columns = { "Id", "Name" },
							Values =
							{
								new ListValue { Values = { Value.ForString("1"), Value.ForString("Alice") } },
								new ListValue { Values = { Value.ForString("2"), Value.ForString("Bob") } }
							}
						}
					}
				}
			},
			TestServerCallContext.Create());

		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT ARRAY(SELECT AS STRUCT Id, Name FROM TestTable ORDER BY Id) AS arr"
			},
			TestServerCallContext.Create());

		// Assert: should have a single row with an array of structs
		result.Rows.Should().HaveCount(1);
		var arrVal = result.Rows[0].Values[0];
		arrVal.KindCase.Should().Be(Value.KindOneofCase.ListValue);
		arrVal.ListValue.Values.Should().HaveCount(2);
		// Each element is a struct (list)
		arrVal.ListValue.Values[0].KindCase.Should().Be(Value.KindOneofCase.ListValue);
		arrVal.ListValue.Values[0].ListValue.Values[0].StringValue.Should().Be("1");
		arrVal.ListValue.Values[0].ListValue.Values[1].StringValue.Should().Be("Alice");
		arrVal.ListValue.Values[1].ListValue.Values[0].StringValue.Should().Be("2");
		arrVal.ListValue.Values[1].ListValue.Values[1].StringValue.Should().Be("Bob");
	}

	// ─── INFORMATION_SCHEMA.INDEXES returns PARENT_TABLE_NAME and SPANNER_IS_MANAGED ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#indexes
	//   "PARENT_TABLE_NAME: The name of the parent table for this index."
	//   "SPANNER_IS_MANAGED: Whether or not the index is managed by Cloud Spanner."
	[Fact]
	public async Task ExecuteSql_InformationSchemaIndexes_HasParentTableNameAndIsManagedColumns()
	{
		// Arrange
		_database.ExecuteDdl("CREATE INDEX IX_TestTable_Name ON TestTable(Name)");

		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT INDEX_NAME, PARENT_TABLE_NAME, SPANNER_IS_MANAGED FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'TestTable'"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().NotBeEmpty();
		var metadata = result.Metadata;
		metadata.RowType.Fields.Select(f => f.Name).Should().Contain("PARENT_TABLE_NAME");
		metadata.RowType.Fields.Select(f => f.Name).Should().Contain("SPANNER_IS_MANAGED");

		// The PARENT_TABLE_NAME should be empty string and SPANNER_IS_MANAGED should be false
		var parentTableIdx = metadata.RowType.Fields.ToList().FindIndex(f => f.Name == "PARENT_TABLE_NAME");
		var isManagedIdx = metadata.RowType.Fields.ToList().FindIndex(f => f.Name == "SPANNER_IS_MANAGED");
		foreach (var row in result.Rows)
		{
			row.Values[parentTableIdx].StringValue.Should().Be("");
			row.Values[isManagedIdx].BoolValue.Should().BeFalse();
		}
	}

	// ─── INFORMATION_SCHEMA.INDEX_COLUMNS returns SPANNER_TYPE ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#index_columns
	//   "SPANNER_TYPE: The column type."
	[Fact]
	public async Task ExecuteSql_InformationSchemaIndexColumns_HasSpannerTypeColumn()
	{
		// Arrange
		_database.ExecuteDdl("CREATE INDEX IX_TestTable_Name ON TestTable(Name)");

		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = 'TestTable' AND INDEX_NAME = 'IX_TestTable_Name'"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().NotBeEmpty();
		var metadata = result.Metadata;
		metadata.RowType.Fields.Select(f => f.Name).Should().Contain("SPANNER_TYPE");

		var typeIdx = metadata.RowType.Fields.ToList().FindIndex(f => f.Name == "SPANNER_TYPE");
		foreach (var row in result.Rows)
		{
			row.Values[typeIdx].StringValue.Should().NotBeNullOrEmpty();
		}
	}

	// ─── INFORMATION_SCHEMA.TABLES returns SPANNER_STATE ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#tables
	//   "SPANNER_STATE: Possible values are 'COMMITTED' or 'IN_PROGRESS'."
	[Fact]
	public async Task ExecuteSql_InformationSchemaTables_HasSpannerStateColumn()
	{
		// Act
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT TABLE_NAME, SPANNER_STATE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TestTable'"
			},
			TestServerCallContext.Create());

		// Assert
		result.Rows.Should().HaveCount(1);
		var metadata = result.Metadata;
		metadata.RowType.Fields.Select(f => f.Name).Should().Contain("SPANNER_STATE");

		var stateIdx = metadata.RowType.Fields.ToList().FindIndex(f => f.Name == "SPANNER_STATE");
		result.Rows[0].Values[stateIdx].StringValue.Should().Be("COMMITTED");
	}

	// ─── UpdateDatabaseDdl returns error on failure instead of crashing ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.UpdateDatabaseDdlMetadata
	//   "If the operation failed, the error status is captured in the Operation's error field."
	[Fact]
	public async Task UpdateDatabaseDdl_FailingStatement_ReturnsOperationWithError()
	{
		// Arrange
		var adminService = new FakeDatabaseAdminService(_database, new FakeSpannerServerOptions
		{
			ProjectId = "test-project",
			InstanceId = "test-instance",
			DatabaseId = "test-db"
		});

		// Act: try to create a table that already exists
		var operation = await adminService.UpdateDatabaseDdl(
			new UpdateDatabaseDdlRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db",
				Statements = { "CREATE TABLE TestTable (X INT64) PRIMARY KEY (X)" }
			},
			TestServerCallContext.Create());

		// Assert: should return a done operation with Error set
		operation.Done.Should().BeTrue();
		operation.Error.Should().NotBeNull();
		operation.Error.Code.Should().Be((int)Google.Rpc.Code.AlreadyExists);
	}

	// Ref: Same as above — only statements before the failure should be recorded
	[Fact]
	public async Task UpdateDatabaseDdl_PartialFailure_OnlyAppliesStatementsBeforeError()
	{
		// Arrange
		var adminService = new FakeDatabaseAdminService(_database, new FakeSpannerServerOptions
		{
			ProjectId = "test-project",
			InstanceId = "test-instance",
			DatabaseId = "test-db"
		});

		// Act: first statement succeeds, second fails
		var operation = await adminService.UpdateDatabaseDdl(
			new UpdateDatabaseDdlRequest
			{
				Database = "projects/test-project/instances/test-instance/databases/test-db",
				Statements =
				{
					"CREATE TABLE NewTable1 (Id INT64) PRIMARY KEY (Id)",
					"CREATE TABLE TestTable (X INT64) PRIMARY KEY (X)", // already exists
					"CREATE TABLE NewTable2 (Id INT64) PRIMARY KEY (Id)"  // should not be applied
				}
			},
			TestServerCallContext.Create());

		// Assert
		operation.Error.Should().NotBeNull();

		// NewTable1 should exist (first statement succeeded)
		var result = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NewTable1'"
			},
			TestServerCallContext.Create());
		result.Rows.Should().HaveCount(1);

		// NewTable2 should NOT exist (third statement not applied)
		var result2 = await _service.ExecuteSql(
			new ExecuteSqlRequest
			{
				Session = _sessionName,
				Transaction = new TransactionSelector { SingleUse = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() } },
				Sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NewTable2'"
			},
			TestServerCallContext.Create());
		result2.Rows.Should().BeEmpty();
	}
}
