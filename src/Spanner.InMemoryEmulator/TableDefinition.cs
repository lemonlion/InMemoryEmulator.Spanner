using System.Collections.Concurrent;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Schema definition for a single Spanner table.
/// </summary>
public class TableDefinition
{
	public string Name { get; }
	public IReadOnlyList<ColumnDef> Columns { get; }
	public IReadOnlyList<string> PrimaryKeyColumns { get; }
	public string? ParentTable { get; }
	public OnDeleteAction OnDeleteAction { get; }

	/// <summary>CHECK constraints defined on this table.</summary>
	public List<CheckConstraint> CheckConstraints { get; } = [];

	/// <summary>FOREIGN KEY constraints defined on this table (this table is the referencing table).</summary>
	public List<ForeignKeyConstraint> ForeignKeys { get; } = [];

	internal ConcurrentDictionary<RowKey, RowData> Rows { get; } = new();

	public TableDefinition(
		string name,
		IReadOnlyList<ColumnDef> columns,
		IReadOnlyList<string> primaryKeyColumns,
		string? parentTable = null,
		OnDeleteAction onDeleteAction = OnDeleteAction.NoAction)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		Columns = columns ?? throw new ArgumentNullException(nameof(columns));
		PrimaryKeyColumns = primaryKeyColumns ?? throw new ArgumentNullException(nameof(primaryKeyColumns));
		ParentTable = parentTable;
		OnDeleteAction = onDeleteAction;
	}

	internal void ClearData()
	{
		Rows.Clear();
	}
}

/// <summary>
/// Defines the action taken on child rows when a parent row is deleted in an interleaved table.
/// </summary>
public enum OnDeleteAction
{
	/// <summary>Parent delete is blocked if child rows exist.</summary>
	NoAction,

	/// <summary>Deleting a parent row cascades to delete all child rows.</summary>
	Cascade
}

/// <summary>
/// A CHECK constraint on a table.
/// </summary>
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
public record CheckConstraint(string? Name, string Expression);

/// <summary>
/// A FOREIGN KEY constraint referencing another table.
/// </summary>
// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
public record ForeignKeyConstraint(
	string? Name,
	IReadOnlyList<string> Columns,
	string ReferencedTable,
	IReadOnlyList<string> ReferencedColumns,
	bool IsEnforced = true,
	ForeignKeyDeleteAction OnDelete = ForeignKeyDeleteAction.NoAction);

/// <summary>
/// Action taken on referencing rows when a referenced row is deleted via foreign key.
/// </summary>
public enum ForeignKeyDeleteAction
{
	NoAction,
	Cascade
}

/// <summary>
/// Definition of a stored view.
/// </summary>
public class ViewDefinition
{
	public string Name { get; }
	public string SqlBody { get; }

	public ViewDefinition(string name, string sqlBody)
	{
		Name = name;
		SqlBody = sqlBody;
	}
}

/// <summary>
/// Definition of a sequence (BIT_REVERSED_POSITIVE).
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create-sequence
/// </summary>
public class SequenceDefinition
{
	public string Name { get; }
	public string SequenceKind { get; }
	private long _counter;

	public SequenceDefinition(string name, string sequenceKind, long startWithCounter = 1)
	{
		Name = name;
		SequenceKind = sequenceKind;
		_counter = startWithCounter;
	}

	/// <summary>
	/// Returns next sequence value. For BIT_REVERSED_POSITIVE, bit-reverses the counter.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_next_sequence_value
	/// </summary>
	public long GetNextValue()
	{
		var val = Interlocked.Increment(ref _counter);
		return SequenceKind.Equals("bit_reversed_positive", StringComparison.OrdinalIgnoreCase)
			? BitReverse(val)
			: val;
	}

	public long GetInternalState() => Interlocked.Read(ref _counter);

	private static long BitReverse(long value)
	{
		// Reverse the bits of a 63-bit positive integer (keep sign bit 0)
		ulong v = (ulong)value;
		ulong result = 0;
		for (int i = 0; i < 63; i++)
		{
			result = (result << 1) | (v & 1);
			v >>= 1;
		}
		return (long)result;
	}
}

/// <summary>
/// Definition of a change stream.
/// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
/// </summary>
public class ChangeStreamDefinition
{
	public string Name { get; }

	/// <summary>True if the stream watches ALL tables/columns.</summary>
	public bool WatchesAll { get; set; }

	/// <summary>
	/// List of watched table specs. Each entry is (TableName, Columns?) where
	/// Columns is null for whole-table watching.
	/// </summary>
	public List<(string Table, List<string>? Columns)> WatchedTables { get; set; } = new();

	/// <summary>Parsed OPTIONS key-value pairs.</summary>
	public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	public ChangeStreamDefinition(string name)
	{
		Name = name;
	}
}
