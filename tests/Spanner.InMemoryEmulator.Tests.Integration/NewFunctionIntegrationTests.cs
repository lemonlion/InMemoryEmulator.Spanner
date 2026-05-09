using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for newly added SQL functions: hash, array, date, string, math, and conditional.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NewFunctionIntegrationTests : IntegrationTestBase
{
	public NewFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE FuncTest (Id INT64 NOT NULL, Name STRING(100), Val INT64, Data BYTES(MAX)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// Hash Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SHA256_ReturnsBytes()
	{
		var rows = await QueryAsync("SELECT SHA256(b'abc') AS h");
		rows.Should().ContainSingle();
		rows[0]["h"].Should().BeOfType<byte[]>();
		((byte[])rows[0]["h"]!).Should().HaveCount(32);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SHA512_ReturnsBytes()
	{
		var rows = await QueryAsync("SELECT SHA512(b'test') AS h");
		rows.Should().ContainSingle();
		((byte[])rows[0]["h"]!).Should().HaveCount(64);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SHA1_ReturnsBytes()
	{
		var rows = await QueryAsync("SELECT SHA1(b'hello') AS h");
		rows.Should().ContainSingle();
		((byte[])rows[0]["h"]!).Should().HaveCount(20);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MD5_ReturnsBytes()
	{
		var rows = await QueryAsync("SELECT MD5(b'hello') AS h");
		rows.Should().ContainSingle();
		((byte[])rows[0]["h"]!).Should().HaveCount(16);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SHA256_NullReturnsNull()
	{
		var rows = await QueryAsync("SELECT SHA256(NULL) AS h");
		rows.Should().ContainSingle();
		rows[0]["h"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FARM_FINGERPRINT_ReturnsInt64()
	{
		var rows = await QueryAsync("SELECT FARM_FINGERPRINT('hello') AS fp");
		rows.Should().ContainSingle();
		rows[0]["fp"].Should().BeOfType<long>();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FARM_FINGERPRINT_DeterministicForSameInput()
	{
		var rows1 = await QueryAsync("SELECT FARM_FINGERPRINT('test') AS fp");
		var rows2 = await QueryAsync("SELECT FARM_FINGERPRINT('test') AS fp");
		rows1[0]["fp"].Should().Be(rows2[0]["fp"]);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FARM_FINGERPRINT_DifferentForDifferentInput()
	{
		var rows = await QueryAsync("SELECT FARM_FINGERPRINT('a') AS fp1, FARM_FINGERPRINT('b') AS fp2");
		rows[0]["fp1"].Should().NotBe(rows[0]["fp2"]);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional Array Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ARRAY_INCLUDES_ReturnsTrue()
	{
		var rows = await QueryAsync("SELECT ARRAY_INCLUDES([1, 2, 3], 2) AS result");
		rows.Should().ContainSingle();
		rows[0]["result"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ARRAY_INCLUDES_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT ARRAY_INCLUDES([1, 2, 3], 5) AS result");
		rows.Should().ContainSingle();
		rows[0]["result"].Should().Be(false);
	}

	// ═══════════════════════════════════════════════════════════════
	// Date Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task UNIX_DATE_ReturnsEpochDays()
	{
		var rows = await QueryAsync("SELECT UNIX_DATE(DATE '1970-01-01') AS d");
		rows.Should().ContainSingle();
		rows[0]["d"].Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task UNIX_DATE_ReturnsNonZero()
	{
		var rows = await QueryAsync("SELECT UNIX_DATE(DATE '2000-01-01') AS d");
		rows.Should().ContainSingle();
		((long)rows[0]["d"]!).Should().BeGreaterThan(0);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DATE_FROM_UNIX_DATE_RoundTrips()
	{
		var rows = await QueryAsync("SELECT DATE_FROM_UNIX_DATE(0) AS d");
		rows.Should().ContainSingle();
		// Should be 1970-01-01
		var date = (DateTime)rows[0]["d"]!;
		date.Year.Should().Be(1970);
		date.Month.Should().Be(1);
		date.Day.Should().Be(1);
	}

	// ═══════════════════════════════════════════════════════════════
	// String Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NORMALIZE_DefaultNFC()
	{
		var rows = await QueryAsync("SELECT NORMALIZE('café') AS n");
		rows.Should().ContainSingle();
		rows[0]["n"].Should().Be("café");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NORMALIZE_AND_CASEFOLD_LowerCases()
	{
		var rows = await QueryAsync("SELECT NORMALIZE_AND_CASEFOLD('HELLO') AS n");
		rows.Should().ContainSingle();
		rows[0]["n"].Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task OCTET_LENGTH_ReturnsUtf8Bytes()
	{
		var rows = await QueryAsync("SELECT OCTET_LENGTH('abc') AS len");
		rows.Should().ContainSingle();
		rows[0]["len"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task OCTET_LENGTH_MultiByteChar()
	{
		// é is 2 bytes in UTF-8
		var rows = await QueryAsync("SELECT OCTET_LENGTH('é') AS len");
		rows.Should().ContainSingle();
		((long)rows[0]["len"]!).Should().BeGreaterThan(1);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task REGEXP_EXTRACT_ALL_ReturnsAllMatches()
	{
		var rows = await QueryAsync("SELECT REGEXP_EXTRACT_ALL('abc123def456', '[0-9]+') AS matches");
		rows.Should().ContainSingle();
		var matches = rows[0]["matches"] as System.Collections.IList;
		matches.Should().NotBeNull();
		matches!.Count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task REGEXP_INSTR_ReturnsPosition()
	{
		var rows = await QueryAsync("SELECT REGEXP_INSTR('abcdef', 'cd') AS pos");
		rows.Should().ContainSingle();
		rows[0]["pos"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task REGEXP_INSTR_NoMatch_ReturnsZero()
	{
		var rows = await QueryAsync("SELECT REGEXP_INSTR('abcdef', 'xyz') AS pos");
		rows.Should().ContainSingle();
		rows[0]["pos"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Bit Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task BIT_COUNT_Int64()
	{
		var rows = await QueryAsync("SELECT BIT_COUNT(7) AS bc");
		rows.Should().ContainSingle();
		rows[0]["bc"].Should().Be(3L); // 7 = 0b111
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task BIT_COUNT_Zero()
	{
		var rows = await QueryAsync("SELECT BIT_COUNT(0) AS bc");
		rows.Should().ContainSingle();
		rows[0]["bc"].Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task BIT_COUNT_NullReturnsNull()
	{
		var rows = await QueryAsync("SELECT BIT_COUNT(NULL) AS bc");
		rows.Should().ContainSingle();
		rows[0]["bc"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ERROR function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/debugging_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ERROR_ThrowsWithMessage()
	{
		var act = () => QueryAsync("SELECT ERROR('test error message') AS e");
		await act.Should().ThrowAsync<SpannerException>();
	}
}
