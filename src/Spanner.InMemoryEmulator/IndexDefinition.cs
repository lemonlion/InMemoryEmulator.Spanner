namespace Spanner.InMemoryEmulator;

/// <summary>
/// Schema definition for a secondary index.
/// </summary>
public class IndexDefinition
{
	public string Name { get; }
	public string TableName { get; }
	public IReadOnlyList<IndexColumn> Columns { get; }
	public IReadOnlyList<string> StoringColumns { get; }
	public bool IsUnique { get; }
	public bool IsNullFiltered { get; }

	public IndexDefinition(
		string name,
		string tableName,
		IReadOnlyList<IndexColumn> columns,
		IReadOnlyList<string>? storingColumns = null,
		bool isUnique = false,
		bool isNullFiltered = false)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
		Columns = columns ?? throw new ArgumentNullException(nameof(columns));
		StoringColumns = storingColumns ?? Array.Empty<string>();
		IsUnique = isUnique;
		IsNullFiltered = isNullFiltered;
	}
}

/// <summary>
/// A column within an index, with sort direction.
/// </summary>
public class IndexColumn
{
	public string Name { get; }
	public SortOrder Order { get; }

	public IndexColumn(string name, SortOrder order = SortOrder.Asc)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		Order = order;
	}
}

/// <summary>
/// Sort direction for index columns and ORDER BY clauses.
/// </summary>
public enum SortOrder
{
	Asc,
	Desc
}
