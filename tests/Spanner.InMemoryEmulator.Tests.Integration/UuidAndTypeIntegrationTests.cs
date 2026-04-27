using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;
using Xunit;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for UUID type support, NEW_UUID function, and PROTO/ENUM DDL stubs.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class UuidAndTypeIntegrationTests : IntegrationTestBase
{
	public UuidAndTypeIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── UUID Type ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "UUID: Encoded as string, in lower-case hexa-decimal format, as described in RFC 9562, section 4."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CreateTable_WithUuidColumn_Succeeds()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE UuidTest1 (Id UUID NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");

		var rows = await QueryAsync(
			"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UuidTest1' AND COLUMN_NAME = 'Id'");
		rows.Should().ContainSingle();
		rows[0]["SPANNER_TYPE"].Should().Be("UUID");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "UUID: Encoded as string, in lower-case hexa-decimal format."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InsertAndQuery_UuidColumn_RoundTrips()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE UuidTest2 (Id UUID NOT NULL, Label STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		var uuid = Guid.NewGuid().ToString().ToLowerInvariant();
		await InsertAsync("UuidTest2", new Dictionary<string, object?> { ["Id"] = uuid, ["Label"] = "test" });

		var rows = await QueryAsync($"SELECT Id, Label FROM UuidTest2 WHERE Id = '{uuid}'");
		rows.Should().ContainSingle();
		rows[0]["Id"]!.ToString().Should().Be(uuid);
	}

	// ─── NEW_UUID Function ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
	//   "NEW_UUID() returns a UUID value."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NewUuid_ReturnsValidUuid()
	{
		var result = await QueryScalarAsync("SELECT NEW_UUID()");
		result.Should().NotBeNull();
		var str = result!.ToString()!;
		Guid.TryParse(str, out _).Should().BeTrue($"Expected valid UUID, got: '{str}'");
		str.Should().Be(str.ToLowerInvariant(), "UUID should be lower-case hex format");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
	//   "Each invocation of NEW_UUID() returns a unique value."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NewUuid_ReturnsDifferentValuesPerCall()
	{
		var rows = await QueryAsync("SELECT NEW_UUID() AS a, NEW_UUID() AS b");
		rows.Should().ContainSingle();
		var a = rows[0]["a"]!.ToString();
		var b = rows[0]["b"]!.ToString();
		a.Should().NotBe(b);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
	//   "NEW_UUID returns UUID type (not STRING like GENERATE_UUID)"
	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NewUuid_CanBeInsertedIntoUuidColumn()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE UuidTest3 (Id UUID NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO UuidTest3 (Id, Val) VALUES (NEW_UUID(), 42)");

		var rows = await QueryAsync("SELECT Id, Val FROM UuidTest3 WHERE Val = 42");
		rows.Should().ContainSingle();
		Guid.TryParse(rows[0]["Id"]!.ToString(), out _).Should().BeTrue();
	}

	// ─── PROTO / ENUM stubs — accept DDL but no runtime operations ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "PROTO: Encoded as a base64-encoded string, as described in RFC 4648."
	//   "ENUM: Encoded as string, in decimal format."
	// Neither Go emulator supports these either. We only need DDL acceptance.
}
