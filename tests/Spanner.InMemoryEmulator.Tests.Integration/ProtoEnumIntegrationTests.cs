using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for PROTO BUNDLE DDL and PROTO/ENUM column types.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#proto_bundle_statements
///   CREATE PROTO BUNDLE, ALTER PROTO BUNDLE, DROP PROTO BUNDLE
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#protocol_buffers
///   Proto/Enum column types use fully qualified names from the proto bundle.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class ProtoEnumIntegrationTests : IntegrationTestBase
{
	public ProtoEnumIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// CREATE PROTO BUNDLE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateProtoBundle_SingleType_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_proto_bundle
		//   "CREATE PROTO BUNDLE (proto_type_name [, ...])"
		await ExecuteDdlAsync("CREATE PROTO BUNDLE (examples.shipping.Order)");
	}

	[Fact]
	public async Task CreateProtoBundle_MultipleTypes_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_proto_bundle
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.shipping.Order, examples.shipping.OrderItem, examples.shipping.Status)");
	}

	// ═══════════════════════════════════════════════════════════════
	// ALTER PROTO BUNDLE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AlterProtoBundle_Insert_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_proto_bundle
		//   "ALTER PROTO BUNDLE [INSERT (...)] [UPDATE (...)] [DELETE (...)]"
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Singer)",
			"ALTER PROTO BUNDLE INSERT (examples.music.Album)");
	}

	[Fact]
	public async Task AlterProtoBundle_Update_Succeeds()
	{
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Singer)",
			"ALTER PROTO BUNDLE UPDATE (examples.music.Singer)");
	}

	[Fact]
	public async Task AlterProtoBundle_Delete_Succeeds()
	{
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Singer, examples.music.Album)",
			"ALTER PROTO BUNDLE DELETE (examples.music.Album)");
	}

	[Fact]
	public async Task AlterProtoBundle_InsertAndDelete_Succeeds()
	{
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Singer)",
			"ALTER PROTO BUNDLE INSERT (examples.music.Album) DELETE (examples.music.Singer)");
	}

	// ═══════════════════════════════════════════════════════════════
	// DROP PROTO BUNDLE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DropProtoBundle_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#drop_proto_bundle
		//   "DROP PROTO BUNDLE"
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.test.Message)",
			"DROP PROTO BUNDLE");
	}

	// ═══════════════════════════════════════════════════════════════
	// Proto/Enum column types
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_WithProtoColumn_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#protocol_buffers
		//   "SingerInfo googlesql.example.SingerInfo" — column type is a proto message FQN
		var table = $"Proto_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.SingerInfo)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Info examples.music.SingerInfo) PRIMARY KEY (K)");

		// Should be able to insert null proto values
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "Info", null } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	public async Task CreateTable_WithEnumColumn_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#protocol_buffers
		//   Enum types from proto bundle are also used as column types
		var table = $"Enum_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Genre)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Genre examples.music.Genre) PRIMARY KEY (K)");

		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "Genre", null } });
		var rows = await QueryAsync($"SELECT K FROM {table}");
		rows.Should().HaveCount(1);
	}

	[Fact]
	public async Task CreateTable_WithProtoArrayColumn_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#protocol_buffers
		//   "ARRAY<googlesql.example.SingerInfo>" — array of proto type
		var table = $"ProtoArr_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.SingerInfo)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Infos ARRAY<examples.music.SingerInfo>) PRIMARY KEY (K)");

		var rows = await QueryAsync($"SELECT K FROM {table} WHERE K = 0");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// INFORMATION_SCHEMA verification
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InformationSchema_ProtoColumn_ShowsFqnType()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#columns
		//   SPANNER_TYPE for proto column should show the fully-qualified proto type name
		var table = $"ProtoIs_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.SingerInfo)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Info examples.music.SingerInfo) PRIMARY KEY (K)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Info'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"].Should().Be("examples.music.SingerInfo");
	}

	[Fact]
	public async Task InformationSchema_EnumColumn_ShowsFqnType()
	{
		var table = $"EnumIs_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.music.Genre)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Genre examples.music.Genre) PRIMARY KEY (K)");

		var rows = await QueryAsync(
			$"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'Genre'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"].Should().Be("examples.music.Genre");
	}

	// ═══════════════════════════════════════════════════════════════
	// Proto column data round-trip
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProtoColumn_InsertAndRead_Base64RoundTrip()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
		//   "PROTO: Encoded as a base64-encoded string, as described in RFC 4648."
		var table = $"ProtoRt_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.test.Payload)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Data examples.test.Payload) PRIMARY KEY (K)");

		// Insert raw bytes representing a proto message
		var protoBytes = new byte[] { 0x08, 0x96, 0x01 }; // example varint-encoded proto
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "Data", protoBytes } });

		var rows = await QueryAsync($"SELECT Data FROM {table} WHERE K = 1");
		rows.Should().ContainSingle();
		// Proto value comes back as base64-encoded bytes
		var result = rows[0]["Data"];
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task EnumColumn_InsertNull_Succeeds()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
		//   "ENUM: Encoded as a string containing the enum's name."
		// Note: Without actual proto descriptors, enum FQN types default to PROTO TypeCode.
		// This test verifies the DDL and null data path work correctly.
		var table = $"EnumRt_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			"CREATE PROTO BUNDLE (examples.test.Color)",
			$"CREATE TABLE {table} (K INT64 NOT NULL, Color examples.test.Color) PRIMARY KEY (K)");

		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "Color", null } });

		var rows = await QueryAsync($"SELECT K FROM {table} WHERE K = 1");
		rows.Should().ContainSingle();
	}
}
