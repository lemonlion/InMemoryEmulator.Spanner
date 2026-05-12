namespace InMemoryEmulator.Spanner;

/// <summary>
/// Stores column values for a single row in an in-memory Spanner table.
/// </summary>
public class RowData
{
	/// <summary>Column name → .NET value.</summary>
	public Dictionary<string, object?> Columns { get; }

	/// <summary>When this row was last committed.</summary>
	public DateTimeOffset CommitTimestamp { get; set; }

	public RowData(Dictionary<string, object?> columns, DateTimeOffset commitTimestamp)
	{
		Columns = columns ?? throw new ArgumentNullException(nameof(columns));
		CommitTimestamp = commitTimestamp;
	}

	public RowData(Dictionary<string, object?> columns)
		: this(columns, DateTimeOffset.UtcNow)
	{
	}
}
