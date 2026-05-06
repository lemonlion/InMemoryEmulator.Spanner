using Google.Cloud.Spanner.V1;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Converts between .NET types, Spanner types, and protobuf <c>Value</c> representations.
/// </summary>
internal static class TypeConverter
{
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   Defines the mapping between Spanner type codes and their protobuf wire representations.

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSet
	//   "INT64 is encoded as string to avoid precision loss in JavaScript."

	/// <summary>
	/// Converts a .NET value to a protobuf <c>Value</c> for the given Spanner type.
	/// </summary>
	public static Google.Protobuf.WellKnownTypes.Value ToProtobufValue(object? value, TypeCode spannerType)
	{
		if (value is null)
		{
			return Google.Protobuf.WellKnownTypes.Value.ForNull();
		}

		return spannerType switch
		{
			TypeCode.Bool => Google.Protobuf.WellKnownTypes.Value.ForBool(Convert.ToBoolean(value)),
			TypeCode.Int64 => Google.Protobuf.WellKnownTypes.Value.ForString(Convert.ToInt64(value).ToString()),
			TypeCode.Float32 => Google.Protobuf.WellKnownTypes.Value.ForNumber(Convert.ToSingle(value)),
			TypeCode.Float64 => Google.Protobuf.WellKnownTypes.Value.ForNumber(Convert.ToDouble(value)),
			TypeCode.String => Google.Protobuf.WellKnownTypes.Value.ForString(FormatAsString(value)),
			TypeCode.Timestamp => Google.Protobuf.WellKnownTypes.Value.ForString(FormatTimestamp(value)),
			TypeCode.Date => Google.Protobuf.WellKnownTypes.Value.ForString(FormatDate(value)),
			TypeCode.Bytes => Google.Protobuf.WellKnownTypes.Value.ForString(Convert.ToBase64String((byte[])value)),
			TypeCode.Numeric => Google.Protobuf.WellKnownTypes.Value.ForString(value.ToString()!),
			TypeCode.Json => Google.Protobuf.WellKnownTypes.Value.ForString(value is System.Text.Json.JsonElement je ? je.GetRawText() : (string)value),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
			//   "UUID values are encoded as lowercase hyphenated strings (RFC 9562)."
			(TypeCode)17 => Google.Protobuf.WellKnownTypes.Value.ForString((string)value),
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "PROTO: Encoded as a base64-encoded string." / "ENUM: Encoded as a string containing the enum's name."
			(TypeCode)13 => Google.Protobuf.WellKnownTypes.Value.ForString(value is byte[] protoBytes ? Convert.ToBase64String(protoBytes) : value.ToString()!),
			(TypeCode)14 => Google.Protobuf.WellKnownTypes.Value.ForString(value.ToString()!),
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "ARRAY values are encoded as list_value."
			TypeCode.Array => ToProtobufArrayValue(value),
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "STRUCT values are encoded as list_value where field order matches the struct type definition."
			TypeCode.Struct => ToProtobufStructValue(value),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
			//   INTERVAL is serialized as its canonical string representation.
			TypeCode.Interval => Google.Protobuf.WellKnownTypes.Value.ForString(value.ToString()!),
			// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
			//   TOKENLIST is an internal type; fallback to string representation.
			TypeCode.Unspecified => Google.Protobuf.WellKnownTypes.Value.ForString(value.ToString()!),
			_ => throw new ArgumentException($"Unsupported Spanner type: {spannerType}")
		};
	}

	/// <summary>
	/// Converts a protobuf <c>Value</c> to a .NET value for the given Spanner type.
	/// </summary>
	public static object? FromProtobufValue(Google.Protobuf.WellKnownTypes.Value value, TypeCode spannerType, TypeCode? arrayElementType = null)
	{
		if (value.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue)
		{
			return null;
		}

		return spannerType switch
		{
			TypeCode.Bool => value.BoolValue,
			TypeCode.Int64 => long.Parse(value.StringValue),
			TypeCode.Float32 => (float)value.NumberValue,
			TypeCode.Float64 => value.NumberValue,
			TypeCode.String => value.StringValue,
			TypeCode.Timestamp => ParseTimestamp(value.StringValue),
			TypeCode.Date => ParseDate(value.StringValue),
			TypeCode.Bytes => Convert.FromBase64String(value.StringValue),
			TypeCode.Numeric => decimal.Parse(value.StringValue),
			TypeCode.Json => value.StringValue,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
			(TypeCode)17 => value.StringValue,
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "PROTO: Encoded as a base64-encoded string." / "ENUM: Encoded as a string."
			(TypeCode)13 => Convert.FromBase64String(value.StringValue),
			(TypeCode)14 => value.StringValue,
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "ARRAY values are encoded as list_value."
			TypeCode.Array => FromProtobufArrayValue(value, arrayElementType),
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "STRUCT values are encoded as list_value where field order matches the struct type definition."
			TypeCode.Struct => value.ListValue?.Values?.Select(v => (object?)v).ToList() as object,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
			TypeCode.Interval => value.StringValue,
			// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
			//   TOKENLIST is an internal type; fallback to string.
			TypeCode.Unspecified => value.StringValue,
			_ => throw new ArgumentException($"Unsupported Spanner type: {spannerType}")
		};
	}

	/// <summary>
	/// Builds a protobuf <c>Type</c> from a <see cref="TypeCode"/>.
	/// </summary>
	public static Google.Cloud.Spanner.V1.Type ToProtobufType(TypeCode typeCode, TypeCode? arrayElementType = null, string? protoTypeFqn = null)
	{
		var type = new Google.Cloud.Spanner.V1.Type { Code = typeCode };
		if (typeCode == TypeCode.Array && arrayElementType.HasValue)
		{
			type.ArrayElementType = new Google.Cloud.Spanner.V1.Type { Code = arrayElementType.Value };
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#type
			//   For ARRAY<PROTO> or ARRAY<ENUM>, the element type carries the proto_type_fqn.
			if (protoTypeFqn != null)
				type.ArrayElementType.ProtoTypeFqn = protoTypeFqn;
		}
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#type
		//   "proto_type_fqn: If code == PROTO or code == ENUM, then proto_type_fqn
		//    is the fully qualified name of the proto type."
		if (protoTypeFqn != null && typeCode is (TypeCode)13 or (TypeCode)14)
			type.ProtoTypeFqn = protoTypeFqn;
		return type;
	}

	/// <summary>
	/// Infers the Spanner <see cref="TypeCode"/> from a .NET runtime value.
	/// </summary>
	public static TypeCode InferTypeCodeFromValue(object? value) => value switch
	{
		null => TypeCode.String,
		bool => TypeCode.Bool,
		long or int or short or byte or sbyte => TypeCode.Int64,
		float => TypeCode.Float32,
		double => TypeCode.Float64,
		decimal => TypeCode.Numeric,
		string => TypeCode.String,
		DateTime dt when dt.Date == dt => TypeCode.Date,
		DateTime => TypeCode.Timestamp,
		DateTimeOffset => TypeCode.Timestamp,
		DateOnly => TypeCode.Date,
		byte[] => TypeCode.Bytes,
		System.Collections.IList => TypeCode.Array,
		SpannerTokenList => TypeCode.Unspecified,
		_ => TypeCode.String
	};

	private static string FormatAsString(object value) => value switch
	{
		DateTime dt when dt.Date == dt => dt.ToString("yyyy-MM-dd"),
		DateTime dt => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ"),
		DateTimeOffset dto => dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ"),
		_ => value.ToString()!
	};

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#timestamp_type
	//   "TIMESTAMP values are expressed as an RFC 3339 datetime."
	private static string FormatTimestamp(object value)
	{
		return value switch
		{
			DateTime dt => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ"),
			DateTimeOffset dto => dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ"),
			_ => throw new ArgumentException($"Cannot format {value.GetType().Name} as TIMESTAMP")
		};
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#date_type
	//   "DATE values are expressed as 'YYYY-MM-DD'."
	private static string FormatDate(object value)
	{
		return value switch
		{
			DateTime dt => dt.ToString("yyyy-MM-dd"),
			DateOnly d => d.ToString("yyyy-MM-dd"),
			_ => throw new ArgumentException($"Cannot format {value.GetType().Name} as DATE")
		};
	}

	private static DateTime ParseTimestamp(string value)
	{
		return DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
	}

	private static DateTime ParseDate(string value)
	{
		return DateTime.ParseExact(value, "yyyy-MM-dd", null);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "Encoded as list_value, where the list_value values are the array elements."
	private static Google.Protobuf.WellKnownTypes.Value ToProtobufArrayValue(object value)
	{
		var listValue = new Google.Protobuf.WellKnownTypes.ListValue();
		if (value is System.Collections.IEnumerable enumerable)
		{
			foreach (var item in enumerable)
			{
				listValue.Values.Add(item switch
				{
					null => Google.Protobuf.WellKnownTypes.Value.ForNull(),
					string s => Google.Protobuf.WellKnownTypes.Value.ForString(s),
					long l => Google.Protobuf.WellKnownTypes.Value.ForString(l.ToString()),
					double d => Google.Protobuf.WellKnownTypes.Value.ForNumber(d),
					bool b => Google.Protobuf.WellKnownTypes.Value.ForBool(b),
					Dictionary<string, object?> dict => ToProtobufStructValue(dict),
					IList<object?> structList => ToProtobufStructValue(structList),
					_ => Google.Protobuf.WellKnownTypes.Value.ForString(item.ToString()!)
				});
			}
		}
		var result = new Google.Protobuf.WellKnownTypes.Value();
		result.ListValue = listValue;
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "STRUCT values are encoded as list_value where field order matches the struct type definition."
	private static Google.Protobuf.WellKnownTypes.Value ToProtobufStructValue(object value)
	{
		var listValue = new Google.Protobuf.WellKnownTypes.ListValue();
		if (value is Dictionary<string, object?> dict)
		{
			foreach (var val in dict.Values)
			{
				listValue.Values.Add(val switch
				{
					null => Google.Protobuf.WellKnownTypes.Value.ForNull(),
					string s => Google.Protobuf.WellKnownTypes.Value.ForString(s),
					long l => Google.Protobuf.WellKnownTypes.Value.ForString(l.ToString()),
					double d => Google.Protobuf.WellKnownTypes.Value.ForNumber(d),
					bool b => Google.Protobuf.WellKnownTypes.Value.ForBool(b),
					_ => Google.Protobuf.WellKnownTypes.Value.ForString(val.ToString()!)
				});
			}
		}
		else if (value is IList<object?> list)
		{
			foreach (var val in list)
			{
				listValue.Values.Add(val switch
				{
					null => Google.Protobuf.WellKnownTypes.Value.ForNull(),
					Google.Protobuf.WellKnownTypes.Value protoVal => protoVal,
					string s => Google.Protobuf.WellKnownTypes.Value.ForString(s),
					long l => Google.Protobuf.WellKnownTypes.Value.ForString(l.ToString()),
					double d => Google.Protobuf.WellKnownTypes.Value.ForNumber(d),
					bool b => Google.Protobuf.WellKnownTypes.Value.ForBool(b),
					_ => Google.Protobuf.WellKnownTypes.Value.ForString(val?.ToString() ?? "")
				});
			}
		}
		var result = new Google.Protobuf.WellKnownTypes.Value();
		result.ListValue = listValue;
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   Array elements must be deserialized using the array_element_type.
	private static List<object?> FromProtobufArrayValue(Google.Protobuf.WellKnownTypes.Value value, TypeCode? elementType)
	{
		var result = new List<object?>();
		if (value.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.ListValue)
		{
			foreach (var item in value.ListValue.Values)
			{
				if (item.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue)
				{
					result.Add(null);
				}
				else if (elementType.HasValue)
				{
					result.Add(FromProtobufValue(item, elementType.Value));
				}
				else
				{
					// Fallback: infer from wire format
					result.Add(item.KindCase switch
					{
						Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue => item.StringValue,
						Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue => item.NumberValue,
						Google.Protobuf.WellKnownTypes.Value.KindOneofCase.BoolValue => item.BoolValue,
						_ => item.StringValue
					});
				}
			}
		}
		return result;
	}
}
