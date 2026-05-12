using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for mathematical functions:
/// trig domain errors, IEEE special values, ROUND modes, DIV/MOD edge cases, overflow behavior.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MathFunctionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public MathFunctionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// Trigonometric functions - basic values
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sin
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SIN(0)", 0.0)]
	[InlineData("COS(0)", 1.0)]
	[InlineData("TAN(0)", 0.0)]
	[InlineData("ASIN(0)", 0.0)]
	[InlineData("ACOS(1)", 0.0)]
	[InlineData("ATAN(0)", 0.0)]
	[InlineData("SINH(0)", 0.0)]
	[InlineData("COSH(0)", 1.0)]
	[InlineData("TANH(0)", 0.0)]
	public async Task TrigFunctions_ZeroInput_ReturnsExpected(string expr, double expected)
	{
		var result = Convert.ToDouble(await Eval(expr));
		result.Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("SIN(1)", 0.8414709848078965)]
	[InlineData("COS(1)", 0.5403023058681398)]
	[InlineData("TAN(1)", 1.5574077246549023)]
	[InlineData("ASIN(0.5)", 0.5235987755982989)]
	[InlineData("ACOS(0.5)", 1.0471975511965979)]
	[InlineData("ATAN(1)", 0.7853981633974483)]
	[InlineData("ATAN2(1, 1)", 0.7853981633974483)]
	[InlineData("ATAN2(0, 1)", 0.0)]
	[InlineData("ATAN2(1, 0)", 1.5707963267948966)]
	[InlineData("ATAN2(-1, 0)", -1.5707963267948966)]
	public async Task TrigFunctions_StandardValues(string expr, double expected)
	{
		var result = Convert.ToDouble(await Eval(expr));
		result.Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("SINH(1)", 1.1752011936438014)]
	[InlineData("COSH(1)", 1.5430806348152437)]
	[InlineData("TANH(1)", 0.7615941559557649)]
	[InlineData("ASINH(0)", 0.0)]
	[InlineData("ASINH(1)", 0.88137358701954305)]
	[InlineData("ACOSH(1)", 0.0)]
	[InlineData("ACOSH(2)", 1.3169578969248166)]
	[InlineData("ATANH(0)", 0.0)]
	[InlineData("ATANH(0.5)", 0.5493061443340548)]
	public async Task HyperbolicFunctions_StandardValues(string expr, double expected)
	{
		var result = Convert.ToDouble(await Eval(expr));
		result.Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Trig functions with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SIN(NULL)")]
	[InlineData("COS(NULL)")]
	[InlineData("TAN(NULL)")]
	[InlineData("ASIN(NULL)")]
	[InlineData("ACOS(NULL)")]
	[InlineData("ATAN(NULL)")]
	[InlineData("ATAN2(NULL, 1)")]
	[InlineData("ATAN2(1, NULL)")]
	[InlineData("SINH(NULL)")]
	[InlineData("COSH(NULL)")]
	[InlineData("TANH(NULL)")]
	[InlineData("ASINH(NULL)")]
	[InlineData("ACOSH(NULL)")]
	[InlineData("ATANH(NULL)")]
	public async Task TrigFunctions_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// IEEE special values: NaN, Infinity
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IEEE_DIVIDE(0.0, 0.0)")]           // NaN
	[InlineData("IEEE_DIVIDE(CAST('nan' AS FLOAT64), 1.0)")]
	public async Task IeeeDivide_ProducesNan(string expr)
	{
		var result = Convert.ToDouble(await Eval(expr));
		double.IsNaN(result).Should().BeTrue();
	}

	[Theory]
	[InlineData("IEEE_DIVIDE(1.0, 0.0)", double.PositiveInfinity)]
	[InlineData("IEEE_DIVIDE(-1.0, 0.0)", double.NegativeInfinity)]
	public async Task IeeeDivide_ProducesInfinity(string expr, double expected)
	{
		var result = Convert.ToDouble(await Eval(expr));
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(IEEE_DIVIDE(-1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[InlineData("IS_INF(0.0)", false)]
	public async Task IsNanIsInf_CorrectResults(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_DIVIDE, SAFE_NEGATE, SAFE_ADD, SAFE_SUBTRACT, SAFE_MULTIPLY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 2)", 5.0)]
	[InlineData("SAFE_DIVIDE(10, 3)", 3.3333333333333335)]
	[InlineData("SAFE_DIVIDE(0, 5)", 0.0)]
	[InlineData("SAFE_DIVIDE(-10, 2)", -5.0)]
	public async Task SafeDivide_NormalValues(string expr, double expected)
	{
		var result = Convert.ToDouble(await Eval(expr));
		result.Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("SAFE_DIVIDE(1, 0)")]
	[InlineData("SAFE_DIVIDE(0, 0)")]
	[InlineData("SAFE_DIVIDE(-1, 0)")]
	public async Task SafeDivide_DivisionByZero_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	public async Task SafeNegate_NormalValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task SafeNegate_MinInt64_ReturnsNull()
	{
		// -9223372036854775808 cannot be negated
		(await Eval("SAFE_NEGATE(-9223372036854775808)")).Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_ADD(1, 2)", 3L)]
	[InlineData("SAFE_ADD(-1, 1)", 0L)]
	[InlineData("SAFE_ADD(0, 0)", 0L)]
	[InlineData("SAFE_SUBTRACT(10, 3)", 7L)]
	[InlineData("SAFE_SUBTRACT(0, 0)", 0L)]
	[InlineData("SAFE_SUBTRACT(5, 10)", -5L)]
	[InlineData("SAFE_MULTIPLY(3, 4)", 12L)]
	[InlineData("SAFE_MULTIPLY(0, 999)", 0L)]
	[InlineData("SAFE_MULTIPLY(-2, 3)", -6L)]
	public async Task SafeArith_NormalValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_ADD(9223372036854775807, 1)")]
	[InlineData("SAFE_SUBTRACT(-9223372036854775808, 1)")]
	[InlineData("SAFE_MULTIPLY(9223372036854775807, 2)")]
	public async Task SafeArith_Overflow_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ROUND and TRUNC with precision
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ROUND(2.5)", 3.0)]
	[InlineData("ROUND(3.5)", 4.0)]
	[InlineData("ROUND(-2.5)", -3.0)]
	[InlineData("ROUND(-3.5)", -4.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	[InlineData("ROUND(1.0)", 1.0)]
	[InlineData("ROUND(1.49)", 1.0)]
	[InlineData("ROUND(1.51)", 2.0)]
	[InlineData("ROUND(0.0)", 0.0)]
	public async Task Round_NoDecimalPlaces(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("ROUND(1.2345, 2)", 1.23)]
	[InlineData("ROUND(1.2355, 2)", 1.24)]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	//   "Rounds halfway cases away from zero."
	[InlineData("ROUND(1.2345, 3)", 1.235)]
	[InlineData("ROUND(1.2345, 0)", 1.0)]
	[InlineData("ROUND(99.999, 1)", 100.0)]
	[InlineData("ROUND(-1.2345, 2)", -1.23)]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Round_WithDecimalPlaces(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("TRUNC(2.9)", 2.0)]
	[InlineData("TRUNC(-2.9)", -2.0)]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(1.0)", 1.0)]
	[InlineData("TRUNC(1.999)", 1.0)]
	[InlineData("TRUNC(-1.999)", -1.0)]
	public async Task Trunc_NoDecimalPlaces(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("TRUNC(1.2345, 2)", 1.23)]
	[InlineData("TRUNC(1.2399, 2)", 1.23)]
	[InlineData("TRUNC(-1.2345, 2)", -1.23)]
	[InlineData("TRUNC(1.2345, 0)", 1.0)]
	public async Task Trunc_WithDecimalPlaces(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// DIV and MOD edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(10, -3)", -3L)]
	[InlineData("DIV(-10, 3)", -3L)]    // truncates toward zero per Spanner spec
	[InlineData("DIV(-10, -3)", 3L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(7, 1)", 7L)]
	[InlineData("DIV(7, 7)", 1L)]
	public async Task Div_IntegerDivision(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(10, -3)", 1L)]
	[InlineData("MOD(-10, 3)", -1L)]    // C-style remainder, sign follows dividend
	[InlineData("MOD(-10, -3)", -1L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(7, 1)", 0L)]
	[InlineData("MOD(7, 7)", 0L)]
	public async Task Mod_Remainder(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// ABS, SIGN edge cases
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(9223372036854775807)", 9223372036854775807L)]
	public async Task Abs_IntValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	public async Task Abs_FloatValues(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("SIGN(1)", 1L)]
	[InlineData("SIGN(-1)", -1L)]
	[InlineData("SIGN(100)", 1L)]
	[InlineData("SIGN(-100)", -1L)]
	public async Task Sign_IntValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SIGN(0.0)", 0L)]
	[InlineData("SIGN(1.5)", 1L)]
	[InlineData("SIGN(-1.5)", -1L)]
	public async Task Sign_FloatValues(string expr, long expected)
	{
		// Spanner SIGN always returns INT64
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// SQRT, POW, EXP, LN, LOG, LOG10
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sqrt
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SQRT(0)", 0.0)]
	[InlineData("SQRT(1)", 1.0)]
	[InlineData("SQRT(4)", 2.0)]
	[InlineData("SQRT(9)", 3.0)]
	[InlineData("SQRT(2)", 1.4142135623730951)]
	[InlineData("SQRT(0.25)", 0.5)]
	public async Task Sqrt_Values(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("POW(2, 0)", 1.0)]
	[InlineData("POW(2, 1)", 2.0)]
	[InlineData("POW(2, 10)", 1024.0)]
	[InlineData("POW(10, 3)", 1000.0)]
	[InlineData("POW(0, 0)", 1.0)]
	[InlineData("POW(0, 1)", 0.0)]
	[InlineData("POW(-2, 2)", 4.0)]
	[InlineData("POW(-2, 3)", -8.0)]
	public async Task Pow_Values(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("EXP(0)", 1.0)]
	[InlineData("EXP(1)", 2.718281828459045)]
	[InlineData("EXP(-1)", 0.36787944117144233)]
	[InlineData("LN(1)", 0.0)]
	[InlineData("LN(2.718281828459045)", 1.0)]
	[InlineData("LOG(1)", 0.0)]
	[InlineData("LOG10(1)", 0.0)]
	[InlineData("LOG10(10)", 1.0)]
	[InlineData("LOG10(100)", 2.0)]
	[InlineData("LOG10(1000)", 3.0)]
	public async Task ExpLnLog_Values(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// GREATEST / LEAST with multiple values
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("GREATEST(-1, -2, -3)", -1L)]
	[InlineData("GREATEST(0, -1, 1)", 1L)]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(1, 1, 1)", 1L)]
	[InlineData("LEAST(-1, -2, -3)", -3L)]
	[InlineData("LEAST(0, -1, 1)", -1L)]
	public async Task GreatestLeast_MultipleValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("GREATEST(1, NULL, 3)")]
	[InlineData("LEAST(1, NULL, 3)")]
	[InlineData("GREATEST(NULL, 2)")]
	[InlineData("LEAST(NULL, 2)")]
	public async Task GreatestLeast_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("GREATEST('a', 'b', 'c')", "c")]
	[InlineData("GREATEST('c', 'b', 'a')", "c")]
	[InlineData("LEAST('a', 'b', 'c')", "a")]
	[InlineData("LEAST('c', 'b', 'a')", "a")]
	[InlineData("GREATEST('abc', 'abd', 'abe')", "abe")]
	[InlineData("LEAST('abc', 'abd', 'abe')", "abc")]
	public async Task GreatestLeast_StringValues(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CEIL / FLOOR edge cases
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(0.1)", 1.0)]
	[InlineData("CEIL(0.9)", 1.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(1.1)", 2.0)]
	[InlineData("CEIL(-0.1)", 0.0)]
	[InlineData("CEIL(-0.9)", 0.0)]
	[InlineData("CEIL(-1.0)", -1.0)]
	[InlineData("CEIL(-1.1)", -1.0)]
	[InlineData("CEILING(2.3)", 3.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(0.1)", 0.0)]
	[InlineData("FLOOR(0.9)", 0.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(1.9)", 1.0)]
	[InlineData("FLOOR(-0.1)", -1.0)]
	[InlineData("FLOOR(-0.9)", -1.0)]
	[InlineData("FLOOR(-1.0)", -1.0)]
	[InlineData("FLOOR(-1.1)", -2.0)]
	public async Task CeilFloor_EdgeCases(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BIT_COUNT(0)", 0L)]
	[InlineData("BIT_COUNT(1)", 1L)]
	[InlineData("BIT_COUNT(2)", 1L)]
	[InlineData("BIT_COUNT(3)", 2L)]
	[InlineData("BIT_COUNT(7)", 3L)]
	[InlineData("BIT_COUNT(255)", 8L)]
	[InlineData("BIT_COUNT(-1)", 64L)]  // all bits set for INT64
	public async Task BitCount_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task BitCount_Null_ReturnsNull()
	{
		(await Eval("BIT_COUNT(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Negative number and boundary math
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 2", 3L)]
	[InlineData("1 - 2", -1L)]
	[InlineData("3 * 4", 12L)]
	// Division cases removed — INT64 / INT64 returns FLOAT64 per Spanner spec
	public async Task BasicArithmetic_IntResults(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 + 2.0", 3.0)]
	[InlineData("1.5 + 2.5", 4.0)]
	[InlineData("10.0 / 3.0", 3.3333333333333335)]
	[InlineData("1.0 / 3.0", 0.3333333333333333)]
	public async Task BasicArithmetic_FloatResults(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// LOG with base argument
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#log
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LOG(8, 2)", 3.0)]
	[InlineData("LOG(27, 3)", 3.0)]
	[InlineData("LOG(100, 10)", 2.0)]
	[InlineData("LOG(1, 10)", 0.0)]
	public async Task Log_WithBase(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}
}
