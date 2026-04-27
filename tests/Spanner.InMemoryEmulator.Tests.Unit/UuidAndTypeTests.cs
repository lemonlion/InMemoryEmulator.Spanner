using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Xunit;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for UUID type support, NEW_UUID function, and type converter handling.
/// </summary>
public class UuidAndTypeTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		return db;
	}

	// ─── UUID DDL & DML ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "UUID: Encoded as string, in lower-case hexa-decimal format, as described in RFC 9562, section 4."
	[Fact]
	public void CreateTable_WithUuidColumn_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id UUID NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");

		// Verify column is recognized
		var rows = db.ExecuteQuery(
			"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'Id'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"].Should().Be("UUID");
	}

	[Fact]
	public void InsertAndQuery_UuidColumn_RoundTrips()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id UUID NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var uuid = Guid.NewGuid().ToString().ToLowerInvariant();
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = uuid, ["Val"] = 1L });

		var rows = db.ExecuteQuery($"SELECT Id, Val FROM T WHERE Id = '{uuid}'");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(uuid);
	}

	// ─── NEW_UUID Function ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
	//   "NEW_UUID() returns a UUID value."
	[Fact]
	public void NewUuid_ReturnsValidUuid()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });

		var rows = db.ExecuteQuery("SELECT NEW_UUID() AS R FROM T WHERE Id=1");
		rows.Should().ContainSingle();
		var value = rows[0]["R"]!.ToString()!;
		Guid.TryParse(value, out _).Should().BeTrue($"Expected valid UUID, got: '{value}'");
		value.Should().Be(value.ToLowerInvariant(), "UUID should be lower-case hex format");
	}

	[Fact]
	public void NewUuid_EachCallReturnsDifferentValue()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });

		var rows = db.ExecuteQuery("SELECT NEW_UUID() AS a, NEW_UUID() AS b FROM T WHERE Id=1");
		rows.Should().ContainSingle();
		rows[0]["a"]!.ToString().Should().NotBe(rows[0]["b"]!.ToString());
	}

	[Fact]
	public void NewUuid_CanInsertIntoUuidColumn()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id UUID NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var engine = new SqlEngine(db);
		var resultSet = engine.ExecuteSql("INSERT INTO T (Id, Val) VALUES (NEW_UUID(), 42)", null);

		var rows = db.ExecuteQuery("SELECT Id, Val FROM T WHERE Val = 42");
		rows.Should().ContainSingle();
		Guid.TryParse(rows[0]["Id"]!.ToString(), out _).Should().BeTrue();
	}

	// ─── TypeConverter UUID handling ───

	[Fact]
	public void TypeConverter_ToProtobufValue_Uuid_EncodesAsString()
	{
		var uuid = "550e8400-e29b-41d4-a716-446655440000";
		var result = TypeConverter.ToProtobufValue(uuid, TypeCode.Uuid);
		result.KindCase.Should().Be(Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue);
		result.StringValue.Should().Be(uuid);
	}

	[Fact]
	public void TypeConverter_FromProtobufValue_Uuid_DecodesAsString()
	{
		var uuid = "550e8400-e29b-41d4-a716-446655440000";
		var protoValue = Google.Protobuf.WellKnownTypes.Value.ForString(uuid);
		var result = TypeConverter.FromProtobufValue(protoValue, TypeCode.Uuid);
		result.Should().Be(uuid);
	}

	[Fact]
	public void TypeConverter_ToProtobufType_Uuid_SetsCorrectCode()
	{
		var type = TypeConverter.ToProtobufType(TypeCode.Uuid);
		type.Code.Should().Be(TypeCode.Uuid);
	}
}
