using FluentAssertions;
using Google.Cloud.Spanner.V1;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace InMemoryEmulator.Spanner.Tests.Unit;

public class TypeConverterTests
{
	[Fact]
	public void Int64_RoundTrips_AsString()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSet
		//   "INT64 is encoded as string to avoid precision loss in JavaScript."
		var original = 42L;
		var protobuf = TypeConverter.ToProtobufValue(original, TypeCode.Int64);
		protobuf.StringValue.Should().Be("42");

		var roundTripped = TypeConverter.FromProtobufValue(protobuf, TypeCode.Int64);
		roundTripped.Should().Be(42L);
	}

	[Fact]
	public void Bool_RoundTrips()
	{
		var protobuf = TypeConverter.ToProtobufValue(true, TypeCode.Bool);
		protobuf.BoolValue.Should().BeTrue();

		var roundTripped = TypeConverter.FromProtobufValue(protobuf, TypeCode.Bool);
		roundTripped.Should().Be(true);
	}

	[Fact]
	public void String_RoundTrips()
	{
		var protobuf = TypeConverter.ToProtobufValue("hello", TypeCode.String);
		protobuf.StringValue.Should().Be("hello");

		var roundTripped = TypeConverter.FromProtobufValue(protobuf, TypeCode.String);
		roundTripped.Should().Be("hello");
	}

	[Fact]
	public void Null_RoundTrips()
	{
		var protobuf = TypeConverter.ToProtobufValue(null, TypeCode.String);
		protobuf.KindCase.Should().Be(Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue);

		var roundTripped = TypeConverter.FromProtobufValue(protobuf, TypeCode.String);
		roundTripped.Should().BeNull();
	}

	[Fact]
	public void Float64_RoundTrips()
	{
		var protobuf = TypeConverter.ToProtobufValue(3.14, TypeCode.Float64);
		protobuf.NumberValue.Should().Be(3.14);

		var roundTripped = TypeConverter.FromProtobufValue(protobuf, TypeCode.Float64);
		roundTripped.Should().Be(3.14);
	}
}
