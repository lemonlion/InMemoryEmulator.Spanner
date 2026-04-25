using Google.Cloud.Spanner.V1;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Builds protobuf <c>ResultSet</c> and <c>PartialResultSet</c> from query results.
/// </summary>
internal static class ResultSetBuilder
{
	/// <summary>
	/// Builds a <see cref="ResultSet"/> from output column definitions and rows.
	/// </summary>
	public static ResultSet Build(
		IReadOnlyList<ColumnDef> outputColumns,
		IReadOnlyList<Dictionary<string, object?>> rows)
	{
		var metadata = BuildMetadata(outputColumns);
		var resultSet = new ResultSet { Metadata = metadata };

		foreach (var row in rows)
		{
			var listValue = new Google.Protobuf.WellKnownTypes.ListValue();
			foreach (var col in outputColumns)
			{
				row.TryGetValue(col.Name, out var value);
				listValue.Values.Add(TypeConverter.ToProtobufValue(value, col.SpannerType));
			}
			resultSet.Rows.Add(listValue);
		}

		return resultSet;
	}

	/// <summary>
	/// Builds <see cref="ResultSetMetadata"/> from output column definitions.
	/// </summary>
	public static ResultSetMetadata BuildMetadata(IReadOnlyList<ColumnDef> outputColumns)
	{
		var metadata = new ResultSetMetadata
		{
			RowType = new StructType()
		};

		foreach (var col in outputColumns)
		{
			metadata.RowType.Fields.Add(new StructType.Types.Field
			{
				Name = col.Name,
				Type = TypeConverter.ToProtobufType(col.SpannerType, col.ArrayElementType)
			});
		}

		return metadata;
	}
}
