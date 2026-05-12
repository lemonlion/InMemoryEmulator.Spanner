using System.Collections.Concurrent;
using Google.Cloud.Spanner.V1;
using Google.Protobuf.WellKnownTypes;

namespace InMemoryEmulator.Spanner;

/// <summary>
/// Manages transaction lifecycle for the fake Spanner service.
/// </summary>
internal class TransactionManager
{
	private readonly ConcurrentDictionary<string, TransactionState> _transactions = new();

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BeginTransaction
	//   "Begins a new transaction."
	public Transaction BeginTransaction(string sessionName, TransactionOptions options)
	{
		var txnId = Guid.NewGuid().ToByteArray();
		var transaction = new Transaction
		{
			Id = Google.Protobuf.ByteString.CopyFrom(txnId),
			ReadTimestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
		};

		var state = new TransactionState(
			Convert.ToBase64String(txnId),
			sessionName,
			options);

		_transactions[state.Id] = state;
		return transaction;
	}

	public bool TryGet(string transactionId, out TransactionState? state)
	{
		return _transactions.TryGetValue(transactionId, out state);
	}

	public bool TryGetByBytes(Google.Protobuf.ByteString transactionIdBytes, out TransactionState? state)
	{
		var id = Convert.ToBase64String(transactionIdBytes.ToByteArray());
		return TryGet(id, out state);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	//   "Commits a transaction."
	public void MarkCommitted(string transactionId)
	{
		if (_transactions.TryGetValue(transactionId, out var state))
		{
			state.IsCommitted = true;
		}
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Rollback
	//   "Rolls back a transaction, releasing any locks it holds."
	public void MarkRolledBack(string transactionId)
	{
		if (_transactions.TryGetValue(transactionId, out var state))
		{
			state.IsRolledBack = true;
		}
	}

	public void Remove(string transactionId)
	{
		_transactions.TryRemove(transactionId, out _);
	}
}

/// <summary>
/// Represents a before-image of a row that was modified by DML within a transaction.
/// Used to undo DML changes on rollback.
/// </summary>
internal record DmlUndoEntry(string TableName, RowKey Key, RowData? OriginalRow);

internal class TransactionState
{
	public string Id { get; }
	public string SessionName { get; }
	public TransactionOptions Options { get; }
	public DateTimeOffset CreatedAt { get; }
	public List<Mutation> BufferedMutations { get; } = new();
	public List<DmlUndoEntry> DmlUndoLog { get; } = new();
	public DateTimeOffset? ReadTimestamp { get; set; }
	public Transaction? ProtoTransaction { get; set; }
	public bool IsCommitted { get; set; }
	public bool IsRolledBack { get; set; }

	public TransactionState(string id, string sessionName, TransactionOptions options)
	{
		Id = id;
		SessionName = sessionName;
		Options = options;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	public bool IsReadOnly => Options.ModeCase == TransactionOptions.ModeOneofCase.ReadOnly;
	public bool IsReadWrite => Options.ModeCase == TransactionOptions.ModeOneofCase.ReadWrite;
	public bool IsPartitionedDml => Options.ModeCase == TransactionOptions.ModeOneofCase.PartitionedDml;
}
