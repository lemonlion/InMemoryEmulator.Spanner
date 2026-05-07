using System.Collections.Concurrent;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Records a gRPC request received by the fake Spanner service.
/// </summary>
public record RequestLogEntry(
	string MethodName,
	IMessage Request,
	DateTimeOffset Timestamp);

/// <summary>
/// Records a SQL statement executed against the fake Spanner service.
/// </summary>
public record SqlLogEntry(
	string Sql,
	IReadOnlyDictionary<string, object?>? Parameters,
	string? TransactionId,
	DateTimeOffset Timestamp);

/// <summary>
/// Raised when a gRPC request is received, before execution begins.
/// </summary>
public record SpannerRequestEvent(
	string MethodName,
	IMessage Request,
	DateTimeOffset Timestamp);

/// <summary>
/// Raised after a gRPC request has been executed (or has faulted).
/// </summary>
public record SpannerResponseEvent(
	string MethodName,
	IMessage Request,
	IMessage? Response,
	TimeSpan Duration,
	StatusCode? StatusCode,
	DateTimeOffset Timestamp);

/// <summary>
/// gRPC service implementation for Spanner.SpannerBase.
/// Backs a <see cref="FakeSpannerServer"/> with in-memory storage.
/// Exposes fault injection and request logging for test assertions.
/// </summary>
public class FakeSpannerService : Google.Cloud.Spanner.V1.Spanner.SpannerBase
{
	private readonly InMemorySpannerDatabase _database;
	private readonly SessionManager _sessionManager = new();
	private readonly TransactionManager _transactionManager = new();
	private readonly string _databaseName;
	private readonly ConcurrentBag<RequestLogEntry> _requestLog = new();
	private readonly ConcurrentBag<SqlLogEntry> _sqlLog = new();

	/// <summary>
	/// Inject faults into any gRPC call. Return non-null <see cref="RpcException"/> to
	/// short-circuit the call. Parameters: (rpcMethodName, request).
	/// </summary>
	public Func<string, IMessage, RpcException?>? FaultInjector { get; set; }

	/// <summary>
	/// Optional callback invoked when a gRPC request is received, before execution.
	/// This is an observation hook — it cannot modify or cancel the request.
	/// </summary>
	public Action<SpannerRequestEvent>? OnRequestReceived { get; set; }

	/// <summary>
	/// Optional callback invoked after a gRPC request has been executed.
	/// Includes the response, duration, and gRPC status code.
	/// For streaming responses, fires once after the stream completes with Response = null.
	/// </summary>
	public Action<SpannerResponseEvent>? OnResponseSent { get; set; }

	/// <summary>All gRPC requests received, in order.</summary>
	public IReadOnlyList<RequestLogEntry> RequestLog => _requestLog.ToArray();

	/// <summary>All SQL statements executed (queries + DML).</summary>
	public IReadOnlyList<SqlLogEntry> SqlLog => _sqlLog.ToArray();

	/// <summary>Clear all log entries.</summary>
	public void ClearLogs()
	{
		_requestLog.Clear();
		_sqlLog.Clear();
	}

	internal SessionManager SessionManager => _sessionManager;
	internal TransactionManager TransactionManager => _transactionManager;

	public FakeSpannerService(InMemorySpannerDatabase database, FakeSpannerServerOptions options)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_databaseName = $"projects/{options.ProjectId}/instances/{options.InstanceId}/databases/{options.DatabaseId}";
	}

	private void LogRequest(string method, IMessage request)
	{
		_requestLog.Add(new RequestLogEntry(method, request, DateTimeOffset.UtcNow));
	}

	private void CheckFault(string method, IMessage request)
	{
		var fault = FaultInjector?.Invoke(method, request);
		if (fault != null) throw fault;
	}

	private void NotifyRequestReceived(string method, IMessage request, DateTimeOffset timestamp)
	{
		try { OnRequestReceived?.Invoke(new SpannerRequestEvent(method, request, timestamp)); }
		catch { /* Observer errors must not affect server behaviour */ }
	}

	private void NotifyResponseSent(string method, IMessage request, IMessage? response, TimeSpan duration, StatusCode statusCode, DateTimeOffset timestamp)
	{
		try { OnResponseSent?.Invoke(new SpannerResponseEvent(method, request, response, duration, statusCode, timestamp)); }
		catch { /* Observer errors must not affect server behaviour */ }
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.CreateSession
	public override Task<Session> CreateSession(CreateSessionRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(CreateSession), request);
		NotifyRequestReceived(nameof(CreateSession), request, startTime);
		try
		{
			CheckFault(nameof(CreateSession), request);
			var session = _sessionManager.CreateSession(request.Database);
			NotifyResponseSent(nameof(CreateSession), request, session, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(session);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(CreateSession), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchCreateSessions
	public override Task<BatchCreateSessionsResponse> BatchCreateSessions(BatchCreateSessionsRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(BatchCreateSessions), request);
		NotifyRequestReceived(nameof(BatchCreateSessions), request, startTime);
		try
		{
			CheckFault(nameof(BatchCreateSessions), request);
			var multiplexed = request.SessionTemplate?.Multiplexed ?? false;
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.BatchCreateSessionsRequest
			//   "Required. The number of sessions to be created in this batch call. At least one session is created."
			var count = Math.Max(1, request.SessionCount);
			var sessions = _sessionManager.BatchCreateSessions(request.Database, count, multiplexed);
			var response = new BatchCreateSessionsResponse();
			response.Session.AddRange(sessions);
			NotifyResponseSent(nameof(BatchCreateSessions), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(BatchCreateSessions), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.GetSession
	public override Task<Session> GetSession(GetSessionRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(GetSession), request);
		NotifyRequestReceived(nameof(GetSession), request, startTime);
		try
		{
			CheckFault(nameof(GetSession), request);
			var session = _sessionManager.GetSession(request.Name);
			if (session == null)
			{
				throw new RpcException(new Status(StatusCode.NotFound, $"Session not found: {request.Name}"));
			}
			NotifyResponseSent(nameof(GetSession), request, session, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(session);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(GetSession), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.DeleteSession
	public override Task<Empty> DeleteSession(DeleteSessionRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(DeleteSession), request);
		NotifyRequestReceived(nameof(DeleteSession), request, startTime);
		try
		{
			CheckFault(nameof(DeleteSession), request);
			ValidateSession(request.Name);
			_sessionManager.DeleteSession(request.Name);
			var result = new Empty();
			NotifyResponseSent(nameof(DeleteSession), request, result, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(result);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(DeleteSession), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ListSessions
	public override Task<ListSessionsResponse> ListSessions(ListSessionsRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(ListSessions), request);
		NotifyRequestReceived(nameof(ListSessions), request, startTime);
		try
		{
			CheckFault(nameof(ListSessions), request);
			var sessions = _sessionManager.ListSessions(request.Database);
			var response = new ListSessionsResponse();
			response.Sessions.AddRange(sessions);
			NotifyResponseSent(nameof(ListSessions), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(ListSessions), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BeginTransaction
	public override Task<Transaction> BeginTransaction(BeginTransactionRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(BeginTransaction), request);
		NotifyRequestReceived(nameof(BeginTransaction), request, startTime);
		try
		{
			CheckFault(nameof(BeginTransaction), request);
			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.BeginTransactionRequest
			//   "options: Required. Options for the new transaction."
			//   "mode: Required. The type of transaction."
			if (request.Options == null || request.Options.ModeCase == TransactionOptions.ModeOneofCase.None)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument,
					"Transaction options with a valid mode (read_write, read_only, or partitioned_dml) are required."));
			}

			var transaction = _transactionManager.BeginTransaction(request.Session, request.Options);

			// Store proto on state so PartitionQuery/PartitionRead can echo it back
			if (_transactionManager.TryGetByBytes(transaction.Id, out var txnState) && txnState != null)
			{
				txnState.ProtoTransaction = transaction;
			}

			NotifyResponseSent(nameof(BeginTransaction), request, transaction, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(transaction);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(BeginTransaction), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	public override Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(Commit), request);
		NotifyRequestReceived(nameof(Commit), request, startTime);
		try
		{
			CheckFault(nameof(Commit), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
			//   "Required. The transaction in which to commit." — one of transaction_id or single_use_transaction must be set
			if ((request.TransactionId == null || request.TransactionId.IsEmpty) && request.SingleUseTransaction == null)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument,
					"Either transaction_id or single_use_transaction must be specified."));
			}

			var commitTimestamp = DateTimeOffset.UtcNow;
			var mutationExecutor = new MutationExecutor(_database);

			// Collect mutations from both the request and any buffered transaction mutations
			var allMutations = new List<Mutation>();
			TransactionState? txnState = null;

			if (request.TransactionId != null && !request.TransactionId.IsEmpty)
			{
				if (!_transactionManager.TryGetByBytes(request.TransactionId, out txnState) || txnState == null)
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
					//   "transaction_id: Commit a previously-started transaction."
					//   If the transaction is not found, return NOT_FOUND.
					throw new RpcException(new Status(StatusCode.NotFound,
						"Transaction not found."));
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitRequest
				//   "transaction_id: Commit a previously-started transaction."
				//   A committed or rolled-back transaction is no longer active.
				if (txnState.IsCommitted)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot commit a transaction that has already been committed."));
				}
				if (txnState.IsRolledBack)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot commit a transaction that has already been rolled back."));
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
				//   "Read-only transactions do not support commit."
				if (txnState.IsReadOnly)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot commit a read-only transaction."));
				}

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
				//   "Partitioned DML transactions auto-commit; explicit Commit is not supported."
				if (txnState.IsPartitionedDml)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot commit a partitioned DML transaction."));
				}

				allMutations.AddRange(txnState.BufferedMutations);
			}

			allMutations.AddRange(request.Mutations);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
			//   "Commits a transaction. The request includes the mutations to be applied to rows in the database."
			try
			{
				mutationExecutor.ApplyMutations(allMutations, commitTimestamp);
			}
			catch (InvalidOperationException ex)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
				//   Insert: "Fails if any of the rows already exist." → ALREADY_EXISTS
				//   Update: "If any of the rows does not already exist, the transaction fails with error NOT_FOUND."
				var code = ex.Message.Contains("already exists")
					? StatusCode.AlreadyExists
					: ex.Message.Contains("does not exist")
						? StatusCode.NotFound
						: StatusCode.FailedPrecondition;
				throw new RpcException(new Status(code, ex.Message));
			}

			// Mark committed only after mutations succeed — prevents corrupted state on failure
			if (txnState != null)
			{
				_transactionManager.MarkCommitted(txnState.Id);
			}

			var response = new CommitResponse
			{
				CommitTimestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(commitTimestamp)
			};

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitResponse
			//   "commit_stats: The statistics about this Commit. Not returned by default."
			if (request.ReturnCommitStats)
			{
				response.CommitStats = new CommitResponse.Types.CommitStats
				{
					MutationCount = allMutations.Sum(m => CountMutationOperations(m))
				};
			}

			NotifyResponseSent(nameof(Commit), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(Commit), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	/// <summary>
	/// Counts the number of mutation operations in a single Mutation message.
	/// A mutation counts as (number of rows) × (number of columns) for writes,
	/// and (number of keys) for deletes.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitResponse.CommitStats
	//   "mutation_count: The total number of mutations for the transaction."
	// Ref: https://cloud.google.com/spanner/quotas
	//   "A mutation is counted for each column value written... each row deleted counts as one mutation."
	private static int CountMutationOperations(Mutation m) => m.OperationCase switch
	{
		Mutation.OperationOneofCase.Insert => m.Insert.Values.Count * m.Insert.Columns.Count,
		Mutation.OperationOneofCase.InsertOrUpdate => m.InsertOrUpdate.Values.Count * m.InsertOrUpdate.Columns.Count,
		Mutation.OperationOneofCase.Replace => m.Replace.Values.Count * m.Replace.Columns.Count,
		Mutation.OperationOneofCase.Update => m.Update.Values.Count * m.Update.Columns.Count,
		Mutation.OperationOneofCase.Delete => m.Delete.KeySet.Keys.Count + (m.Delete.KeySet.All ? 1 : 0) + m.Delete.KeySet.Ranges.Count,
		_ => 0
	};

	/// <summary>
	/// Checks if a SQL statement is a DML (INSERT, UPDATE, DELETE) statement.
	/// </summary>
	private static bool IsDmlStatement(string sql)
	{
		var trimmed = sql.TrimStart();
		return trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
			   trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
			   trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
	public override Task<Empty> Rollback(RollbackRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(Rollback), request);
		NotifyRequestReceived(nameof(Rollback), request, startTime);
		try
		{
			CheckFault(nameof(Rollback), request);

			ValidateSession(request.Session);

			if (_transactionManager.TryGetByBytes(request.TransactionId, out var txnState) && txnState != null)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
				//   "Rollback returns OK if it successfully aborts the transaction, the transaction
				//    was already aborted, or the transaction isn't found."
				//   A committed transaction cannot be rolled back — skip undo logic but return OK.
				if (!txnState.IsCommitted && !txnState.IsRolledBack)
				{
					// Undo DML changes applied during this transaction (in reverse order).
					for (int i = txnState.DmlUndoLog.Count - 1; i >= 0; i--)
					{
						var entry = txnState.DmlUndoLog[i];
						if (_database.Schema.TryGetTable(entry.TableName, out var table) && table != null)
						{
							if (entry.OriginalRow == null)
								table.Rows.TryRemove(entry.Key, out _); // Was INSERT → undo = delete
							else
								table.Rows[entry.Key] = entry.OriginalRow; // Was UPDATE/DELETE → undo = restore
						}
					}

					_transactionManager.MarkRolledBack(txnState.Id);
				}
			}

			var result = new Empty();
			NotifyResponseSent(nameof(Rollback), request, result, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(result);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(Rollback), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteSql
	public override Task<ResultSet> ExecuteSql(ExecuteSqlRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(ExecuteSql), request);
		NotifyRequestReceived(nameof(ExecuteSql), request, startTime);
		try
		{
			CheckFault(nameof(ExecuteSql), request);

			ValidateSession(request.Session);
			_sqlLog.Add(new SqlLogEntry(request.Sql, null, null, DateTimeOffset.UtcNow));

			try
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest
				//   "Standard DML statements require a read-write transaction. To protect against replays,
				//    single-use transactions are not supported."
				if (IsDmlStatement(request.Sql) &&
					request.Transaction?.SelectorCase == TransactionSelector.SelectorOneofCase.SingleUse)
				{
					throw new RpcException(new Status(StatusCode.InvalidArgument,
						"DML statements may not be performed in single-use transactions, to protect against replays."));
				}

				var txnState = ResolveTransactionState(request.Session, request.Transaction);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
				//   Read-only transactions cannot contain DML statements.
				if (txnState?.IsReadOnly == true && IsDmlStatement(request.Sql))
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"DML statements cannot be executed in a read-only transaction."));

				var engine = new SqlEngine(_database, txnState?.DmlUndoLog);
				var parameters = SqlEngine.ExtractParameters(request);
				var resultSet = engine.ExecuteSql(request.Sql, parameters);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetStats
				//   "Partitioned DML doesn't offer exactly-once semantics, so it returns a lower bound
				//    of the rows modified."
				if (txnState?.IsPartitionedDml == true && resultSet.Stats != null &&
					resultSet.Stats.RowCountCase == ResultSetStats.RowCountOneofCase.RowCountExact)
				{
					var count = resultSet.Stats.RowCountExact;
					resultSet.Stats.RowCountLowerBound = count;
				}

				SetTransactionMetadata(request.Transaction, txnState, resultSet);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest.QueryMode
				ApplyQueryMode(request.QueryMode, resultSet);

				NotifyResponseSent(nameof(ExecuteSql), request, resultSet, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
				return Task.FromResult(resultSet);
			}
			catch (InvalidOperationException ex)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#google.rpc.Code
				//   Duplicate primary key → ALREADY_EXISTS (6)
				//   Foreign key violation → FAILED_PRECONDITION (9)
				var code = ex.Message.Contains("already exists")
					? StatusCode.AlreadyExists
					: ex.Message.Contains("Foreign key constraint")
					? StatusCode.FailedPrecondition
					: StatusCode.InvalidArgument;
				throw new RpcException(new Status(code, ex.Message));
			}
			catch (NotSupportedException ex)
			{
				throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
			}
			catch (Exception ex) when (ex is not RpcException)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
			}
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(ExecuteSql), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteStreamingSql
	public override async Task ExecuteStreamingSql(ExecuteSqlRequest request, Grpc.Core.IServerStreamWriter<PartialResultSet> responseStream, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(ExecuteStreamingSql), request);
		NotifyRequestReceived(nameof(ExecuteStreamingSql), request, startTime);
		try
		{
			CheckFault(nameof(ExecuteStreamingSql), request);

			ValidateSession(request.Session);
			_sqlLog.Add(new SqlLogEntry(request.Sql, null, null, DateTimeOffset.UtcNow));

			try
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest
				//   "Standard DML statements require a read-write transaction. To protect against replays,
				//    single-use transactions are not supported."
				if (IsDmlStatement(request.Sql) &&
					request.Transaction?.SelectorCase == TransactionSelector.SelectorOneofCase.SingleUse)
				{
					throw new RpcException(new Status(StatusCode.InvalidArgument,
						"DML statements may not be performed in single-use transactions, to protect against replays."));
				}

				var txnState = ResolveTransactionState(request.Session, request.Transaction);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
				//   Read-only transactions cannot contain DML statements.
				if (txnState?.IsReadOnly == true && IsDmlStatement(request.Sql))
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"DML statements cannot be executed in a read-only transaction."));

				var engine = new SqlEngine(_database, txnState?.DmlUndoLog);
				var parameters = SqlEngine.ExtractParameters(request);
				var resultSet = engine.ExecuteSql(request.Sql, parameters);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetStats
				//   "Partitioned DML doesn't offer exactly-once semantics, so it returns a lower bound
				//    of the rows modified."
				if (txnState?.IsPartitionedDml == true && resultSet.Stats != null &&
					resultSet.Stats.RowCountCase == ResultSetStats.RowCountOneofCase.RowCountExact)
				{
					var count = resultSet.Stats.RowCountExact;
					resultSet.Stats.RowCountLowerBound = count;
				}

				SetTransactionMetadata(request.Transaction, txnState, resultSet);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest.QueryMode
				ApplyQueryMode(request.QueryMode, resultSet);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.PartialResultSet
				//   "Stream the entire result set as a single PartialResultSet (simplification)."
				var partialResultSet = new PartialResultSet
				{
					Metadata = resultSet.Metadata
				};

				foreach (var row in resultSet.Rows)
				{
					partialResultSet.Values.Add(row.Values);
				}

				if (resultSet.Stats != null)
				{
					partialResultSet.Stats = resultSet.Stats;
				}

				await responseStream.WriteAsync(partialResultSet);
			}
			catch (InvalidOperationException ex)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#google.rpc.Code
				//   Duplicate primary key → ALREADY_EXISTS (6)
				//   Foreign key violation → FAILED_PRECONDITION (9)
				var code = ex.Message.Contains("already exists")
					? StatusCode.AlreadyExists
					: ex.Message.Contains("Foreign key constraint")
					? StatusCode.FailedPrecondition
					: StatusCode.InvalidArgument;
				throw new RpcException(new Status(code, ex.Message));
			}
			catch (NotSupportedException ex)
			{
				throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
			}
			catch (Exception ex) when (ex is not RpcException)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
			}

			NotifyResponseSent(nameof(ExecuteStreamingSql), request, null, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(ExecuteStreamingSql), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	/// <summary>
	/// Resolves the active transaction state from a TransactionSelector.
	/// For Begin: creates a new transaction. For Id: looks up existing.
	/// </summary>
	private TransactionState? ResolveTransactionState(string sessionName, TransactionSelector? selector)
	{
		if (selector == null) return null;

		switch (selector.SelectorCase)
		{
			case TransactionSelector.SelectorOneofCase.Begin:
				var txn = _transactionManager.BeginTransaction(sessionName, selector.Begin);
				_transactionManager.TryGetByBytes(txn.Id, out var newState);
				// Store the proto Transaction so we can set it in metadata later
				if (newState != null) newState.ProtoTransaction = txn;
				return newState;

			case TransactionSelector.SelectorOneofCase.Id:
				_transactionManager.TryGetByBytes(selector.Id, out var existingState);
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionSelector
				//   "id: Execute the read or SQL query in a previously-started transaction."
				//   A committed or rolled-back transaction is no longer "started".
				if (existingState != null && existingState.IsCommitted)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot use a transaction that has already been committed."));
				}
				if (existingState != null && existingState.IsRolledBack)
				{
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"Cannot use a transaction that has already been rolled back."));
				}
				return existingState;

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionSelector
			//   "Execute the read or SQL query in a temporary transaction.
			//    This is the most efficient way to execute a transaction that consists of a single SQL query."
			case TransactionSelector.SelectorOneofCase.SingleUse:
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions.ReadOnly
				//   Stale reads use SingleUse with ReadOnly options. The in-memory emulator has no MVCC
				//   so we always return current data, but we accept the staleness parameters and
				//   create a proper transaction to return a valid ReadTimestamp.
				var singleUseTxn = _transactionManager.BeginTransaction(sessionName, selector.SingleUse);
				_transactionManager.TryGetByBytes(singleUseTxn.Id, out var singleUseState);
				if (singleUseState != null) singleUseState.ProtoTransaction = singleUseTxn;
				return singleUseState;

			default:
				return null;
		}
	}

	/// <summary>
	/// Sets transaction metadata on the result set for Begin and SingleUse transactions.
	/// </summary>
	private static void SetTransactionMetadata(TransactionSelector? selector, TransactionState? txnState, ResultSet resultSet)
	{
		if (selector == null || txnState?.ProtoTransaction == null) return;

		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetMetadata
		//   "If the read or SQL query began a transaction as a side-effect, the information
		//    about the new transaction is yielded here."
		if (selector.SelectorCase is TransactionSelector.SelectorOneofCase.Begin
			or TransactionSelector.SelectorOneofCase.SingleUse)
		{
			resultSet.Metadata ??= new ResultSetMetadata();
			resultSet.Metadata.Transaction = txnState.ProtoTransaction;
		}
	}

	/// <summary>
	/// Applies QueryMode semantics to a ResultSet.
	/// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest.QueryMode
	/// </summary>
	private static void ApplyQueryMode(ExecuteSqlRequest.Types.QueryMode queryMode, ResultSet resultSet)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSet
		//   "DML statements always produce stats containing the number of rows modified,
		//    unless executed using the ExecuteSqlRequest.QueryMode.PLAN ExecuteSqlRequest.query_mode."
		// Preserve the RowCountExact/RowCountLowerBound from DML execution when adding query statistics.
		var existingRowCountExact = resultSet.Stats?.RowCountExact ?? 0;
		var existingRowCountCase = resultSet.Stats?.RowCountCase ?? ResultSetStats.RowCountOneofCase.None;

		switch (queryMode)
		{
			// Ref: "NORMAL: The default mode. Only the statement results are returned."
			case ExecuteSqlRequest.Types.QueryMode.Normal:
				break;

			// Ref: "PLAN: This mode returns only the query plan, without any results or execution statistics information."
			case ExecuteSqlRequest.Types.QueryMode.Plan:
				resultSet.Rows.Clear();
				resultSet.Stats = new ResultSetStats
				{
					QueryPlan = new QueryPlan()
				};
				break;

			// Ref: "PROFILE: This mode returns the query plan, overall execution statistics,
			//   operator level execution statistics along with the results."
			case ExecuteSqlRequest.Types.QueryMode.Profile:
				resultSet.Stats = new ResultSetStats
				{
					QueryPlan = new QueryPlan(),
					QueryStats = new Struct()
				};
				resultSet.Stats.QueryStats.Fields.Add("rows_returned", Value.ForString(resultSet.Rows.Count.ToString()));
				resultSet.Stats.QueryStats.Fields.Add("elapsed_time", Value.ForString("0 msecs"));
				resultSet.Stats.QueryStats.Fields.Add("cpu_time", Value.ForString("0 msecs"));
				if (existingRowCountCase == ResultSetStats.RowCountOneofCase.RowCountExact)
					resultSet.Stats.RowCountExact = existingRowCountExact;
				break;

			// Ref: "WITH_STATS: This mode returns the overall execution statistics along with the results."
			case ExecuteSqlRequest.Types.QueryMode.WithStats:
				resultSet.Stats = new ResultSetStats
				{
					QueryStats = new Struct()
				};
				resultSet.Stats.QueryStats.Fields.Add("rows_returned", Value.ForString(resultSet.Rows.Count.ToString()));
				resultSet.Stats.QueryStats.Fields.Add("elapsed_time", Value.ForString("0 msecs"));
				resultSet.Stats.QueryStats.Fields.Add("cpu_time", Value.ForString("0 msecs"));
				if (existingRowCountCase == ResultSetStats.RowCountOneofCase.RowCountExact)
					resultSet.Stats.RowCountExact = existingRowCountExact;
				break;

			// Ref: "WITH_PLAN_AND_STATS: This mode returns the query plan, overall execution statistics along with the results."
			case ExecuteSqlRequest.Types.QueryMode.WithPlanAndStats:
				resultSet.Stats = new ResultSetStats
				{
					QueryPlan = new QueryPlan(),
					QueryStats = new Struct()
				};
				resultSet.Stats.QueryStats.Fields.Add("rows_returned", Value.ForString(resultSet.Rows.Count.ToString()));
				resultSet.Stats.QueryStats.Fields.Add("elapsed_time", Value.ForString("0 msecs"));
				resultSet.Stats.QueryStats.Fields.Add("cpu_time", Value.ForString("0 msecs"));
				if (existingRowCountCase == ResultSetStats.RowCountOneofCase.RowCountExact)
					resultSet.Stats.RowCountExact = existingRowCountExact;
				break;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteBatchDml
	//   "Statements are executed serially... Execution stops after the first failed statement."
	public override Task<ExecuteBatchDmlResponse> ExecuteBatchDml(ExecuteBatchDmlRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(ExecuteBatchDml), request);
		NotifyRequestReceived(nameof(ExecuteBatchDml), request, startTime);
		try
		{
			CheckFault(nameof(ExecuteBatchDml), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
			//   "Must be a read-write transaction. To protect against replays,
			//    single-use transactions are not supported."
			if (request.Transaction?.SelectorCase == TransactionSelector.SelectorOneofCase.SingleUse)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument,
					"ExecuteBatchDml does not support single-use transactions, to protect against replays."));
			}

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
			//   "Required. The list of statements to execute in this batch. Statements are
			//    executed serially, such that the effects of statement i are visible to statement i+1.
			//    At least one statement must be provided."
			if (request.Statements.Count == 0)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument,
					"No statements provided in ExecuteBatchDml request."));
			}

			var response = new ExecuteBatchDmlResponse();
			var txnState = ResolveTransactionState(request.Session, request.Transaction);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.TransactionOptions
			//   Read-only transactions cannot contain DML statements.
			if (txnState?.IsReadOnly == true)
				throw new RpcException(new Status(StatusCode.FailedPrecondition,
					"DML statements cannot be executed in a read-only transaction."));

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlRequest
			//   "Must be a read-write transaction." — partitioned DML is not read-write.
			if (txnState?.IsPartitionedDml == true)
				throw new RpcException(new Status(StatusCode.FailedPrecondition,
					"ExecuteBatchDml is not supported for partitioned DML transactions."));

			var engine = new SqlEngine(_database, txnState?.DmlUndoLog);
			var isFirst = true;

			foreach (var statement in request.Statements)
			{
				_sqlLog.Add(new SqlLogEntry(statement.Sql, null, null, DateTimeOffset.UtcNow));
				try
				{
					var parameters = SqlEngine.ExtractParameters(statement.Sql, statement.Params, statement.ParamTypes);
					var resultSet = engine.ExecuteSql(statement.Sql, parameters);

					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteBatchDmlResponse
					//   "Only the first ResultSet in the response contains valid ResultSetMetadata."
					if (isFirst)
					{
						SetTransactionMetadata(request.Transaction, txnState, resultSet);
						isFirst = false;
					}

					response.ResultSets.Add(resultSet);
				}
				catch (Exception ex)
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#executebatchdmlresponse
					//   "If a statement fails, the status in the response body identifies the cause."
					var errorCode = ex.Message.Contains("already exists")
						? Google.Rpc.Code.AlreadyExists
						: Google.Rpc.Code.InvalidArgument;
					response.Status = new Google.Rpc.Status
					{
						Code = (int)errorCode,
						Message = ex.Message
					};
					break;
				}
			}

			// If all statements succeeded
			response.Status ??= new Google.Rpc.Status { Code = (int)Google.Rpc.Code.Ok };

			NotifyResponseSent(nameof(ExecuteBatchDml), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(ExecuteBatchDml), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Read
	//   "Reads rows from the database using key lookups and scans."
	public override Task<ResultSet> Read(ReadRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(Read), request);
		NotifyRequestReceived(nameof(Read), request, startTime);
		try
		{
			CheckFault(nameof(Read), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
			//   "transaction: The transaction to use. If none is provided, the default is
			//    a temporary read-only transaction with strong concurrency."
			var txnState = ResolveTransactionState(request.Session, request.Transaction);

			try
			{
				var resultSet = ExecuteRead(request);

				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSetMetadata
				//   "If the read or SQL query began a transaction as a side-effect, the information
				//    about the new transaction is yielded here."
				SetTransactionMetadata(request.Transaction, txnState, resultSet);

				NotifyResponseSent(nameof(Read), request, resultSet, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
				return Task.FromResult(resultSet);
			}
			catch (InvalidOperationException ex)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
			}
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(Read), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.StreamingRead
	//   "Like Read, except returns the result set as a stream."
	public override async Task StreamingRead(ReadRequest request, Grpc.Core.IServerStreamWriter<PartialResultSet> responseStream, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(StreamingRead), request);
		NotifyRequestReceived(nameof(StreamingRead), request, startTime);
		try
		{
			CheckFault(nameof(StreamingRead), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
			//   "transaction: The transaction to use."
			var txnState = ResolveTransactionState(request.Session, request.Transaction);

			try
			{
				var resultSet = ExecuteRead(request);

				// Set transaction metadata for Begin/SingleUse selectors
				SetTransactionMetadata(request.Transaction, txnState, resultSet);

				var partialResultSet = new PartialResultSet
				{
					Metadata = resultSet.Metadata
				};
				foreach (var row in resultSet.Rows)
				{
					partialResultSet.Values.Add(row.Values);
				}

				await responseStream.WriteAsync(partialResultSet);
			}
			catch (InvalidOperationException ex)
			{
				throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
			}

			NotifyResponseSent(nameof(StreamingRead), request, null, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(StreamingRead), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	private ResultSet ExecuteRead(ReadRequest request)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
		//   "Required. The name of the table in the database to be read."
		if (!_database.Schema.TryGetTable(request.Table, out var tableDef) || tableDef == null)
			throw new RpcException(new Status(StatusCode.NotFound, $"Table not found: {request.Table}"));

		// Determine which columns to return
		var requestedColumns = request.Columns.Count > 0
			? request.Columns.ToList()
			: tableDef.Columns.Select(c => c.Name).ToList();

		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ReadRequest
		//   "Required. The columns of table to be returned for each row matching this request."
		//   If a requested column does not exist, return NOT_FOUND.
		foreach (var colName in requestedColumns)
		{
			if (!tableDef.Columns.Any(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase)))
			{
				throw new RpcException(new Status(StatusCode.NotFound, $"Column not found: {colName}"));
			}
		}

		// Get all matching rows based on KeySet
		var matchingRows = new List<Dictionary<string, object?>>();

		if (request.KeySet != null && request.KeySet.All)
		{
			// KeySet.All — return all rows
			matchingRows.AddRange(tableDef.Rows.Values
				.Where(r => !tableDef.IsRowExpired(r))
				.Select(
				r => new Dictionary<string, object?>(r.Columns, StringComparer.OrdinalIgnoreCase)));
		}
		else if (request.KeySet != null)
		{
			// Specific keys
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.KeySet
			//   Key values correspond to primary key columns in their definition order.
			foreach (var key in request.KeySet.Keys)
			{
				var pkValues = key.Values
					.Select((v, i) =>
					{
						var pkColName = tableDef.PrimaryKeyColumns[i];
						var colDef = tableDef.Columns.First(c => string.Equals(c.Name, pkColName, StringComparison.OrdinalIgnoreCase));
						return TypeConverter.FromProtobufValue(v, colDef.SpannerType);
					})
					.ToArray();
				var rowKey = new RowKey(pkValues);
				if (tableDef.Rows.TryGetValue(rowKey, out var rowData) && !tableDef.IsRowExpired(rowData))
				{
					matchingRows.Add(new Dictionary<string, object?>(rowData.Columns, StringComparer.OrdinalIgnoreCase));
				}
			}

			// Key ranges
			foreach (var range in request.KeySet.Ranges)
			{
				var startClosed = range.StartKeyTypeCase == KeyRange.StartKeyTypeOneofCase.StartClosed ? range.StartClosed : null;
				var startOpen = range.StartKeyTypeCase == KeyRange.StartKeyTypeOneofCase.StartOpen ? range.StartOpen : null;
				var endClosed = range.EndKeyTypeCase == KeyRange.EndKeyTypeOneofCase.EndClosed ? range.EndClosed : null;
				var endOpen = range.EndKeyTypeCase == KeyRange.EndKeyTypeOneofCase.EndOpen ? range.EndOpen : null;

				var startKey = ExtractKeyFromRange(startClosed, startOpen, tableDef, out var startInclusive);
				var endKey = ExtractKeyFromRange(endClosed, endOpen, tableDef, out var endInclusive);

				foreach (var kvp in tableDef.Rows)
				{
					var startCmp = startKey != null ? kvp.Key.CompareTo(startKey) : 1;
					var endCmp = endKey != null ? kvp.Key.CompareTo(endKey) : -1;

					var afterStart = startKey == null || (startInclusive ? startCmp >= 0 : startCmp > 0);
					var beforeEnd = endKey == null || (endInclusive ? endCmp <= 0 : endCmp < 0);

					if (afterStart && beforeEnd)
					{
						if (!tableDef.IsRowExpired(kvp.Value))
						{
							matchingRows.Add(new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase));
						}
					}
				}
			}
		}

		// Apply LIMIT
		if (request.Limit > 0)
		{
			matchingRows = matchingRows.Take((int)request.Limit).ToList();
		}

		// Build output columns
		var outputColumns = requestedColumns
			.Select(colName =>
			{
				var colDef = tableDef.Columns.First(c =>
					string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
				return new ColumnDef(colDef.Name, colDef.SpannerType, protoTypeFqn: colDef.ProtoTypeFqn, arrayElementType: colDef.ArrayElementType);
			})
			.ToList();

		// Project to requested columns only
		var projectedRows = matchingRows.Select(row =>
		{
			var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			foreach (var colName in requestedColumns)
			{
				row.TryGetValue(colName, out var val);
				projected[colName] = val;
			}
			return projected;
		}).ToList();

		return ResultSetBuilder.Build(outputColumns, projectedRows);
	}

	private static RowKey? ExtractKeyFromRange(
		Google.Protobuf.WellKnownTypes.ListValue? closed,
		Google.Protobuf.WellKnownTypes.ListValue? open,
		TableDefinition tableDef,
		out bool inclusive)
	{
		var keyValues = closed ?? open;
		inclusive = closed != null;

		if (keyValues == null || keyValues.Values.Count == 0)
			return null;

		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.KeyRange
		//   Key range boundaries correspond to primary key columns in their definition order.
		var pkValues = keyValues.Values
			.Select((v, i) =>
			{
				var pkColName = tableDef.PrimaryKeyColumns[i];
				var colDef = tableDef.Columns.First(c => string.Equals(c.Name, pkColName, StringComparison.OrdinalIgnoreCase));
				return TypeConverter.FromProtobufValue(v, colDef.SpannerType);
			})
			.ToArray();
		return new RowKey(pkValues);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionQuery
	//   "Creates a set of partition tokens that can be used to execute a query operation in parallel."
	//   In-memory emulator: returns a single partition covering the entire result.
	public override Task<PartitionResponse> PartitionQuery(PartitionQueryRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(PartitionQuery), request);
		NotifyRequestReceived(nameof(PartitionQuery), request, startTime);
		try
		{
			CheckFault(nameof(PartitionQuery), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#partitionqueryrequest
			//   "Read-only snapshot transactions are supported."
			var txnState = ResolveTransactionState(request.Session, request.Transaction);

			var response = new PartitionResponse();
			response.Partitions.Add(new Partition
			{
				PartitionToken = Google.Protobuf.ByteString.CopyFromUtf8("partition-0")
			});

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#partitionresponse
			//   "transaction: Transaction created by this request."
			if (txnState?.ProtoTransaction != null)
			{
				response.Transaction = txnState.ProtoTransaction;
			}

			NotifyResponseSent(nameof(PartitionQuery), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(PartitionQuery), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionRead
	//   "Creates a set of partition tokens that can be used to execute a read operation in parallel."
	//   In-memory emulator: returns a single partition covering the entire result.
	public override Task<PartitionResponse> PartitionRead(PartitionReadRequest request, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(PartitionRead), request);
		NotifyRequestReceived(nameof(PartitionRead), request, startTime);
		try
		{
			CheckFault(nameof(PartitionRead), request);

			ValidateSession(request.Session);

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#partitionreadrequest
			//   "Read only snapshot transactions are supported."
			var txnState = ResolveTransactionState(request.Session, request.Transaction);

			var response = new PartitionResponse();
			response.Partitions.Add(new Partition
			{
				PartitionToken = Google.Protobuf.ByteString.CopyFromUtf8("partition-0")
			});

			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#partitionresponse
			//   "transaction: Transaction created by this request."
			if (txnState?.ProtoTransaction != null)
			{
				response.Transaction = txnState.ProtoTransaction;
			}

			NotifyResponseSent(nameof(PartitionRead), request, response, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
			return Task.FromResult(response);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(PartitionRead), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchWrite
	//   "Batches the supplied mutation groups in a collection of efficient transactions."
	//   "Each mutation group is applied atomically. The mutation groups may be applied out of order."
	public override async Task BatchWrite(BatchWriteRequest request, Grpc.Core.IServerStreamWriter<BatchWriteResponse> responseStream, ServerCallContext context)
	{
		var startTime = DateTimeOffset.UtcNow;
		LogRequest(nameof(BatchWrite), request);
		NotifyRequestReceived(nameof(BatchWrite), request, startTime);
		try
		{
			CheckFault(nameof(BatchWrite), request);

			ValidateSession(request.Session);

			var mutationExecutor = new MutationExecutor(_database);

			for (var i = 0; i < request.MutationGroups.Count; i++)
			{
				var group = request.MutationGroups[i];
				var commitTimestamp = DateTimeOffset.UtcNow;

				var response = new BatchWriteResponse();
				response.Indexes.Add(i);

				try
				{
					mutationExecutor.ApplyMutations(group.Mutations, commitTimestamp);
					response.Status = new Google.Rpc.Status { Code = (int)Google.Rpc.Code.Ok };
					response.CommitTimestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(commitTimestamp);
				}
				catch (Exception ex)
				{
					response.Status = new Google.Rpc.Status
					{
						Code = (int)Google.Rpc.Code.InvalidArgument,
						Message = ex.Message
					};
				}

				await responseStream.WriteAsync(response);
			}

			NotifyResponseSent(nameof(BatchWrite), request, null, DateTimeOffset.UtcNow - startTime, StatusCode.OK, DateTimeOffset.UtcNow);
		}
		catch (RpcException ex)
		{
			NotifyResponseSent(nameof(BatchWrite), request, null, DateTimeOffset.UtcNow - startTime, ex.StatusCode, DateTimeOffset.UtcNow);
			throw;
		}
	}

	private void ValidateSession(string sessionName)
	{
		if (!_sessionManager.TryGetSession(sessionName, out _))
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner
			//   "If the session does not exist, returns NOT_FOUND."
			throw new RpcException(new Status(StatusCode.NotFound, $"Session not found: {sessionName}"));
		}
		_sessionManager.UpdateLastUsed(sessionName);
	}
}
