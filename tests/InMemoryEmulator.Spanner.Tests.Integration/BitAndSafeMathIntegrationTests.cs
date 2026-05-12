using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for BIT_REVERSE, SAFE_ADD/SUB/MUL/NEGATE overflow detection,
/// and other math edge cases not covered by existing tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BitAndSafeMathIntegrationTests : IntegrationTestBase
{
	public BitAndSafeMathIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT_REVERSE — reverses bit pattern of an INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#bit_reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: When is_signed=true, only non-sign bits (0-62) are reversed; sign bit is preserved.
	// Verified against real Cloud Spanner.
	[InlineData("BIT_REVERSE(0, true)", 0L)]
	[InlineData("BIT_REVERSE(1, true)", 4611686018427387904L)]       // 0x4000000000000000
	[InlineData("BIT_REVERSE(-1, true)", -1L)]
	[InlineData("BIT_REVERSE(2, true)", 2305843009213693952L)]       // 0x2000000000000000
	[InlineData("BIT_REVERSE(4, true)", 1152921504606846976L)]       // 0x1000000000000000
	[InlineData("BIT_REVERSE(8, true)", 576460752303423488L)]        // 0x0800000000000000
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task BitReverse_Signed(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task BitReverse_Null_IsNull()
	{
		var result = await Eval("BIT_REVERSE(CAST(NULL AS INT64), true)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task BitReverse_RoundTrip()
	{
		// BIT_REVERSE(BIT_REVERSE(x)) = x
		var result = (long)(await Eval("BIT_REVERSE(BIT_REVERSE(42, true), true)"))!;
		result.Should().Be(42);
	}

	[Fact]
	public async Task BitReverse_RoundTrip_Negative()
	{
		var result = (long)(await Eval("BIT_REVERSE(BIT_REVERSE(-42, true), true)"))!;
		result.Should().Be(-42);
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT_COUNT — extended edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#bit_count
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BIT_COUNT(0)", 0L)]
	[InlineData("BIT_COUNT(1)", 1L)]
	[InlineData("BIT_COUNT(2)", 1L)]
	[InlineData("BIT_COUNT(3)", 2L)]
	[InlineData("BIT_COUNT(7)", 3L)]
	[InlineData("BIT_COUNT(15)", 4L)]
	[InlineData("BIT_COUNT(255)", 8L)]
	[InlineData("BIT_COUNT(1023)", 10L)]
	[InlineData("BIT_COUNT(-1)", 64L)]
	[InlineData("BIT_COUNT(-2)", 63L)]
	public async Task BitCount_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task BitCount_MaxInt64()
	{
		var result = (long)(await Eval("BIT_COUNT(9223372036854775807)"))!;
		result.Should().Be(63);
	}

	[Fact]
	public async Task BitCount_MinInt64()
	{
		// MIN_INT64 = -9223372036854775808 = one 1-bit (sign bit only in two's complement)
		var result = (long)(await Eval("BIT_COUNT(-9223372036854775808)"))!;
		result.Should().Be(1);
	}

	[Fact]
	public async Task BitCount_Null_IsNull()
	{
		var result = await Eval("BIT_COUNT(CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_ADD — returns NULL on overflow instead of error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_ADD(1, 2)", 3L)]
	[InlineData("SAFE_ADD(0, 0)", 0L)]
	[InlineData("SAFE_ADD(-1, 1)", 0L)]
	[InlineData("SAFE_ADD(100, -50)", 50L)]
	[InlineData("SAFE_ADD(-100, -200)", -300L)]
	public async Task SafeAdd_NormalValues(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeAdd_Overflow_ReturnsNull()
	{
		// INT64 max = 9223372036854775807; adding 1 overflows
		var result = await Eval("SAFE_ADD(9223372036854775807, 1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeAdd_NegativeOverflow_ReturnsNull()
	{
		var result = await Eval("SAFE_ADD(-9223372036854775808, -1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeAdd_Null_IsNull()
	{
		var result = await Eval("SAFE_ADD(CAST(NULL AS INT64), 1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeAdd_NullSecond_IsNull()
	{
		var result = await Eval("SAFE_ADD(1, CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_SUBTRACT — returns NULL on overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_subtract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_SUBTRACT(5, 3)", 2L)]
	[InlineData("SAFE_SUBTRACT(0, 0)", 0L)]
	[InlineData("SAFE_SUBTRACT(-1, -1)", 0L)]
	[InlineData("SAFE_SUBTRACT(100, 200)", -100L)]
	public async Task SafeSubtract_NormalValues(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeSubtract_Overflow_ReturnsNull()
	{
		// -9223372036854775808 - 1 overflows
		var result = await Eval("SAFE_SUBTRACT(-9223372036854775808, 1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeSubtract_PositiveOverflow_ReturnsNull()
	{
		var result = await Eval("SAFE_SUBTRACT(9223372036854775807, -1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeSubtract_Null_IsNull()
	{
		var result = await Eval("SAFE_SUBTRACT(CAST(NULL AS INT64), 1)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_MULTIPLY — returns NULL on overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_multiply
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_MULTIPLY(3, 4)", 12L)]
	[InlineData("SAFE_MULTIPLY(0, 999)", 0L)]
	[InlineData("SAFE_MULTIPLY(-2, 3)", -6L)]
	[InlineData("SAFE_MULTIPLY(-2, -3)", 6L)]
	[InlineData("SAFE_MULTIPLY(1, 1)", 1L)]
	public async Task SafeMultiply_NormalValues(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeMultiply_Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_MULTIPLY(9223372036854775807, 2)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeMultiply_NegativeOverflow_ReturnsNull()
	{
		var result = await Eval("SAFE_MULTIPLY(-9223372036854775808, 2)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeMultiply_Null_IsNull()
	{
		var result = await Eval("SAFE_MULTIPLY(CAST(NULL AS INT64), 2)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_NEGATE — returns NULL on overflow (MIN_INT64)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_negate
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	[InlineData("SAFE_NEGATE(42)", -42L)]
	[InlineData("SAFE_NEGATE(-42)", 42L)]
	public async Task SafeNegate_NormalValues(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeNegate_MinInt64_ReturnsNull()
	{
		// -(-9223372036854775808) overflows INT64
		var result = await Eval("SAFE_NEGATE(-9223372036854775808)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeNegate_MaxInt64_ReturnsNegMax()
	{
		var result = (long)(await Eval("SAFE_NEGATE(9223372036854775807)"))!;
		result.Should().Be(-9223372036854775807L);
	}

	[Fact]
	public async Task SafeNegate_Null_IsNull()
	{
		var result = await Eval("SAFE_NEGATE(CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_DIVIDE — returns NULL on division by zero
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		var result = await Eval("SAFE_DIVIDE(10, 0)");
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_DIVIDE(10.0, 2.0)", 5.0)]
	[InlineData("SAFE_DIVIDE(0.0, 1.0)", 0.0)]
	[InlineData("SAFE_DIVIDE(-10.0, 2.0)", -5.0)]
	public async Task SafeDivide_NormalValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task SafeDivide_Null_IsNull()
	{
		var result = await Eval("SAFE_DIVIDE(CAST(NULL AS FLOAT64), 1.0)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// MOD — edge cases with negative numbers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("MOD(7, 3)", 1L)]
	[InlineData("MOD(-7, 3)", -1L)]
	[InlineData("MOD(7, -3)", 1L)]
	[InlineData("MOD(-7, -3)", -1L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(5, 5)", 0L)]
	[InlineData("MOD(1, 1)", 0L)]
	[InlineData("MOD(100, 7)", 2L)]
	[InlineData("MOD(-100, 7)", -2L)]
	public async Task Mod_EdgeCases(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Mod_Null_IsNull()
	{
		var result = await Eval("MOD(CAST(NULL AS INT64), 3)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// DIV — integer division with negative numbers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DIV(7, 3)", 2L)]
	[InlineData("DIV(-7, 3)", -2L)]
	[InlineData("DIV(7, -3)", -2L)]
	[InlineData("DIV(-7, -3)", 2L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(5, 5)", 1L)]
	[InlineData("DIV(100, 7)", 14L)]
	[InlineData("DIV(1, 2)", 0L)]
	public async Task Div_EdgeCases(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// IEEE_DIVIDE — special float division semantics
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 2.0)", 5.0)]
	[InlineData("IEEE_DIVIDE(0.0, 1.0)", 0.0)]
	[InlineData("IEEE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	public async Task IeeeDivide_NormalValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task IeeeDivide_ByZero_IsInf()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(1.0, 0.0)"))!;
		double.IsPositiveInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task IeeeDivide_NegByZero_IsNegInf()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(-1.0, 0.0)"))!;
		double.IsNegativeInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task IeeeDivide_ZeroByZero_IsNaN()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(0.0, 0.0)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// ROUND edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ROUND(2.5)", 3.0)]
	[InlineData("ROUND(3.5)", 4.0)]
	[InlineData("ROUND(-2.5)", -3.0)]
	[InlineData("ROUND(-3.5)", -4.0)]
	[InlineData("ROUND(1.4999)", 1.0)]
	[InlineData("ROUND(1.5001)", 2.0)]
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	public async Task Round_HalfAwayFromZero(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("ROUND(3.14159, 2)", 3.14)]
	[InlineData("ROUND(3.14159, 4)", 3.1416)]
	[InlineData("ROUND(3.14159, 0)", 3.0)]
	[InlineData("ROUND(123.456, -1)", 120.0)]
	[InlineData("ROUND(123.456, -2)", 100.0)]
	[InlineData("ROUND(150.0, -2)", 200.0)]
	[InlineData("ROUND(1.005, 2)", 1.0)]  // Floating point precision edge case
	public async Task Round_WithPrecision(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Round_Null_IsNull()
	{
		var result = await Eval("ROUND(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TRUNC edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUNC(2.7)", 2.0)]
	[InlineData("TRUNC(2.3)", 2.0)]
	[InlineData("TRUNC(-2.7)", -2.0)]
	[InlineData("TRUNC(-2.3)", -2.0)]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(0.9999)", 0.0)]
	[InlineData("TRUNC(-0.9999)", 0.0)]
	public async Task Trunc_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("TRUNC(3.14159, 2)", 3.14)]
	[InlineData("TRUNC(3.14159, 4)", 3.1415)]
	[InlineData("TRUNC(3.14159, 0)", 3.0)]
	[InlineData("TRUNC(123.456, -1)", 120.0)]
	[InlineData("TRUNC(123.456, -2)", 100.0)]
	public async Task Trunc_WithPrecision(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	// ═══════════════════════════════════════════════════════════════
	// IS_NAN / IS_INF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#is_nan
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IS_NAN(CAST('nan' AS FLOAT64))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[InlineData("IS_NAN(CAST('inf' AS FLOAT64))", false)]
	[InlineData("IS_INF(CAST('inf' AS FLOAT64))", true)]
	[InlineData("IS_INF(CAST('-inf' AS FLOAT64))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[InlineData("IS_INF(0.0)", false)]
	[InlineData("IS_INF(CAST('nan' AS FLOAT64))", false)]
	public async Task IsNanIsInf_Values(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// GREATEST / LEAST with mixed types and NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("GREATEST(1.0, 2.0, 3.0)", 3.0)]
	[InlineData("GREATEST(-1.0, -2.0, -3.0)", -1.0)]
	[InlineData("GREATEST(1.5, 1.5, 1.5)", 1.5)]
	[InlineData("LEAST(1.0, 2.0, 3.0)", 1.0)]
	[InlineData("LEAST(-1.0, -2.0, -3.0)", -3.0)]
	[InlineData("LEAST(1.5, 1.5, 1.5)", 1.5)]
	public async Task GreatestLeast_Floats(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Greatest_WithNull_ReturnsNull()
	{
		var result = await Eval("GREATEST(1, NULL, 3)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Least_WithNull_ReturnsNull()
	{
		var result = await Eval("LEAST(1, NULL, 3)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Greatest_SingleArg()
	{
		var result = (long)(await Eval("GREATEST(42)"))!;
		result.Should().Be(42);
	}

	[Fact]
	public async Task Least_SingleArg()
	{
		var result = (long)(await Eval("LEAST(42)"))!;
		result.Should().Be(42);
	}

	// ═══════════════════════════════════════════════════════════════
	// FARM_FINGERPRINT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions#farm_fingerprint
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task FarmFingerprint_Deterministic()
	{
		var r1 = (long)(await Eval("FARM_FINGERPRINT('hello')"))!;
		var r2 = (long)(await Eval("FARM_FINGERPRINT('hello')"))!;
		r1.Should().Be(r2);
	}

	[Fact]
	public async Task FarmFingerprint_DifferentInputsDifferentOutputs()
	{
		var r1 = (long)(await Eval("FARM_FINGERPRINT('hello')"))!;
		var r2 = (long)(await Eval("FARM_FINGERPRINT('world')"))!;
		r1.Should().NotBe(r2);
	}

	[Fact]
	public async Task FarmFingerprint_EmptyString()
	{
		var result = await Eval("FARM_FINGERPRINT('')");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task FarmFingerprint_Null_IsNull()
	{
		var result = await Eval("FARM_FINGERPRINT(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── SAFE functions with NUMERIC (decimal) ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
	//   "If the result overflows, returns NULL."

	[Fact]
	public async Task SafeDivide_Numeric_PreservesPrecision()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
		//   "SAFE_DIVIDE returns NUMERIC when both arguments are NUMERIC."
		var result = await Eval("SAFE_DIVIDE(NUMERIC '1.0', NUMERIC '3.0')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate)
			.Should().BeApproximately(0.333333333m, 0.000001m);
	}

	[Fact]
	public async Task SafeDivide_Numeric_ZeroDivisor_ReturnsNull()
	{
		var result = await Eval("SAFE_DIVIDE(NUMERIC '10.5', NUMERIC '0')");
		result.Should().BeNull();
	}

	[Fact]
	public async Task SafeNegate_Numeric_Works()
	{
		var result = await Eval("SAFE_NEGATE(NUMERIC '42.5')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate).Should().Be(-42.5m);
	}

	[Fact]
	public async Task SafeAdd_Numeric_PreservesPrecision()
	{
		var result = await Eval("SAFE_ADD(NUMERIC '1.1', NUMERIC '2.2')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate).Should().Be(3.3m);
	}

	[Fact]
	public async Task SafeSubtract_Numeric_PreservesPrecision()
	{
		var result = await Eval("SAFE_SUBTRACT(NUMERIC '5.5', NUMERIC '2.2')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate).Should().Be(3.3m);
	}

	[Fact]
	public async Task SafeMultiply_Numeric_PreservesPrecision()
	{
		var result = await Eval("SAFE_MULTIPLY(NUMERIC '2.5', NUMERIC '4.0')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate).Should().Be(10.0m);
	}

	// ─── Division by zero — all numeric types ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	//   "Divide by zero operations return an error."

	[Fact]
	public async Task Float64_DivideByZero_ThrowsError()
	{
		var act = async () => await Eval("1.0 / 0.0");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Float64_ModuloByZero_ThrowsError()
	{
		var act = async () => await Eval("1.0 % 0.0");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Int64_DivideByZero_ThrowsError()
	{
		var act = async () => await Eval("1 / 0");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Int64_ModuloByZero_ThrowsError()
	{
		var act = async () => await Eval("1 % 0");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Numeric_DivideByZero_ThrowsError()
	{
		var act = async () => await Eval("CAST(1 AS NUMERIC) / CAST(0 AS NUMERIC)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Numeric_ModuloByZero_ThrowsError()
	{
		var act = async () => await Eval("CAST(1 AS NUMERIC) % CAST(0 AS NUMERIC)");
		await act.Should().ThrowAsync<SpannerException>();
	}
}
