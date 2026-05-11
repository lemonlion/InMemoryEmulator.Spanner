using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended tests for CAST, SAFE_CAST, and implicit type coercion across all Spanner types.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastCoercionExtendedIntegrationTests : IntegrationTestBase
{
	public CastCoercionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ═══════════════════════════════════════════════════════════════
	// 1. CAST INT64 → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(1 AS STRING)", "1")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(100 AS STRING)", "100")]
	[InlineData("CAST(-100 AS STRING)", "-100")]
	[InlineData("CAST(999999999 AS STRING)", "999999999")]
	[InlineData("CAST(9223372036854775807 AS STRING)", "9223372036854775807")]
	[InlineData("CAST(-9223372036854775808 AS STRING)", "-9223372036854775808")]
	[InlineData("CAST(12345678901234 AS STRING)", "12345678901234")]
	[InlineData("CAST(42 AS STRING)", "42")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Int64ToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 2. CAST STRING → INT64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('1' AS INT64)", 1L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('-42' AS INT64)", -42L)]
	[InlineData("CAST('9223372036854775807' AS INT64)", 9223372036854775807L)]
	[InlineData("CAST('-9223372036854775808' AS INT64)", -9223372036854775808L)]
	[InlineData("CAST('00123' AS INT64)", 123L)]
	[InlineData("CAST('1000000' AS INT64)", 1000000L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToInt64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 3. CAST FLOAT64 → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0.0' AS FLOAT64) AS STRING)", "0")]
	[InlineData("CAST(CAST('1.0' AS FLOAT64) AS STRING)", "1")]
	[InlineData("CAST(CAST('-1.5' AS FLOAT64) AS STRING)", "-1.5")]
	[InlineData("CAST(CAST('3.14' AS FLOAT64) AS STRING)", "3.14")]
	[InlineData("CAST(CAST('0.001' AS FLOAT64) AS STRING)", "0.001")]
	[InlineData("CAST(CAST('-0.001' AS FLOAT64) AS STRING)", "-0.001")]
	[InlineData("CAST(CAST('inf' AS FLOAT64) AS STRING)", "inf")]
	[InlineData("CAST(CAST('-inf' AS FLOAT64) AS STRING)", "-inf")]
	[InlineData("CAST(CAST('nan' AS FLOAT64) AS STRING)", "nan")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Float64ToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 4. CAST STRING → FLOAT64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('0.0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('1.0' AS FLOAT64)", 1.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('100' AS FLOAT64)", 100.0)]
	[InlineData("CAST('-100' AS FLOAT64)", -100.0)]
	[InlineData("CAST('0.001' AS FLOAT64)", 0.001)]
	[InlineData("CAST('1e10' AS FLOAT64)", 1e10)]
	[InlineData("CAST('1.5e2' AS FLOAT64)", 150.0)]
	[InlineData("CAST('-2.5e3' AS FLOAT64)", -2500.0)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-9);
	}

	[Theory]
	[InlineData("CAST('inf' AS FLOAT64)")]
	[InlineData("CAST('-inf' AS FLOAT64)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToFloat64_Infinity(string expr)
	{
		var result = await Eval(expr);
		double.IsInfinity((double)result!).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToFloat64_NaN()
	{
		var result = await Eval("CAST('nan' AS FLOAT64)");
		double.IsNaN((double)result!).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// 5. CAST BOOL → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(TRUE AS STRING)", "true")]
	[InlineData("CAST(FALSE AS STRING)", "false")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_BoolToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 6. CAST STRING → BOOL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[InlineData("CAST('TRUE' AS BOOL)", true)]
	[InlineData("CAST('FALSE' AS BOOL)", false)]
	[InlineData("CAST('True' AS BOOL)", true)]
	[InlineData("CAST('False' AS BOOL)", false)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToBool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 7. CAST INT64 → FLOAT64 (implicit coercion)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_rules#coercion
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(1 AS FLOAT64)", 1.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[InlineData("CAST(1000000 AS FLOAT64)", 1000000.0)]
	[InlineData("CAST(-999 AS FLOAT64)", -999.0)]
	[InlineData("CAST(9223372036854775807 AS FLOAT64)", 9.223372036854776E+18)]
	[InlineData("CAST(-9223372036854775808 AS FLOAT64)", -9.223372036854776E+18)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Int64ToFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Math.Abs(expected * 1e-9) + 1e-9);
	}

	// ═══════════════════════════════════════════════════════════════
	// 8. CAST FLOAT64 → INT64 (truncation)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0.0' AS FLOAT64) AS INT64)", 0L)]
	[InlineData("CAST(CAST('1.0' AS FLOAT64) AS INT64)", 1L)]
	[InlineData("CAST(CAST('-1.0' AS FLOAT64) AS INT64)", -1L)]
	[InlineData("CAST(CAST('1.9' AS FLOAT64) AS INT64)", 2L)]
	[InlineData("CAST(CAST('-1.9' AS FLOAT64) AS INT64)", -2L)]
	[InlineData("CAST(CAST('1.5' AS FLOAT64) AS INT64)", 2L)]
	[InlineData("CAST(CAST('2.5' AS FLOAT64) AS INT64)", 3L)]
	[InlineData("CAST(CAST('3.5' AS FLOAT64) AS INT64)", 4L)]
	[InlineData("CAST(CAST('0.5' AS FLOAT64) AS INT64)", 1L)]
	[InlineData("CAST(CAST('99.99' AS FLOAT64) AS INT64)", 100L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Float64ToInt64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 9. CAST STRING → DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('2024-01-01' AS DATE)", "2024-01-01")]
	[InlineData("CAST('2000-12-31' AS DATE)", "2000-12-31")]
	[InlineData("CAST('1970-01-01' AS DATE)", "1970-01-01")]
	[InlineData("CAST('2024-02-29' AS DATE)", "2024-02-29")]
	[InlineData("CAST('2099-06-15' AS DATE)", "2099-06-15")]
	[InlineData("CAST('1900-01-01' AS DATE)", "1900-01-01")]
	[InlineData("CAST('2024-12-25' AS DATE)", "2024-12-25")]
	[InlineData("CAST('2024-07-04' AS DATE)", "2024-07-04")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToDate(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = DateTime.Parse(expectedStr);
		((DateTime)result!).Date.Should().Be(expected.Date);
	}

	// ═══════════════════════════════════════════════════════════════
	// 10. CAST DATE → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE '2024-01-01' AS STRING)", "2024-01-01")]
	[InlineData("CAST(DATE '2000-12-31' AS STRING)", "2000-12-31")]
	[InlineData("CAST(DATE '1970-01-01' AS STRING)", "1970-01-01")]
	[InlineData("CAST(DATE '2024-02-29' AS STRING)", "2024-02-29")]
	[InlineData("CAST(DATE '2099-06-15' AS STRING)", "2099-06-15")]
	[InlineData("CAST(DATE '1900-01-01' AS STRING)", "1900-01-01")]
	[InlineData("CAST(DATE '2024-07-04' AS STRING)", "2024-07-04")]
	[InlineData("CAST(DATE '2024-12-25' AS STRING)", "2024-12-25")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_DateToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 11. CAST STRING → TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('2024-01-01T00:00:00Z' AS TIMESTAMP)", "2024-01-01T00:00:00Z")]
	[InlineData("CAST('2024-06-15T12:30:45Z' AS TIMESTAMP)", "2024-06-15T12:30:45Z")]
	[InlineData("CAST('1970-01-01T00:00:00Z' AS TIMESTAMP)", "1970-01-01T00:00:00Z")]
	[InlineData("CAST('2024-02-29T23:59:59Z' AS TIMESTAMP)", "2024-02-29T23:59:59Z")]
	[InlineData("CAST('2000-01-01T00:00:00Z' AS TIMESTAMP)", "2000-01-01T00:00:00Z")]
	[InlineData("CAST('2024-12-31T23:59:59Z' AS TIMESTAMP)", "2024-12-31T23:59:59Z")]
	[InlineData("CAST('2024-07-04T18:00:00Z' AS TIMESTAMP)", "2024-07-04T18:00:00Z")]
	[InlineData("CAST('1999-12-31T23:59:59Z' AS TIMESTAMP)", "1999-12-31T23:59:59Z")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToTimestamp(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = DateTime.Parse(expectedStr).ToUniversalTime();
		((DateTime)result!).ToUniversalTime().Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 12. CAST TIMESTAMP → STRING (uses session timezone: America/Los_Angeles)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(TIMESTAMP '2024-06-15T12:30:45Z' AS STRING)", "2024-06-15 05:30:45-07")]
	[InlineData("CAST(TIMESTAMP '2024-02-29T23:59:59Z' AS STRING)", "2024-02-29 15:59:59-08")]
	[InlineData("CAST(TIMESTAMP '2024-12-31T23:59:59Z' AS STRING)", "2024-12-31 15:59:59-08")]
	[InlineData("CAST(TIMESTAMP '1999-12-31T23:59:59Z' AS STRING)", "1999-12-31 15:59:59-08")]
	[InlineData("CAST(TIMESTAMP '2024-07-04T18:00:00Z' AS STRING)", "2024-07-04 11:00:00-07")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cast_TimestampToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 13. CAST INT64 → NUMERIC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS NUMERIC)", "0")]
	[InlineData("CAST(1 AS NUMERIC)", "1")]
	[InlineData("CAST(-1 AS NUMERIC)", "-1")]
	[InlineData("CAST(42 AS NUMERIC)", "42")]
	[InlineData("CAST(-42 AS NUMERIC)", "-42")]
	[InlineData("CAST(9223372036854775807 AS NUMERIC)", "9223372036854775807")]
	[InlineData("CAST(-9223372036854775808 AS NUMERIC)", "-9223372036854775808")]
	[InlineData("CAST(1000000 AS NUMERIC)", "1000000")]
	[InlineData("CAST(999 AS NUMERIC)", "999")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Int64ToNumeric(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = SpannerNumeric.Parse(expectedStr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 14. CAST FLOAT64 → NUMERIC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0.0' AS FLOAT64) AS NUMERIC)", "0")]
	[InlineData("CAST(CAST('1.0' AS FLOAT64) AS NUMERIC)", "1")]
	[InlineData("CAST(CAST('-1.0' AS FLOAT64) AS NUMERIC)", "-1")]
	[InlineData("CAST(CAST('3.14' AS FLOAT64) AS NUMERIC)", "3.14")]
	[InlineData("CAST(CAST('-3.14' AS FLOAT64) AS NUMERIC)", "-3.14")]
	[InlineData("CAST(CAST('0.5' AS FLOAT64) AS NUMERIC)", "0.5")]
	[InlineData("CAST(CAST('100.001' AS FLOAT64) AS NUMERIC)", "100.001")]
	[InlineData("CAST(CAST('99999.99' AS FLOAT64) AS NUMERIC)", "99999.99")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Float64ToNumeric(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = SpannerNumeric.Parse(expectedStr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 15. CAST NUMERIC → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0' AS NUMERIC) AS STRING)", "0")]
	[InlineData("CAST(CAST('1' AS NUMERIC) AS STRING)", "1")]
	[InlineData("CAST(CAST('-1' AS NUMERIC) AS STRING)", "-1")]
	[InlineData("CAST(CAST('3.14' AS NUMERIC) AS STRING)", "3.14")]
	[InlineData("CAST(CAST('-3.14' AS NUMERIC) AS STRING)", "-3.14")]
	[InlineData("CAST(CAST('0.000000001' AS NUMERIC) AS STRING)", "0.000000001")]
	[InlineData("CAST(CAST('42.5' AS NUMERIC) AS STRING)", "42.5")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_NumericToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 16. CAST STRING → NUMERIC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('0' AS NUMERIC)", "0")]
	[InlineData("CAST('1' AS NUMERIC)", "1")]
	[InlineData("CAST('-1' AS NUMERIC)", "-1")]
	[InlineData("CAST('3.14' AS NUMERIC)", "3.14")]
	[InlineData("CAST('-3.14' AS NUMERIC)", "-3.14")]
	[InlineData("CAST('0.000000001' AS NUMERIC)", "0.000000001")]
	[InlineData("CAST('42' AS NUMERIC)", "42")]
	[InlineData("CAST('1000000.5' AS NUMERIC)", "1000000.5")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToNumeric(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = SpannerNumeric.Parse(expectedStr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 17. CAST NUMERIC → INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0' AS NUMERIC) AS INT64)", 0L)]
	[InlineData("CAST(CAST('1' AS NUMERIC) AS INT64)", 1L)]
	[InlineData("CAST(CAST('-1' AS NUMERIC) AS INT64)", -1L)]
	[InlineData("CAST(CAST('42' AS NUMERIC) AS INT64)", 42L)]
	[InlineData("CAST(CAST('-42' AS NUMERIC) AS INT64)", -42L)]
	[InlineData("CAST(CAST('100' AS NUMERIC) AS INT64)", 100L)]
	[InlineData("CAST(CAST('1.5' AS NUMERIC) AS INT64)", 2L)]
	[InlineData("CAST(CAST('2.5' AS NUMERIC) AS INT64)", 3L)]
	[InlineData("CAST(CAST('3.5' AS NUMERIC) AS INT64)", 4L)]
	[InlineData("CAST(CAST('0.5' AS NUMERIC) AS INT64)", 1L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cast_NumericToInt64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 18. CAST NUMERIC → FLOAT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST('0' AS NUMERIC) AS FLOAT64)", 0.0)]
	[InlineData("CAST(CAST('1' AS NUMERIC) AS FLOAT64)", 1.0)]
	[InlineData("CAST(CAST('-1' AS NUMERIC) AS FLOAT64)", -1.0)]
	[InlineData("CAST(CAST('3.14' AS NUMERIC) AS FLOAT64)", 3.14)]
	[InlineData("CAST(CAST('-3.14' AS NUMERIC) AS FLOAT64)", -3.14)]
	[InlineData("CAST(CAST('42.5' AS NUMERIC) AS FLOAT64)", 42.5)]
	[InlineData("CAST(CAST('0.000000001' AS NUMERIC) AS FLOAT64)", 1e-9)]
	[InlineData("CAST(CAST('1000000.5' AS NUMERIC) AS FLOAT64)", 1000000.5)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_NumericToFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Math.Abs(expected * 1e-9) + 1e-12);
	}

	// ═══════════════════════════════════════════════════════════════
	// 19. SAFE_CAST — returns NULL on invalid conversions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#safe_casting
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	[InlineData("SAFE_CAST('abc' AS NUMERIC)")]
	[InlineData("SAFE_CAST('not-a-date' AS DATE)")]
	[InlineData("SAFE_CAST('not-a-timestamp' AS TIMESTAMP)")]
	[InlineData("SAFE_CAST('' AS INT64)")]
	[InlineData("SAFE_CAST('12.34.56' AS FLOAT64)")]
	[InlineData("SAFE_CAST('2024-13-01' AS DATE)")]
	[InlineData("SAFE_CAST('2024-02-30' AS DATE)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_InvalidConversion_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_CAST('42' AS INT64)", 42L)]
	[InlineData("SAFE_CAST('0' AS INT64)", 0L)]
	[InlineData("SAFE_CAST('-1' AS INT64)", -1L)]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[InlineData("SAFE_CAST('false' AS BOOL)", false)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_ValidConversion_ReturnsValue(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("SAFE_CAST('0.0' AS FLOAT64)", 0.0)]
	[InlineData("SAFE_CAST('-1.5' AS FLOAT64)", -1.5)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_ValidFloat64_ReturnsValue(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-9);
	}

	[Theory]
	[InlineData("SAFE_CAST('2024-01-01' AS DATE)", "2024-01-01")]
	[InlineData("SAFE_CAST('2000-06-15' AS DATE)", "2000-06-15")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_ValidDate_ReturnsValue(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		((DateTime)result!).Date.Should().Be(DateTime.Parse(expectedStr).Date);
	}

	// ═══════════════════════════════════════════════════════════════
	// 20. Arithmetic coercion — INT64 + FLOAT64 = FLOAT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_rules#coercion
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 1.0", 2.0)]
	[InlineData("10 - 0.5", 9.5)]
	[InlineData("3 * 2.0", 6.0)]
	[InlineData("7 / 2.0", 3.5)]
	[InlineData("0 + 0.0", 0.0)]
	[InlineData("-1 + 0.5", -0.5)]
	[InlineData("100 * 0.01", 1.0)]
	[InlineData("1 + 1e0", 2.0)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task ArithmeticCoercion_Int64PlusFloat64_ReturnsFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-9);
	}

	// ═══════════════════════════════════════════════════════════════
	// 21. NULL CAST — CAST(NULL AS X)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[InlineData("CAST(NULL AS NUMERIC)")]
	[InlineData("CAST(NULL AS BYTES)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_NullToType_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 22. CAST chains — CAST(CAST(x AS Y) AS Z)
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(-1 AS STRING) AS INT64)", -1L)]
	[InlineData("CAST(CAST(0 AS STRING) AS INT64)", 0L)]
	[InlineData("CAST(CAST(1 AS FLOAT64) AS INT64)", 1L)]
	[InlineData("CAST(CAST(TRUE AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(FALSE AS STRING) AS BOOL)", false)]
	[InlineData("CAST(CAST(42 AS NUMERIC) AS INT64)", 42L)]
	[InlineData("CAST(CAST(-42 AS NUMERIC) AS INT64)", -42L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Chain_Int64OrBool(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(CAST(3.14 AS STRING) AS FLOAT64)", 3.14)]
	[InlineData("CAST(CAST(0.0 AS STRING) AS FLOAT64)", 0.0)]
	[InlineData("CAST(CAST(-1.5 AS STRING) AS FLOAT64)", -1.5)]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS STRING)", "42")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Chain_Float64(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-9);
		else
			result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(CAST(DATE '2024-01-01' AS STRING) AS DATE)", "2024-01-01")]
	[InlineData("CAST(CAST(DATE '2000-12-31' AS STRING) AS DATE)", "2000-12-31")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Chain_Date(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		((DateTime)result!).Date.Should().Be(DateTime.Parse(expectedStr).Date);
	}

	[Theory]
	[InlineData("CAST(CAST(TIMESTAMP '2024-01-01T00:00:00Z' AS STRING) AS TIMESTAMP)", "2024-01-01T00:00:00Z")]
	[InlineData("CAST(CAST(TIMESTAMP '2024-06-15T12:30:45Z' AS STRING) AS TIMESTAMP)", "2024-06-15T12:30:45Z")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Chain_Timestamp(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = DateTime.Parse(expectedStr).ToUniversalTime();
		((DateTime)result!).ToUniversalTime().Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(CAST(CAST('3.14' AS NUMERIC) AS STRING) AS NUMERIC)", "3.14")]
	[InlineData("CAST(CAST(CAST('0' AS NUMERIC) AS STRING) AS NUMERIC)", "0")]
	[InlineData("CAST(CAST(CAST('-42.5' AS NUMERIC) AS STRING) AS NUMERIC)", "-42.5")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Chain_Numeric(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = SpannerNumeric.Parse(expectedStr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 23. CAST BOOL → INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(TRUE AS INT64)", 1L)]
	[InlineData("CAST(FALSE AS INT64)", 0L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_BoolToInt64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 24. CAST INT64 → BOOL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS BOOL)", false)]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(-1 AS BOOL)", true)]
	[InlineData("CAST(42 AS BOOL)", true)]
	[InlineData("CAST(-100 AS BOOL)", true)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_Int64ToBool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 25. CAST STRING → BYTES and BYTES → STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(b'' AS STRING)", "")]
	[InlineData("CAST(b'hello' AS STRING)", "hello")]
	[InlineData("CAST(b'abc' AS STRING)", "abc")]
	[InlineData("CAST(b'test123' AS STRING)", "test123")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_BytesToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('' AS BYTES)", "")]
	[InlineData("CAST('hello' AS BYTES)", "hello")]
	[InlineData("CAST('abc' AS BYTES)", "abc")]
	[InlineData("CAST('test123' AS BYTES)", "test123")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringToBytes(string expr, string expectedText)
	{
		var result = await Eval(expr);
		var bytes = (byte[])result!;
		System.Text.Encoding.UTF8.GetString(bytes).Should().Be(expectedText);
	}

	// ═══════════════════════════════════════════════════════════════
	// 26. CAST DATE → TIMESTAMP (midnight in session timezone: America/Los_Angeles → UTC)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE '2024-01-01' AS TIMESTAMP)", "2024-01-01T08:00:00Z")]
	[InlineData("CAST(DATE '1970-01-01' AS TIMESTAMP)", "1970-01-01T08:00:00Z")]
	[InlineData("CAST(DATE '2024-12-31' AS TIMESTAMP)", "2024-12-31T08:00:00Z")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cast_DateToTimestamp(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = DateTime.Parse(expectedStr).ToUniversalTime();
		((DateTime)result!).ToUniversalTime().Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 27. CAST TIMESTAMP → DATE (extract date in session timezone: America/Los_Angeles)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(TIMESTAMP '2024-01-01T12:30:00Z' AS DATE)", "2024-01-01")]
	[InlineData("CAST(TIMESTAMP '2024-06-15T23:59:59Z' AS DATE)", "2024-06-15")]
	[InlineData("CAST(TIMESTAMP '1970-01-01T00:00:00Z' AS DATE)", "1969-12-31")]
	[InlineData("CAST(TIMESTAMP '2024-12-31T18:00:00Z' AS DATE)", "2024-12-31")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cast_TimestampToDate(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		((DateTime)result!).Date.Should().Be(DateTime.Parse(expectedStr).Date);
	}

	// ═══════════════════════════════════════════════════════════════
	// 28. Implicit coercion in comparisons — INT64 vs FLOAT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_rules#coercion
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 = 1.0", true)]
	[InlineData("0 = 0.0", true)]
	[InlineData("1 < 1.5", true)]
	[InlineData("2 > 1.5", true)]
	[InlineData("1 != 1.5", true)]
	[InlineData("1 = 1.5", false)]
	[InlineData("-1 < 0.0", true)]
	[InlineData("10 >= 10.0", true)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task ImplicitCoercion_Int64VsFloat64_Comparison(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 29. SAFE_CAST with valid numeric strings
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('0' AS NUMERIC)", "0")]
	[InlineData("SAFE_CAST('3.14' AS NUMERIC)", "3.14")]
	[InlineData("SAFE_CAST('-42' AS NUMERIC)", "-42")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_ValidNumeric_ReturnsValue(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = SpannerNumeric.Parse(expectedStr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_CAST('abc' AS NUMERIC)")]
	[InlineData("SAFE_CAST('' AS NUMERIC)")]
	[InlineData("SAFE_CAST('12.34.56' AS NUMERIC)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_InvalidNumeric_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 30. SAFE_CAST with timestamp
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('2024-01-01T00:00:00Z' AS TIMESTAMP)", "2024-01-01T00:00:00Z")]
	[InlineData("SAFE_CAST('2024-06-15T12:30:45Z' AS TIMESTAMP)", "2024-06-15T12:30:45Z")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_ValidTimestamp_ReturnsValue(string expr, string expectedStr)
	{
		var result = await Eval(expr);
		var expected = DateTime.Parse(expectedStr).ToUniversalTime();
		((DateTime)result!).ToUniversalTime().Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_CAST('not-a-time' AS TIMESTAMP)")]
	[InlineData("SAFE_CAST('2024-13-01T00:00:00Z' AS TIMESTAMP)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_InvalidTimestamp_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 31. Mixed-type expressions with implicit coercion
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_rules#coercion
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COALESCE(CAST(NULL AS FLOAT64), 3.14)", 3.14)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task ImplicitCoercion_MixedExpressions(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-9);
	}

	// ═══════════════════════════════════════════════════════════════
	// 32. CAST with leading/trailing whitespace in strings
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(' 42' AS INT64)", 42L)]
	[InlineData("CAST('42 ' AS INT64)", 42L)]
	[InlineData("CAST(' 42 ' AS INT64)", 42L)]
	[InlineData("CAST(' -1 ' AS INT64)", -1L)]
	[InlineData("CAST(' 3.14 ' AS FLOAT64)", 3.14)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_StringWithWhitespace_TrimsAndConverts(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-9);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 33. CAST FLOAT64 literal forms
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(1e0 AS INT64)", 1L)]
	[InlineData("CAST(1e1 AS INT64)", 10L)]
	[InlineData("CAST(1e2 AS INT64)", 100L)]
	[InlineData("CAST(1.5e1 AS INT64)", 15L)]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_ScientificNotationToInt64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(1e0 AS STRING)", "1")]
	[InlineData("CAST(1.5e1 AS STRING)", "15")]
	[InlineData("CAST(1.23e2 AS STRING)", "123")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task Cast_ScientificNotationToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 34. SAFE_CAST returning NULL for CAST that would overflow
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('99999999999999999999999' AS INT64)")]
	[InlineData("SAFE_CAST('-99999999999999999999999' AS INT64)")]
	[Trait(TestTraits.Category, "CastCoercionExtended")]
	public async Task SafeCast_Overflow_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}
}
