using Google.Cloud.Spanner.V1;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Definition of a single column in a Spanner table.
/// </summary>
public class ColumnDef
{
	public string Name { get; }
	public TypeCode SpannerType { get; }
	public bool IsNullable { get; }
	public long? MaxLength { get; }
	public bool AllowCommitTimestamp { get; }
	public TypeCode? ArrayElementType { get; }
	public string? GeneratedExpression { get; }
	public bool IsStored { get; }
	public string? DefaultExpression { get; }

	public ColumnDef(
		string name,
		TypeCode spannerType,
		bool isNullable = true,
		long? maxLength = null,
		bool allowCommitTimestamp = false,
		TypeCode? arrayElementType = null,
		string? generatedExpression = null,
		bool isStored = false,
		string? defaultExpression = null)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		SpannerType = spannerType;
		IsNullable = isNullable;
		MaxLength = maxLength;
		AllowCommitTimestamp = allowCommitTimestamp;
		ArrayElementType = arrayElementType;
		GeneratedExpression = generatedExpression;
		IsStored = isStored;
		DefaultExpression = defaultExpression;
	}
}
