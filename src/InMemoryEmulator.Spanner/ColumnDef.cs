using Google.Cloud.Spanner.V1;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace InMemoryEmulator.Spanner;

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
	public bool IsHidden { get; }
	/// <summary>
	/// Fully-qualified proto type name for PROTO/ENUM columns (e.g. "examples.music.SingerInfo").
	/// </summary>
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#type
	//   "proto_type_fqn: If code == PROTO or code == ENUM, then proto_type_fqn is the fully
	//    qualified name of the proto type representing the proto/enum definition."
	public string? ProtoTypeFqn { get; }

	public ColumnDef(
		string name,
		TypeCode spannerType,
		bool isNullable = true,
		long? maxLength = null,
		bool allowCommitTimestamp = false,
		TypeCode? arrayElementType = null,
		string? generatedExpression = null,
		bool isStored = false,
		string? defaultExpression = null,
		bool isHidden = false,
		string? protoTypeFqn = null)
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
		IsHidden = isHidden;
		ProtoTypeFqn = protoTypeFqn;
	}
}
