using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive math function tests covering all Spanner math functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MathExhaustiveIntegrationTests : IntegrationTestBase
{
	public MathExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── ABS ───
	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(42)", 42L)]
	[InlineData("ABS(-42)", 42L)]
	[InlineData("ABS(9223372036854775807)", 9223372036854775807L)]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	[InlineData("ABS(-3.14)", 3.14)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Abs(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ─── SIGN ───
	[Theory]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("SIGN(42)", 1L)]
	[InlineData("SIGN(-42)", -1L)]
	[InlineData("SIGN(0.0)", 0L)]
	[InlineData("SIGN(3.14)", 1L)]
	[InlineData("SIGN(-3.14)", -1L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Sign(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── MOD ───
	[Theory]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(10, 2)", 0L)]
	[InlineData("MOD(7, 4)", 3L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(-10, 3)", -1L)]
	[InlineData("MOD(10, -3)", 1L)]
	[InlineData("MOD(-10, -3)", -1L)]
	[InlineData("MOD(100, 7)", 2L)]
	[InlineData("MOD(15, 5)", 0L)]
	[InlineData("MOD(1, 1)", 0L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Mod(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── POWER / POW ───
	[Theory]
	[InlineData("POWER(2, 0)", 1.0)]
	[InlineData("POWER(2, 1)", 2.0)]
	[InlineData("POWER(2, 10)", 1024.0)]
	[InlineData("POWER(3, 3)", 27.0)]
	[InlineData("POWER(10, 0)", 1.0)]
	[InlineData("POWER(10, 1)", 10.0)]
	[InlineData("POWER(10, 2)", 100.0)]
	[InlineData("POW(2, 8)", 256.0)]
	[InlineData("POWER(0, 0)", 1.0)]
	[InlineData("POWER(1, 100)", 1.0)]
	[InlineData("POWER(2.0, 0.5)", 1.4142135623730951)]
	[InlineData("POWER(-1, 2)", 1.0)]
	[InlineData("POWER(-1, 3)", -1.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Power(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── SQRT ───
	[Theory]
	[InlineData("SQRT(0)", 0.0)]
	[InlineData("SQRT(1)", 1.0)]
	[InlineData("SQRT(4)", 2.0)]
	[InlineData("SQRT(9)", 3.0)]
	[InlineData("SQRT(16)", 4.0)]
	[InlineData("SQRT(25)", 5.0)]
	[InlineData("SQRT(100)", 10.0)]
	[InlineData("SQRT(2)", 1.4142135623730951)]
	[InlineData("SQRT(0.25)", 0.5)]
	[InlineData("SQRT(0.01)", 0.1)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Sqrt(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── CEIL / CEILING / FLOOR ───
	[Theory]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(1.1)", 2.0)]
	[InlineData("CEIL(1.9)", 2.0)]
	[InlineData("CEIL(-1.1)", -1.0)]
	[InlineData("CEIL(-1.9)", -1.0)]
	[InlineData("CEIL(2.0)", 2.0)]
	[InlineData("CEILING(3.14)", 4.0)]
	[InlineData("CEILING(-3.14)", -3.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(1.1)", 1.0)]
	[InlineData("FLOOR(1.9)", 1.0)]
	[InlineData("FLOOR(-1.1)", -2.0)]
	[InlineData("FLOOR(-1.9)", -2.0)]
	[InlineData("FLOOR(2.0)", 2.0)]
	[InlineData("FLOOR(3.14)", 3.0)]
	[InlineData("FLOOR(-3.14)", -4.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task CeilFloor(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── ROUND ───
	[Theory]
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(1.4)", 1.0)]
	[InlineData("ROUND(1.5)", 2.0)]
	[InlineData("ROUND(1.6)", 2.0)]
	[InlineData("ROUND(2.5)", 3.0)]
	[InlineData("ROUND(-1.5)", -2.0)]
	[InlineData("ROUND(-2.5)", -3.0)]
	[InlineData("ROUND(3.14)", 3.0)]
	[InlineData("ROUND(3.14, 1)", 3.1)]
	[InlineData("ROUND(3.145, 2)", 3.15)]
	// Note: ROUND(3.155, 2) omitted — IEEE 754 platform-specific edge case tested in FinalBatchIntegrationTests (InMemoryOnly).
	[InlineData("ROUND(100.0)", 100.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(1234.5678, 2)", 1234.57)]
	[InlineData("ROUND(1234.5678, 0)", 1235.0)]
	[InlineData("ROUND(0.0, 5)", 0.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Round(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── TRUNC ───
	[Theory]
	[InlineData("TRUNC(1.9)", 1.0)]
	[InlineData("TRUNC(-1.9)", -1.0)]
	[InlineData("TRUNC(1.5)", 1.0)]
	[InlineData("TRUNC(-1.5)", -1.0)]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(3.14)", 3.0)]
	[InlineData("TRUNC(-3.14)", -3.0)]
	[InlineData("TRUNC(3.14159, 2)", 3.14)]
	[InlineData("TRUNC(3.14159, 4)", 3.1415)]
	[InlineData("TRUNC(3.14159, 0)", 3.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Trunc(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── LN / LOG / LOG10 ───
	[Theory]
	[InlineData("LN(1)", 0.0)]
	[InlineData("LN(2.718281828459045)", 1.0)]
	[InlineData("LOG(1)", 0.0)]
	[InlineData("LOG(10)", 2.302585092994046)]
	[InlineData("LOG(100, 10)", 2.0)]
	[InlineData("LOG10(1)", 0.0)]
	[InlineData("LOG10(10)", 1.0)]
	[InlineData("LOG10(100)", 2.0)]
	[InlineData("LOG10(1000)", 3.0)]
	[InlineData("LOG(8, 2)", 3.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Logarithms(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── EXP ───
	[Theory]
	[InlineData("EXP(0)", 1.0)]
	[InlineData("EXP(1)", 2.718281828459045)]
	[InlineData("EXP(2)", 7.38905609893065)]
	[InlineData("EXP(-1)", 0.36787944117144233)]
	[InlineData("EXP(0.5)", 1.6487212707001282)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Exp(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── GREATEST / LEAST ───
	[Theory]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(-1, -2, -3)", -1L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("GREATEST(42)", 42L)]
	[InlineData("GREATEST(1, 2)", 2L)]
	[InlineData("GREATEST(100, 50, 75)", 100L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Greatest(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(-1, -2, -3)", -3L)]
	[InlineData("LEAST(1, 1, 1)", 1L)]
	[InlineData("LEAST(42)", 42L)]
	[InlineData("LEAST(1, 2)", 1L)]
	[InlineData("LEAST(100, 50, 75)", 50L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Least(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── DIV ───
	[Theory]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(10, 2)", 5L)]
	[InlineData("DIV(7, 4)", 1L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(-10, 3)", -3L)]
	[InlineData("DIV(10, -3)", -3L)]
	[InlineData("DIV(100, 7)", 14L)]
	[InlineData("DIV(15, 5)", 3L)]
	[InlineData("DIV(1, 1)", 1L)]
	[InlineData("DIV(99, 10)", 9L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Div(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── IEEE_DIVIDE ───
	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 3.0)", 3.3333333333333335)]
	[InlineData("IEEE_DIVIDE(10.0, 0.0)", double.PositiveInfinity)]
	[InlineData("IEEE_DIVIDE(-10.0, 0.0)", double.NegativeInfinity)]
	[InlineData("IEEE_DIVIDE(0.0, 0.0)", double.NaN)]
	[InlineData("IEEE_DIVIDE(1.0, 2.0)", 0.5)]
	[InlineData("IEEE_DIVIDE(100.0, 4.0)", 25.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task IeeeDivide(string expr, double expected)
	{
		var result = await Eval(expr);
		if (double.IsNaN(expected))
			double.IsNaN((double)result!).Should().BeTrue();
		else
			((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── Math with NULL ───
	[Theory]
	[InlineData("ABS(NULL)")]
	[InlineData("SQRT(NULL)")]
	[InlineData("ROUND(NULL)")]
	[InlineData("CEIL(NULL)")]
	[InlineData("FLOOR(NULL)")]
	[InlineData("SIGN(NULL)")]
	[InlineData("MOD(NULL, 3)")]
	[InlineData("MOD(10, NULL)")]
	[InlineData("POWER(NULL, 2)")]
	[InlineData("POWER(2, NULL)")]
	[InlineData("LN(NULL)")]
	[InlineData("LOG10(NULL)")]
	[InlineData("EXP(NULL)")]
	[InlineData("TRUNC(NULL)")]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Math_Null_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── Nested math ───
	[Theory]
	[InlineData("ABS(SIGN(-5))", 1L)]
	[InlineData("SQRT(POWER(3, 2))", 3.0)]
	[InlineData("ROUND(SQRT(2), 2)", 1.41)]
	[InlineData("CEIL(SQRT(2))", 2.0)]
	[InlineData("FLOOR(SQRT(2))", 1.0)]
	[InlineData("MOD(ABS(-10), 3)", 1L)]
	[InlineData("POWER(SQRT(4), 2)", 4.0)]
	[InlineData("LOG10(POWER(10, 3))", 3.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task NestedMath(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ─── Arithmetic operators ───
	[Theory]
	[InlineData("1 + 2", 3L)]
	[InlineData("10 - 3", 7L)]
	[InlineData("4 * 5", 20L)]
	// Division cases removed — INT64 / INT64 returns FLOAT64 per Spanner spec
	[InlineData("1 + 2 + 3", 6L)]
	[InlineData("10 - 3 - 2", 5L)]
	[InlineData("2 * 3 * 4", 24L)]
	[InlineData("2 + 3 * 4", 14L)]
	[InlineData("(2 + 3) * 4", 20L)]
	[InlineData("-5 + 3", -2L)]
	[InlineData("5 + -3", 2L)]
	[InlineData("0 * 100", 0L)]
	[InlineData("1 + 0", 1L)]
	[InlineData("0 + 0", 0L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task ArithmeticOperators(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Floating point arithmetic ───
	[Theory]
	[InlineData("1.0 + 2.0", 3.0)]
	[InlineData("10.5 - 3.2", 7.3)]
	[InlineData("2.5 * 4.0", 10.0)]
	[InlineData("10.0 / 3.0", 3.3333333333333335)]
	[InlineData("0.1 + 0.2", 0.30000000000000004)]
	[InlineData("1.0 / 3.0 * 3.0", 1.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task FloatArithmetic(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── Unary minus ───
	[Theory]
	[InlineData("-0", 0L)]
	[InlineData("-1", -1L)]
	[InlineData("-(-1)", 1L)]
	[InlineData("-(-(-1))", -1L)]
	[InlineData("-42", -42L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task UnaryMinus(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── SAFE math functions ───
	[Theory]
	[InlineData("SAFE_DIVIDE(10, 0)")]
	[InlineData("SAFE_DIVIDE(0, 0)")]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task SafeDivide_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 2)", 5.0)]
	[InlineData("SAFE_DIVIDE(10.0, 3.0)", 3.3333333333333335)]
	[InlineData("SAFE_DIVIDE(0, 5)", 0.0)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task SafeDivide_Valid(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── SAFE_NEGATE ───
	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task SafeNegate(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Comparison operators with numbers ───
	[Theory]
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("1 != 2", true)]
	[InlineData("1 != 1", false)]
	[InlineData("1 < 2", true)]
	[InlineData("2 < 1", false)]
	[InlineData("1 <= 1", true)]
	[InlineData("1 <= 2", true)]
	[InlineData("2 <= 1", false)]
	[InlineData("2 > 1", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 >= 1", true)]
	[InlineData("2 >= 1", true)]
	[InlineData("1 >= 2", false)]
	[InlineData("1 <> 2", true)]
	[InlineData("1 <> 1", false)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task ComparisonOperators(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── BETWEEN ───
	[Theory]
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	[InlineData("11 BETWEEN 1 AND 10", false)]
	[InlineData("-1 BETWEEN -5 AND 5", true)]
	[InlineData("5 NOT BETWEEN 1 AND 10", false)]
	[InlineData("0 NOT BETWEEN 1 AND 10", true)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task Between(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── IN operator ───
	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[Trait(TestTraits.Category, "MathExhaustive")]
	public async Task InOperator(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}
}
