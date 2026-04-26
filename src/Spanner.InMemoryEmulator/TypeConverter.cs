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
			TypeCode.Json => Google.Protobuf.WellKnownTypes.Value.ForString((string)value),
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "ARRAY values are encoded as list_value."
			TypeCode.Array => ToProtobufArrayValue(value),
			_ => throw new ArgumentException($"Unsupported Spanner type: {spannerType}")
		};
	}

	/// <summary>
	/// Converts a protobuf <c>Value</c> to a .NET value for the given Spanner type.
	/// </summary>
	public static object? FromProtobufValue(Google.Protobuf.WellKnownTypes.Value value, TypeCode spannerType)
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
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			//   "ARRAY values are encoded as list_value."
			TypeCode.Array => FromProtobufArrayValue(value),
			_ => throw new ArgumentException($"Unsupported Spanner type: {spannerType}")
		};
	}

	/// <summary>
	/// Builds a protobuf <c>Type</c> from a <see cref="TypeCode"/>.
	/// </summary>
	public static Google.Cloud.Spanner.V1.Type ToProtobufType(TypeCode typeCode, TypeCode? arrayElementType = null)
	{
		var type = new Google.Cloud.Spanner.V1.Type { Code = typeCode };
		if (typeCode == TypeCode.Array && arrayElementType.HasValue)
		{
			type.ArrayElementType = new Google.Cloud.Spanner.V1.Type { Code = arrayElementType.Value };
		}
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
					_ => Google.Protobuf.WellKnownTypes.Value.ForString(item.ToString()!)
				});
			}
		}
		var result = new Google.Protobuf.WellKnownTypes.Value();
		result.ListValue = listValue;
		return result;
	}

	private static List<object?> FromProtobufArrayValue(Google.Protobuf.WellKnownTypes.Value value)
	{
		var result = new List<object?>();
		if (value.KindCase == Google.Protobuf.WellKnownTypes.Value.KindOneofCase.ListValue)
		{
			foreach (var item in value.ListValue.Values)
			{
				result.Add(item.KindCase switch
				{
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue => null,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue => item.StringValue,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue => item.NumberValue,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.BoolValue => item.BoolValue,
					_ => item.StringValue
				});
			}
		}
		return result;
	}
}
