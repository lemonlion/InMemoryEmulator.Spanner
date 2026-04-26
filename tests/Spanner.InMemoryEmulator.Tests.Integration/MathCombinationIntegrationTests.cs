using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense math function combination tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MathCombinationIntegrationTests : IntegrationTestBase
{
	public MathCombinationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ABS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#abs
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(100)", 100L)]
	[InlineData("ABS(-100)", 100L)]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	[InlineData("ABS(-0.0)", 0.0)]
	[InlineData("ABS(ABS(-5))", 5L)]
	[InlineData("ABS(-ABS(-3))", 3L)]
	public async Task Abs_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// MOD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(10, 5)", 0L)]
	[InlineData("MOD(10, 7)", 3L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(-10, 3)", -1L)]
	[InlineData("MOD(10, -3)", 1L)]
	[InlineData("MOD(-10, -3)", -1L)]
	[InlineData("MOD(1, 1)", 0L)]
	[InlineData("MOD(7, 2)", 1L)]
	[InlineData("MOD(100, 10)", 0L)]
	[InlineData("MOD(17, 5)", 2L)]
	public async Task Mod_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CEIL and FLOOR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ceil
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(0.1)", 1.0)]
	[InlineData("CEIL(0.5)", 1.0)]
	[InlineData("CEIL(0.9)", 1.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(1.1)", 2.0)]
	[InlineData("CEIL(-0.1)", 0.0)]
	[InlineData("CEIL(-0.5)", 0.0)]
	[InlineData("CEIL(-0.9)", 0.0)]
	[InlineData("CEIL(-1.0)", -1.0)]
	[InlineData("CEIL(-1.1)", -1.0)]
	[InlineData("CEIL(2.5)", 3.0)]
	[InlineData("CEIL(-2.5)", -2.0)]
	[InlineData("CEIL(99.01)", 100.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(0.1)", 0.0)]
	[InlineData("FLOOR(0.5)", 0.0)]
	[InlineData("FLOOR(0.9)", 0.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(1.9)", 1.0)]
	[InlineData("FLOOR(-0.1)", -1.0)]
	[InlineData("FLOOR(-0.5)", -1.0)]
	[InlineData("FLOOR(-0.9)", -1.0)]
	[InlineData("FLOOR(-1.0)", -1.0)]
	[InlineData("FLOOR(-1.1)", -2.0)]
	[InlineData("FLOOR(2.5)", 2.0)]
	[InlineData("FLOOR(-2.5)", -3.0)]
	[InlineData("FLOOR(99.99)", 99.0)]
	public async Task CeilFloor_Combinations(string expr, double expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// ROUND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(0.4)", 0.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(0.6)", 1.0)]
	[InlineData("ROUND(1.0)", 1.0)]
	[InlineData("ROUND(1.5)", 2.0)]
	[InlineData("ROUND(2.5)", 3.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	[InlineData("ROUND(-1.5)", -2.0)]
	[InlineData("ROUND(-2.5)", -3.0)]
	[InlineData("ROUND(3.14159, 0)", 3.0)]
	[InlineData("ROUND(3.14159, 1)", 3.1)]
	[InlineData("ROUND(3.14159, 2)", 3.14)]
	[InlineData("ROUND(3.14159, 3)", 3.142)]
	[InlineData("ROUND(3.14159, 4)", 3.1416)]
	[InlineData("ROUND(2.555, 2)", 2.56)]
	[InlineData("ROUND(-3.14159, 2)", -3.14)]
	[InlineData("ROUND(1234.5678, -1)", 1230.0)]
	[InlineData("ROUND(1234.5678, -2)", 1200.0)]
	[InlineData("ROUND(1234.5678, -3)", 1000.0)]
	public async Task Round_Combinations(string expr, double expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(0.9)", 0.0)]
	[InlineData("TRUNC(1.0)", 1.0)]
	[InlineData("TRUNC(1.9)", 1.0)]
	[InlineData("TRUNC(-0.9)", 0.0)]
	[InlineData("TRUNC(-1.0)", -1.0)]
	[InlineData("TRUNC(-1.9)", -1.0)]
	[InlineData("TRUNC(3.14159, 0)", 3.0)]
	[InlineData("TRUNC(3.14159, 1)", 3.1)]
	[InlineData("TRUNC(3.14159, 2)", 3.14)]
	[InlineData("TRUNC(3.14159, 3)", 3.141)]
	[InlineData("TRUNC(-3.14159, 2)", -3.14)]
	[InlineData("TRUNC(1234.5678, -1)", 1230.0)]
	[InlineData("TRUNC(1234.5678, -2)", 1200.0)]
	public async Task Trunc_Combinations(string expr, double expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SIGN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("SIGN(1)", 1L)]
	[InlineData("SIGN(-1)", -1L)]
	[InlineData("SIGN(100)", 1L)]
	[InlineData("SIGN(-100)", -1L)]
	[InlineData("SIGN(0.0)", 0.0)]
	[InlineData("SIGN(0.001)", 1.0)]
	[InlineData("SIGN(-0.001)", -1.0)]
	[InlineData("SIGN(1e10)", 1.0)]
	[InlineData("SIGN(-1e10)", -1.0)]
	public async Task Sign_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// GREATEST and LEAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("GREATEST(1, 2)", 2L)]
	[InlineData("GREATEST(2, 1)", 2L)]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("GREATEST(-1, -2, -3)", -1L)]
	[InlineData("GREATEST(0, -1, 1)", 1L)]
	[InlineData("GREATEST(1.0, 2.0)", 2.0)]
	[InlineData("GREATEST(1.5, 2.5, 0.5)", 2.5)]
	[InlineData("GREATEST(-0.5, -1.5)", -0.5)]
	[InlineData("LEAST(1, 2)", 1L)]
	[InlineData("LEAST(2, 1)", 1L)]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(1, 1, 1)", 1L)]
	[InlineData("LEAST(-1, -2, -3)", -3L)]
	[InlineData("LEAST(0, -1, 1)", -1L)]
	[InlineData("LEAST(1.0, 2.0)", 1.0)]
	[InlineData("LEAST(1.5, 2.5, 0.5)", 0.5)]
	[InlineData("LEAST(-0.5, -1.5)", -1.5)]
	public async Task GreatestLeast_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SQRT, POW, EXP, LN, LOG, LOG10
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sqrt
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SQRT(0.0)", 0.0)]
	[InlineData("SQRT(1.0)", 1.0)]
	[InlineData("SQRT(4.0)", 2.0)]
	[InlineData("SQRT(9.0)", 3.0)]
	[InlineData("SQRT(16.0)", 4.0)]
	[InlineData("SQRT(25.0)", 5.0)]
	[InlineData("SQRT(100.0)", 10.0)]
	[InlineData("SQRT(0.25)", 0.5)]
	public async Task Sqrt_Combinations(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("POW(2, 0)", 1.0)]
	[InlineData("POW(2, 1)", 2.0)]
	[InlineData("POW(2, 2)", 4.0)]
	[InlineData("POW(2, 3)", 8.0)]
	[InlineData("POW(2, 10)", 1024.0)]
	[InlineData("POW(3, 2)", 9.0)]
	[InlineData("POW(10, 3)", 1000.0)]
	[InlineData("POW(0, 0)", 1.0)]
	[InlineData("POW(0, 1)", 0.0)]
	[InlineData("POW(1, 100)", 1.0)]
	[InlineData("POW(-2, 2)", 4.0)]
	[InlineData("POW(-2, 3)", -8.0)]
	[InlineData("POW(0.5, 2)", 0.25)]
	[InlineData("POW(10, -1)", 0.1)]
	[InlineData("POW(10, -2)", 0.01)]
	public async Task Pow_Combinations(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("EXP(0)", 1.0)]
	[InlineData("EXP(1)", 2.718281828459045)]
	[InlineData("LN(1)", 0.0)]
	[InlineData("LOG10(1)", 0.0)]
	[InlineData("LOG10(10)", 1.0)]
	[InlineData("LOG10(100)", 2.0)]
	[InlineData("LOG10(1000)", 3.0)]
	[InlineData("LOG(8, 2)", 3.0)]
	[InlineData("LOG(100, 10)", 2.0)]
	[InlineData("LOG(27, 3)", 3.0)]
	public async Task ExpLnLog_Combinations(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	// ═══════════════════════════════════════════════════════════════
	// IEEE_DIVIDE and DIV
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 2.0)", 5.0)]
	[InlineData("IEEE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	[InlineData("IEEE_DIVIDE(-10.0, 3.0)", -3.3333333333333335)]
	[InlineData("IEEE_DIVIDE(0.0, 1.0)", 0.0)]
	public async Task IeeeDivide_Combinations(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("IEEE_DIVIDE(1.0, 0.0)")]
	public async Task IeeeDivide_ByZero_ReturnsInfinity(string expr)
	{
		var result = (double)(await Eval(expr))!;
		double.IsInfinity(result).Should().BeTrue();
	}

	[Theory]
	[InlineData("IEEE_DIVIDE(0.0, 0.0)")]
	public async Task IeeeDivide_ZeroOverZero_ReturnsNaN(string expr)
	{
		var result = (double)(await Eval(expr))!;
		double.IsNaN(result).Should().BeTrue();
	}

	[Theory]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(10, 5)", 2L)]
	[InlineData("DIV(10, 7)", 1L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(-10, 3)", -3L)]
	[InlineData("DIV(10, -3)", -3L)]
	[InlineData("DIV(-10, -3)", 3L)]
	[InlineData("DIV(100, 10)", 10L)]
	[InlineData("DIV(7, 2)", 3L)]
	[InlineData("DIV(1, 1)", 1L)]
	public async Task Div_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// IS_NAN and IS_INF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#is_nan
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[InlineData("IS_NAN(IEEE_DIVIDE(1.0, 0.0))", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(IEEE_DIVIDE(-1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[InlineData("IS_INF(0.0)", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(0.0, 0.0))", false)]
	public async Task IsNanIsInf_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SAFE math functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 2)", 5.0)]
	[InlineData("SAFE_DIVIDE(10, 3)", 3.3333333333333335)]
	[InlineData("SAFE_DIVIDE(0, 1)", 0.0)]
	public async Task SafeDivide_ValidResults(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("SAFE_DIVIDE(1, 0)")]
	[InlineData("SAFE_DIVIDE(0, 0)")]
	public async Task SafeDivide_ByZero_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	public async Task SafeNegate_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Arithmetic combos
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 1", 2L)]
	[InlineData("1 + 2 + 3", 6L)]
	[InlineData("10 - 3", 7L)]
	[InlineData("10 - 3 - 2", 5L)]
	[InlineData("3 * 4", 12L)]
	[InlineData("2 * 3 * 4", 24L)]
	[InlineData("10 / 2", 5L)]
	[InlineData("10 / 3", 3L)]
	[InlineData("100 / 10 / 2", 5L)]
	[InlineData("-5", -5L)]
	[InlineData("-(3 + 2)", -5L)]
	[InlineData("1 + 2 * 3", 7L)]
	[InlineData("(1 + 2) * 3", 9L)]
	[InlineData("2 * (3 + 4)", 14L)]
	[InlineData("10 - 2 * 3", 4L)]
	[InlineData("(10 - 2) * 3", 24L)]
	[InlineData("1 + 2 * 3 - 4 / 2", 5L)]
	[InlineData("(1 + 2) * (3 - 4) / 1", -3L)]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task IntArithmetic_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("1.0 + 1.0", 2.0)]
	[InlineData("1.5 + 2.5", 4.0)]
	[InlineData("10.0 - 3.5", 6.5)]
	[InlineData("2.5 * 4.0", 10.0)]
	[InlineData("10.0 / 4.0", 2.5)]
	[InlineData("1.0 / 3.0", 0.3333333333333333)]
	[InlineData("0.1 + 0.2", 0.30000000000000004)]
	[InlineData("-1.5 * 2.0", -3.0)]
	[InlineData("3.14 * 2.0", 6.28)]
	[InlineData("100.0 / 7.0", 14.285714285714286)]
	public async Task FloatArithmetic_Combinations(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	// ═══════════════════════════════════════════════════════════════
	// Complex math pipelines
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ABS(ROUND(-3.7))", 4.0)]
	[InlineData("CEIL(SQRT(10.0))", 4.0)]
	[InlineData("FLOOR(SQRT(10.0))", 3.0)]
	[InlineData("ROUND(SQRT(2.0), 4)", 1.4142)]
	[InlineData("ABS(-5) + ABS(-3)", 8L)]
	[InlineData("SIGN(-5) * ABS(-5)", -5L)]
	[InlineData("GREATEST(ABS(-3), ABS(-5), ABS(-1))", 5L)]
	[InlineData("LEAST(ABS(-3), ABS(-5), ABS(-1))", 1L)]
	[InlineData("MOD(ABS(-10), 3)", 1L)]
	[InlineData("ROUND(POW(2.0, 0.5), 4)", 1.4142)]
	[InlineData("CEIL(LN(10.0))", 3.0)]
	[InlineData("FLOOR(LOG10(500.0))", 2.0)]
	[InlineData("ROUND(EXP(1.0), 4)", 2.7183)]
	[InlineData("POW(SQRT(16.0), 2)", 16.0)]
	[InlineData("SQRT(POW(3.0, 2) + POW(4.0, 2))", 5.0)]
	public async Task MathPipeline_Combinations(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double dbl)
			((double)result!).Should().BeApproximately(dbl, 1e-4);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL propagation through math functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ABS(CAST(NULL AS INT64))")]
	[InlineData("MOD(CAST(NULL AS INT64), 3)")]
	[InlineData("MOD(10, CAST(NULL AS INT64))")]
	[InlineData("CEIL(CAST(NULL AS FLOAT64))")]
	[InlineData("FLOOR(CAST(NULL AS FLOAT64))")]
	[InlineData("ROUND(CAST(NULL AS FLOAT64))")]
	[InlineData("ROUND(CAST(NULL AS FLOAT64), 2)")]
	[InlineData("TRUNC(CAST(NULL AS FLOAT64))")]
	[InlineData("SIGN(CAST(NULL AS INT64))")]
	[InlineData("SIGN(CAST(NULL AS FLOAT64))")]
	[InlineData("GREATEST(1, CAST(NULL AS INT64))")]
	[InlineData("LEAST(1, CAST(NULL AS INT64))")]
	[InlineData("SQRT(CAST(NULL AS FLOAT64))")]
	[InlineData("POW(CAST(NULL AS FLOAT64), 2)")]
	[InlineData("POW(2, CAST(NULL AS FLOAT64))")]
	[InlineData("EXP(CAST(NULL AS FLOAT64))")]
	[InlineData("LN(CAST(NULL AS FLOAT64))")]
	[InlineData("LOG10(CAST(NULL AS FLOAT64))")]
	[InlineData("LOG(CAST(NULL AS FLOAT64), 2)")]
	[InlineData("IEEE_DIVIDE(CAST(NULL AS FLOAT64), 1.0)")]
	[InlineData("IEEE_DIVIDE(1.0, CAST(NULL AS FLOAT64))")]
	[InlineData("DIV(CAST(NULL AS INT64), 3)")]
	[InlineData("DIV(10, CAST(NULL AS INT64))")]
	[InlineData("SAFE_DIVIDE(CAST(NULL AS INT64), 3)")]
	[InlineData("SAFE_NEGATE(CAST(NULL AS INT64))")]
	[InlineData("CAST(NULL AS INT64) + 1")]
	[InlineData("1 + CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) * 2")]
	[InlineData("CAST(NULL AS INT64) - 1")]
	[InlineData("10 / CAST(NULL AS INT64)")]
	public async Task MathFunction_NullInput_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Comparison operators — comprehensive
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// INT64 comparisons
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("1 != 2", true)]
	[InlineData("1 != 1", false)]
	[InlineData("1 <> 2", true)]
	[InlineData("1 <> 1", false)]
	[InlineData("1 < 2", true)]
	[InlineData("2 < 1", false)]
	[InlineData("1 < 1", false)]
	[InlineData("1 <= 2", true)]
	[InlineData("1 <= 1", true)]
	[InlineData("2 <= 1", false)]
	[InlineData("2 > 1", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 > 1", false)]
	[InlineData("1 >= 1", true)]
	[InlineData("2 >= 1", true)]
	[InlineData("1 >= 2", false)]
	// FLOAT64 comparisons
	[InlineData("1.0 = 1.0", true)]
	[InlineData("1.0 = 2.0", false)]
	[InlineData("1.5 < 2.5", true)]
	[InlineData("2.5 > 1.5", true)]
	[InlineData("1.5 <= 1.5", true)]
	[InlineData("1.5 >= 1.5", true)]
	// STRING comparisons
	[InlineData("'abc' = 'abc'", true)]
	[InlineData("'abc' = 'def'", false)]
	[InlineData("'abc' != 'def'", true)]
	[InlineData("'abc' < 'def'", true)]
	[InlineData("'def' > 'abc'", true)]
	[InlineData("'abc' <= 'abc'", true)]
	[InlineData("'abc' >= 'abc'", true)]
	[InlineData("'' = ''", true)]
	[InlineData("'' < 'a'", true)]
	// BOOL comparisons
	[InlineData("TRUE = TRUE", true)]
	[InlineData("FALSE = FALSE", true)]
	[InlineData("TRUE = FALSE", false)]
	[InlineData("TRUE != FALSE", true)]
	// Mixed numeric
	[InlineData("1 = 1.0", true)]
	[InlineData("1 < 1.5", true)]
	[InlineData("2.0 > 1", true)]
	public async Task Comparison_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// BETWEEN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("11 BETWEEN 1 AND 10", false)]
	[InlineData("5 NOT BETWEEN 1 AND 10", false)]
	[InlineData("0 NOT BETWEEN 1 AND 10", true)]
	[InlineData("1.5 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	[InlineData("DATE '2024-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2025-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	public async Task Between_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// IN operator
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("1.5 IN (1.0, 1.5, 2.0)", true)]
	[InlineData("TRUE IN (TRUE, FALSE)", true)]
	[InlineData("1 IN (1)", true)]
	[InlineData("'abc' IN ('abc', 'def', 'ghi')", true)]
	public async Task In_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// IS NULL / IS NOT NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) IS NULL", true)]
	[InlineData("CAST(NULL AS STRING) IS NULL", true)]
	[InlineData("CAST(NULL AS BOOL) IS NULL", true)]
	[InlineData("CAST(NULL AS FLOAT64) IS NULL", true)]
	[InlineData("1 IS NULL", false)]
	[InlineData("'abc' IS NULL", false)]
	[InlineData("TRUE IS NULL", false)]
	[InlineData("0.0 IS NULL", false)]
	[InlineData("CAST(NULL AS INT64) IS NOT NULL", false)]
	[InlineData("CAST(NULL AS STRING) IS NOT NULL", false)]
	[InlineData("1 IS NOT NULL", true)]
	[InlineData("'abc' IS NOT NULL", true)]
	[InlineData("TRUE IS NOT NULL", true)]
	public async Task IsNull_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Boolean logic 
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUE AND TRUE", true)]
	[InlineData("TRUE AND FALSE", false)]
	[InlineData("FALSE AND TRUE", false)]
	[InlineData("FALSE AND FALSE", false)]
	[InlineData("TRUE OR TRUE", true)]
	[InlineData("TRUE OR FALSE", true)]
	[InlineData("FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE", false)]
	[InlineData("NOT TRUE", false)]
	[InlineData("NOT FALSE", true)]
	[InlineData("TRUE AND TRUE AND TRUE", true)]
	[InlineData("TRUE AND TRUE AND FALSE", false)]
	[InlineData("FALSE OR FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE OR FALSE", false)]
	[InlineData("NOT (TRUE AND FALSE)", true)]
	[InlineData("NOT (FALSE OR FALSE)", true)]
	[InlineData("(TRUE OR FALSE) AND (TRUE OR FALSE)", true)]
	[InlineData("(TRUE AND FALSE) OR (FALSE AND TRUE)", false)]
	[InlineData("NOT NOT TRUE", true)]
	[InlineData("NOT NOT FALSE", false)]
	public async Task BooleanLogic_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// COALESCE, IF, IFNULL, NULLIF, CASE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COALESCE(1, 2)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64), 3)", 3L)]
	[InlineData("COALESCE(1, CAST(NULL AS INT64))", 1L)]
	[InlineData("COALESCE('a', 'b')", "a")]
	[InlineData("COALESCE(CAST(NULL AS STRING), 'b')", "b")]
	[InlineData("IF(TRUE, 1, 2)", 1L)]
	[InlineData("IF(FALSE, 1, 2)", 2L)]
	[InlineData("IF(1 = 1, 'yes', 'no')", "yes")]
	[InlineData("IF(1 = 2, 'yes', 'no')", "no")]
	[InlineData("IF(TRUE, 'a', 'b')", "a")]
	[InlineData("IF(FALSE, 'a', 'b')", "b")]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("IFNULL('a', 'b')", "a")]
	[InlineData("IFNULL(CAST(NULL AS STRING), 'b')", "b")]
	[InlineData("NULLIF(1, 1)", null)]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF('a', 'a')", null)]
	[InlineData("NULLIF('a', 'b')", "a")]
	public async Task Conditional_Combinations(string expr, object? expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "other")]
	[InlineData("CASE WHEN TRUE THEN 'yes' ELSE 'no' END", "yes")]
	[InlineData("CASE WHEN FALSE THEN 'yes' ELSE 'no' END", "no")]
	[InlineData("CASE WHEN 1 = 1 THEN 'a' WHEN 2 = 2 THEN 'b' ELSE 'c' END", "a")]
	[InlineData("CASE WHEN 1 = 2 THEN 'a' WHEN 2 = 2 THEN 'b' ELSE 'c' END", "b")]
	[InlineData("CASE WHEN 1 = 2 THEN 'a' WHEN 2 = 3 THEN 'b' ELSE 'c' END", "c")]
	[InlineData("CASE WHEN 1 > 0 THEN 'pos' WHEN 1 < 0 THEN 'neg' ELSE 'zero' END", "pos")]
	[InlineData("CASE WHEN -1 > 0 THEN 'pos' WHEN -1 < 0 THEN 'neg' ELSE 'zero' END", "neg")]
	[InlineData("CASE WHEN 0 > 0 THEN 'pos' WHEN 0 < 0 THEN 'neg' ELSE 'zero' END", "zero")]
	public async Task Case_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Nested conditionals
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(TRUE, IF(TRUE, 1, 2), 3)", 1L)]
	[InlineData("IF(TRUE, IF(FALSE, 1, 2), 3)", 2L)]
	[InlineData("IF(FALSE, 1, IF(TRUE, 2, 3))", 2L)]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 2), 3)", 3L)]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 3), 4)", 2L)]
	[InlineData("IFNULL(NULLIF(1, 1), 2)", 2L)]
	[InlineData("IFNULL(NULLIF(1, 2), 99)", 1L)]
	[InlineData("IF(COALESCE(CAST(NULL AS BOOL), TRUE), 'a', 'b')", "a")]
	public async Task NestedConditional_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);
}
