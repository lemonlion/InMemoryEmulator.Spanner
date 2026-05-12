using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive integration tests for CAST and SAFE_CAST across all type combinations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ComprehensiveCastIntegrationTests : IntegrationTestBase
{
	public ComprehensiveCastIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// INT64 → various types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(42 AS STRING)", "42")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(9223372036854775807 AS STRING)", "9223372036854775807")]
	public async Task CastInt64ToString(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	public async Task CastInt64ToFloat64(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(0 AS BOOL)", false)]
	public async Task CastInt64ToBool(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// FLOAT64 → various types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(3.14 AS INT64)", 3L)]
	[InlineData("CAST(3.5 AS INT64)", 4L)]
	[InlineData("CAST(2.5 AS INT64)", 3L)]  // Rounds halfway cases away from zero
	[InlineData("CAST(-3.14 AS INT64)", -3L)]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	public async Task CastFloat64ToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(3.14 AS STRING)", "3.14")]
	[InlineData("CAST(0.0 AS STRING)", "0")]
	[InlineData("CAST(-1.5 AS STRING)", "-1.5")]
	public async Task CastFloat64ToString(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task CastInfToString()
	{
		var result = (string)(await Eval("CAST(CAST('inf' AS FLOAT64) AS STRING)"))!;
		result.Should().Be("inf");
	}

	[Fact]
	public async Task CastNegInfToString()
	{
		var result = (string)(await Eval("CAST(CAST('-inf' AS FLOAT64) AS STRING)"))!;
		result.Should().Be("-inf");
	}

	[Fact]
	public async Task CastNanToString()
	{
		var result = (string)(await Eval("CAST(CAST('nan' AS FLOAT64) AS STRING)"))!;
		result.Should().BeOneOf("nan", "NaN");
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING → various types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('9223372036854775807' AS INT64)", 9223372036854775807L)]
	public async Task CastStringToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST('inf' AS FLOAT64)", double.PositiveInfinity)]
	[InlineData("CAST('-inf' AS FLOAT64)", double.NegativeInfinity)]
	public async Task CastStringToFloat64(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		if (double.IsInfinity(expected))
			result.Should().Be(expected);
		else
			result.Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	public async Task CastStringNanToFloat64()
	{
		var result = (double)(await Eval("CAST('nan' AS FLOAT64)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[InlineData("CAST('TRUE' AS BOOL)", true)]
	[InlineData("CAST('FALSE' AS BOOL)", false)]
	public async Task CastStringToBool(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST('2024-01-15' AS DATE)", "2024-01-15")]
	[InlineData("CAST('2000-01-01' AS DATE)", "2000-01-01")]
	[InlineData("CAST('1999-12-31' AS DATE)", "1999-12-31")]
	public async Task CastStringToDate(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().NotBeNull();
		// Date is typically returned as DateTime
		var dt = (DateTime)result!;
		dt.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	[Fact]
	public async Task CastStringToTimestamp()
	{
		var result = await Eval("CAST('2024-01-15T10:30:00Z' AS TIMESTAMP)");
		result.Should().NotBeNull();
		var dt = (DateTime)result!;
		dt.Year.Should().Be(2024);
		dt.Month.Should().Be(1);
		dt.Day.Should().Be(15);
	}

	// ═══════════════════════════════════════════════════════════════
	// BOOL → various types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(true AS STRING)", "true")]
	[InlineData("CAST(false AS STRING)", "false")]
	public async Task CastBoolToString(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(true AS INT64)", 1L)]
	[InlineData("CAST(false AS INT64)", 0L)]
	public async Task CastBoolToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// DATE → STRING
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CastDateToString()
	{
		var result = (string)(await Eval("CAST(DATE '2024-01-15' AS STRING)"))!;
		result.Should().Be("2024-01-15");
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP → various
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CastTimestampToString()
	{
		var result = (string)(await Eval("CAST(TIMESTAMP '2024-01-15T10:30:00Z' AS STRING)"))!;
		result.Should().Contain("2024-01-15");
	}

	[Fact]
	public async Task CastTimestampToDate()
	{
		var result = await Eval("CAST(TIMESTAMP '2024-06-15T10:30:00Z' AS DATE)");
		result.Should().NotBeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_CAST — returns NULL on invalid conversions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('42' AS INT64)", 42L)]
	[InlineData("SAFE_CAST('abc' AS INT64)")] // NULL
	[InlineData("SAFE_CAST('' AS INT64)")] // NULL
	[InlineData("SAFE_CAST('3.14' AS INT64)")] // NULL  
	public async Task SafeCast_StringToInt64(string expr, object? expected = null) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[InlineData("SAFE_CAST('false' AS BOOL)", false)]
	[InlineData("SAFE_CAST('maybe' AS BOOL)")] // NULL
	[InlineData("SAFE_CAST('' AS BOOL)")] // NULL
	public async Task SafeCast_StringToBool(string expr, object? expected = null) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeCast_InvalidDate_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('not-a-date' AS DATE)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeCast_InvalidTimestamp_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('not-a-timestamp' AS TIMESTAMP)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeCast_ValidDate()
	{
		var result = await Eval("SAFE_CAST('2024-06-15' AS DATE)");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task SafeCast_Null_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST(CAST(NULL AS STRING) AS INT64)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST NULL preserves NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[InlineData("CAST(NULL AS BYTES)")]
	public async Task Cast_Null_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Chained casts
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(3.14 AS STRING) AS FLOAT64)", 3.14)]
	[InlineData("CAST(CAST(true AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(CAST(42 AS FLOAT64) AS STRING) AS INT64)", 42L)]
	public async Task ChainedCasts(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// BYTES ↔ STRING
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CastBytesToString()
	{
		var result = (string)(await Eval("CAST(b'hello' AS STRING)"))!;
		result.Should().Be("hello");
	}

	[Fact]
	public async Task CastStringToBytes_AndBack()
	{
		var result = (string)(await Eval("CAST(CAST('hello' AS BYTES) AS STRING)"))!;
		result.Should().Be("hello");
	}

	// ═══════════════════════════════════════════════════════════════
	// COERCE_* support (implicit coercion in expressions)
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 1.0", 2.0)]
	[InlineData("1 * 2.0", 2.0)]
	[InlineData("10 / 3.0", 3.3333333333333335)]
	[InlineData("1 - 0.5", 0.5)]
	public async Task ImplicitCoercion_IntToFloat(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	// ═══════════════════════════════════════════════════════════════
	// GENERATE_UUID / NEW_UUID returns valid format
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GenerateUuid_Returns36CharString()
	{
		var result = (string)(await Eval("GENERATE_UUID()"))!;
		result.Should().MatchRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
	}

	[Fact]
	public async Task GenerateUuid_Unique()
	{
		var r1 = (string)(await Eval("GENERATE_UUID()"))!;
		var r2 = (string)(await Eval("GENERATE_UUID()"))!;
		r1.Should().NotBe(r2);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NewUuid_Returns36CharString()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
		//   "NEW_UUID() returns a UUID value." — SDK deserializes UUID as System.Guid.
		var result = await Eval("NEW_UUID()");
		var uuidString = result is Guid g ? g.ToString() : (string)result!;
		uuidString.Should().MatchRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
	}

}
