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

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.CreateSession
	public override Task<Session> CreateSession(CreateSessionRequest request, ServerCallContext context)
	{
		LogRequest(nameof(CreateSession), request);
		CheckFault(nameof(CreateSession), request);

		var session = _sessionManager.CreateSession(request.Database);
		return Task.FromResult(session);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchCreateSessions
	public override Task<BatchCreateSessionsResponse> BatchCreateSessions(BatchCreateSessionsRequest request, ServerCallContext context)
	{
		LogRequest(nameof(BatchCreateSessions), request);
		CheckFault(nameof(BatchCreateSessions), request);

		var multiplexed = request.SessionTemplate?.Multiplexed ?? false;
		var sessions = _sessionManager.BatchCreateSessions(request.Database, request.SessionCount, multiplexed);
		var response = new BatchCreateSessionsResponse();
		response.Session.AddRange(sessions);
		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.GetSession
	public override Task<Session> GetSession(GetSessionRequest request, ServerCallContext context)
	{
		LogRequest(nameof(GetSession), request);
		CheckFault(nameof(GetSession), request);

		var session = _sessionManager.GetSession(request.Name);
		if (session == null)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Session not found: {request.Name}"));
		}
		return Task.FromResult(session);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.DeleteSession
	public override Task<Empty> DeleteSession(DeleteSessionRequest request, ServerCallContext context)
	{
		LogRequest(nameof(DeleteSession), request);
		CheckFault(nameof(DeleteSession), request);

		_sessionManager.DeleteSession(request.Name);
		return Task.FromResult(new Empty());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ListSessions
	public override Task<ListSessionsResponse> ListSessions(ListSessionsRequest request, ServerCallContext context)
	{
		LogRequest(nameof(ListSessions), request);
		CheckFault(nameof(ListSessions), request);

		var sessions = _sessionManager.ListSessions(request.Database);
		var response = new ListSessionsResponse();
		response.Sessions.AddRange(sessions);
		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BeginTransaction
	public override Task<Transaction> BeginTransaction(BeginTransactionRequest request, ServerCallContext context)
	{
		LogRequest(nameof(BeginTransaction), request);
		CheckFault(nameof(BeginTransaction), request);

		ValidateSession(request.Session);
		var transaction = _transactionManager.BeginTransaction(request.Session, request.Options);
		return Task.FromResult(transaction);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	public override Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
	{
		LogRequest(nameof(Commit), request);
		CheckFault(nameof(Commit), request);

		ValidateSession(request.Session);

		var commitTimestamp = DateTimeOffset.UtcNow;
		var mutationExecutor = new MutationExecutor(_database);

		// Collect mutations from both the request and any buffered transaction mutations
		var allMutations = new List<Mutation>();

		if (request.TransactionId != null && !request.TransactionId.IsEmpty)
		{
			if (_transactionManager.TryGetByBytes(request.TransactionId, out var txnState) && txnState != null)
			{
				allMutations.AddRange(txnState.BufferedMutations);
				_transactionManager.MarkCommitted(txnState.Id);
			}
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
			throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
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

		return Task.FromResult(response);
	}

	/// <summary>
	/// Counts the number of mutation operations in a single Mutation message.
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.CommitResponse.CommitStats
	//   "mutation_count: The total number of mutations for the transaction."
	private static int CountMutationOperations(Mutation m) => m.OperationCase switch
	{
		Mutation.OperationOneofCase.Insert => m.Insert.Values.Count,
		Mutation.OperationOneofCase.InsertOrUpdate => m.InsertOrUpdate.Values.Count,
		Mutation.OperationOneofCase.Replace => m.Replace.Values.Count,
		Mutation.OperationOneofCase.Update => m.Update.Values.Count,
		Mutation.OperationOneofCase.Delete => m.Delete.KeySet.Keys.Count + (m.Delete.KeySet.All ? 1 : 0),
		_ => 0
	};

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
	public override Task<Empty> Rollback(RollbackRequest request, ServerCallContext context)
	{
		LogRequest(nameof(Rollback), request);
		CheckFault(nameof(Rollback), request);

		ValidateSession(request.Session);

		if (_transactionManager.TryGetByBytes(request.TransactionId, out var txnState) && txnState != null)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
			//   "Rolls back a transaction, releasing any locks it holds."
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

		return Task.FromResult(new Empty());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteSql
	public override Task<ResultSet> ExecuteSql(ExecuteSqlRequest request, ServerCallContext context)
	{
		LogRequest(nameof(ExecuteSql), request);
		CheckFault(nameof(ExecuteSql), request);

		ValidateSession(request.Session);
		_sqlLog.Add(new SqlLogEntry(request.Sql, null, null, DateTimeOffset.UtcNow));

		try
		{
			var txnState = ResolveTransactionState(request.Session, request.Transaction);
			var engine = new SqlEngine(_database, txnState?.DmlUndoLog);
			var parameters = SqlEngine.ExtractParameters(request);
			var resultSet = engine.ExecuteSql(request.Sql, parameters);

			SetTransactionMetadata(request.Transaction, txnState, resultSet);

			return Task.FromResult(resultSet);
		}
		catch (InvalidOperationException ex)
		{
			throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
		}
		catch (NotSupportedException ex)
		{
			throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteStreamingSql
	public override async Task ExecuteStreamingSql(ExecuteSqlRequest request, Grpc.Core.IServerStreamWriter<PartialResultSet> responseStream, ServerCallContext context)
	{
		LogRequest(nameof(ExecuteStreamingSql), request);
		CheckFault(nameof(ExecuteStreamingSql), request);

		ValidateSession(request.Session);
		_sqlLog.Add(new SqlLogEntry(request.Sql, null, null, DateTimeOffset.UtcNow));

		try
		{
			var txnState = ResolveTransactionState(request.Session, request.Transaction);
			var engine = new SqlEngine(_database, txnState?.DmlUndoLog);
			var parameters = SqlEngine.ExtractParameters(request);
			var resultSet = engine.ExecuteSql(request.Sql, parameters);

			SetTransactionMetadata(request.Transaction, txnState, resultSet);

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
			throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
		}
		catch (NotSupportedException ex)
		{
			throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
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

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.ExecuteBatchDml
	//   "Statements are executed serially... Execution stops after the first failed statement."
	public override Task<ExecuteBatchDmlResponse> ExecuteBatchDml(ExecuteBatchDmlRequest request, ServerCallContext context)
	{
		LogRequest(nameof(ExecuteBatchDml), request);
		CheckFault(nameof(ExecuteBatchDml), request);

		ValidateSession(request.Session);

		var response = new ExecuteBatchDmlResponse();
		var txnState = ResolveTransactionState(request.Session, request.Transaction);
		var engine = new SqlEngine(_database, txnState?.DmlUndoLog);

		foreach (var statement in request.Statements)
		{
			_sqlLog.Add(new SqlLogEntry(statement.Sql, null, null, DateTimeOffset.UtcNow));
			try
			{
				var parameters = SqlEngine.ExtractParameters(statement.Sql, statement.Params, statement.ParamTypes);
				var resultSet = engine.ExecuteSql(statement.Sql, parameters);
				response.ResultSets.Add(resultSet);
			}
			catch (Exception ex)
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#executebatchdmlresponse
				//   "If a statement fails, the status in the response body identifies the cause."
				response.Status = new Google.Rpc.Status
				{
					Code = (int)Google.Rpc.Code.InvalidArgument,
					Message = ex.Message
				};
				break;
			}
		}

		// If all statements succeeded
		response.Status ??= new Google.Rpc.Status { Code = (int)Google.Rpc.Code.Ok };

		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Read
	//   "Reads rows from the database using key lookups and scans."
	public override Task<ResultSet> Read(ReadRequest request, ServerCallContext context)
	{
		LogRequest(nameof(Read), request);
		CheckFault(nameof(Read), request);

		ValidateSession(request.Session);

		try
		{
			var resultSet = ExecuteRead(request);
			return Task.FromResult(resultSet);
		}
		catch (InvalidOperationException ex)
		{
			throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.StreamingRead
	//   "Like Read, except returns the result set as a stream."
	public override async Task StreamingRead(ReadRequest request, Grpc.Core.IServerStreamWriter<PartialResultSet> responseStream, ServerCallContext context)
	{
		LogRequest(nameof(StreamingRead), request);
		CheckFault(nameof(StreamingRead), request);

		ValidateSession(request.Session);

		try
		{
			var resultSet = ExecuteRead(request);

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
	}

	private ResultSet ExecuteRead(ReadRequest request)
	{
		if (!_database.Schema.TryGetTable(request.Table, out var tableDef) || tableDef == null)
			throw new InvalidOperationException($"Table '{request.Table}' not found.");

		// Determine which columns to return
		var requestedColumns = request.Columns.Count > 0
			? request.Columns.ToList()
			: tableDef.Columns.Select(c => c.Name).ToList();

		// Get all matching rows based on KeySet
		var matchingRows = new List<Dictionary<string, object?>>();

		if (request.KeySet != null && request.KeySet.All)
		{
			// KeySet.All — return all rows
			matchingRows.AddRange(tableDef.Rows.Values.Select(
				r => new Dictionary<string, object?>(r.Columns, StringComparer.OrdinalIgnoreCase)));
		}
		else if (request.KeySet != null)
		{
			// Specific keys
			foreach (var key in request.KeySet.Keys)
			{
				var pkValues = key.Values
					.Select((v, i) => TypeConverter.FromProtobufValue(v, tableDef.Columns[i].SpannerType))
					.ToArray();
				var rowKey = new RowKey(pkValues);
				if (tableDef.Rows.TryGetValue(rowKey, out var rowData))
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
						matchingRows.Add(new Dictionary<string, object?>(kvp.Value.Columns, StringComparer.OrdinalIgnoreCase));
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
				var colDef = tableDef.Columns.FirstOrDefault(c =>
					string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
				return colDef != null
					? new ColumnDef(colDef.Name, colDef.SpannerType)
					: new ColumnDef(colName, Google.Cloud.Spanner.V1.TypeCode.String);
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

		var pkValues = keyValues.Values
			.Select((v, i) => TypeConverter.FromProtobufValue(v, tableDef.Columns[i].SpannerType))
			.ToArray();
		return new RowKey(pkValues);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionQuery
	//   "Creates a set of partition tokens that can be used to execute a query operation in parallel."
	//   In-memory emulator: returns a single partition covering the entire result.
	public override Task<PartitionResponse> PartitionQuery(PartitionQueryRequest request, ServerCallContext context)
	{
		LogRequest(nameof(PartitionQuery), request);
		CheckFault(nameof(PartitionQuery), request);

		ValidateSession(request.Session);

		var response = new PartitionResponse();
		response.Partitions.Add(new Partition
		{
			PartitionToken = Google.Protobuf.ByteString.CopyFromUtf8("partition-0")
		});
		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.PartitionRead
	//   "Creates a set of partition tokens that can be used to execute a read operation in parallel."
	//   In-memory emulator: returns a single partition covering the entire result.
	public override Task<PartitionResponse> PartitionRead(PartitionReadRequest request, ServerCallContext context)
	{
		LogRequest(nameof(PartitionRead), request);
		CheckFault(nameof(PartitionRead), request);

		ValidateSession(request.Session);

		var response = new PartitionResponse();
		response.Partitions.Add(new Partition
		{
			PartitionToken = Google.Protobuf.ByteString.CopyFromUtf8("partition-0")
		});
		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchWrite
	//   "Batches the supplied mutation groups in a collection of efficient transactions."
	//   "Each mutation group is applied atomically. The mutation groups may be applied out of order."
	public override async Task BatchWrite(BatchWriteRequest request, Grpc.Core.IServerStreamWriter<BatchWriteResponse> responseStream, ServerCallContext context)
	{
		LogRequest(nameof(BatchWrite), request);
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
