using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for all math/numeric SQL functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MathFunctionExtendedIntegrationTests : IntegrationTestBase
{
	public MathFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ABS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#abs
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(100)", 100L)]
	[InlineData("ABS(-100)", 100L)]
	[InlineData("ABS(-999999)", 999999L)]
	[InlineData("ABS(999999)", 999999L)]
	public async Task Abs_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	[InlineData("ABS(-3.14)", 3.14)]
	[InlineData("ABS(3.14)", 3.14)]
	[InlineData("ABS(-0.001)", 0.001)]
	public async Task Abs_Float_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Abs_Null_ReturnsNull() => (await Eval("ABS(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// MOD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(10, 2)", 0L)]
	[InlineData("MOD(7, 4)", 3L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(100, 7)", 2L)]
	[InlineData("MOD(15, 5)", 0L)]
	[InlineData("MOD(1, 1)", 0L)]
	[InlineData("MOD(-10, 3)", -1L)]
	[InlineData("MOD(10, -3)", 1L)]
	[InlineData("MOD(-10, -3)", -1L)]
	public async Task Mod_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Mod_Null_ReturnsNull() => (await Eval("MOD(NULL, 3)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CEIL / CEILING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ceil
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CEIL(3.2)", 4.0)]
	[InlineData("CEIL(3.0)", 3.0)]
	[InlineData("CEIL(3.9)", 4.0)]
	[InlineData("CEIL(-3.2)", -3.0)]
	[InlineData("CEIL(-3.9)", -3.0)]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(0.1)", 1.0)]
	[InlineData("CEIL(-0.1)", 0.0)]
	[InlineData("CEIL(100.001)", 101.0)]
	[InlineData("CEILING(3.5)", 4.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(-1.0)", -1.0)]
	public async Task Ceil_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Ceil_Null_ReturnsNull() => (await Eval("CEIL(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FLOOR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#floor
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("FLOOR(3.8)", 3.0)]
	[InlineData("FLOOR(3.0)", 3.0)]
	[InlineData("FLOOR(3.1)", 3.0)]
	[InlineData("FLOOR(-3.2)", -4.0)]
	[InlineData("FLOOR(-3.9)", -4.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(0.9)", 0.0)]
	[InlineData("FLOOR(-0.1)", -1.0)]
	[InlineData("FLOOR(100.999)", 100.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(-1.0)", -1.0)]
	[InlineData("FLOOR(99.999)", 99.0)]
	public async Task Floor_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Floor_Null_ReturnsNull() => (await Eval("FLOOR(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ROUND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ROUND(3.456, 2)", 3.46)]
	[InlineData("ROUND(3.454, 2)", 3.45)]
	[InlineData("ROUND(3.5)", 4.0)]
	[InlineData("ROUND(2.5)", 3.0)]
	[InlineData("ROUND(3.0)", 3.0)]
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(-3.5)", -4.0)]
	[InlineData("ROUND(3.14159, 3)", 3.142)]
	[InlineData("ROUND(3.14159, 4)", 3.1416)]
	[InlineData("ROUND(3.14159, 0)", 3.0)]
	[InlineData("ROUND(100.5)", 101.0)]
	[InlineData("ROUND(-2.5)", -3.0)]
	[InlineData("ROUND(1.005, 2)", 1.0)]
	[InlineData("ROUND(9.999, 2)", 10.0)]
	public async Task Round_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Round_Null_ReturnsNull() => (await Eval("ROUND(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TRUNC(3.456, 1)", 3.4)]
	[InlineData("TRUNC(3.456, 2)", 3.45)]
	[InlineData("TRUNC(3.999, 0)", 3.0)]
	[InlineData("TRUNC(-3.456, 1)", -3.4)]
	[InlineData("TRUNC(-3.999, 0)", -3.0)]
	[InlineData("TRUNC(3.14159, 3)", 3.141)]
	[InlineData("TRUNC(3.14159, 4)", 3.1415)]
	[InlineData("TRUNC(0.0, 2)", 0.0)]
	[InlineData("TRUNC(9.99, 1)", 9.9)]
	public async Task Trunc_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Trunc_Null_ReturnsNull() => (await Eval("TRUNC(NULL, 1)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SIGN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("SIGN(1)", 1L)]
	[InlineData("SIGN(-1)", -1L)]
	[InlineData("SIGN(100)", 1L)]
	[InlineData("SIGN(-100)", -1L)]
	[InlineData("SIGN(999999)", 1L)]
	[InlineData("SIGN(-999999)", -1L)]
	public async Task Sign_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SIGN(0.0)", 0.0)]
	[InlineData("SIGN(1.5)", 1.0)]
	[InlineData("SIGN(-1.5)", -1.0)]
	[InlineData("SIGN(0.001)", 1.0)]
	[InlineData("SIGN(-0.001)", -1.0)]
	public async Task Sign_Float_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Sign_Null_ReturnsNull() => (await Eval("SIGN(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// GREATEST / LEAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("GREATEST(-1, -2, -3)", -1L)]
	[InlineData("GREATEST(0, 0)", 0L)]
	[InlineData("GREATEST(100, 50, 75)", 100L)]
	[InlineData("GREATEST(1, 5, 3, 7, 2)", 7L)]
	[InlineData("GREATEST(-10, 0, 10)", 10L)]
	public async Task Greatest_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("GREATEST(1.5, 2.5, 3.5)", 3.5)]
	[InlineData("GREATEST(-1.5, -2.5)", -1.5)]
	[InlineData("GREATEST(0.0, 0.0)", 0.0)]
	public async Task Greatest_Float_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(1, 1, 1)", 1L)]
	[InlineData("LEAST(-1, -2, -3)", -3L)]
	[InlineData("LEAST(0, 0)", 0L)]
	[InlineData("LEAST(100, 50, 75)", 50L)]
	[InlineData("LEAST(1, 5, 3, 7, 2)", 1L)]
	[InlineData("LEAST(-10, 0, 10)", -10L)]
	public async Task Least_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("LEAST(1.5, 2.5, 3.5)", 1.5)]
	[InlineData("LEAST(-1.5, -2.5)", -2.5)]
	[InlineData("LEAST(0.0, 0.0)", 0.0)]
	public async Task Least_Float_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DIV (integer division)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(9, 3)", 3L)]
	[InlineData("DIV(10, 10)", 1L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(100, 7)", 14L)]
	[InlineData("DIV(15, 4)", 3L)]
	[InlineData("DIV(1, 1)", 1L)]
	[InlineData("DIV(-10, 3)", -3L)]
	[InlineData("DIV(10, -3)", -3L)]
	[InlineData("DIV(99, 10)", 9L)]
	public async Task Div_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Div_Null_ReturnsNull() => (await Eval("DIV(NULL, 3)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IEEE_DIVIDE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 2.0)", 5.0)]
	[InlineData("IEEE_DIVIDE(7.0, 2.0)", 3.5)]
	[InlineData("IEEE_DIVIDE(0.0, 1.0)", 0.0)]
	[InlineData("IEEE_DIVIDE(100.0, 4.0)", 25.0)]
	[InlineData("IEEE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	public async Task IeeeDivide_ReturnsExpected(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	public async Task IeeeDivide_ByZero_ReturnsInfinity()
		=> (await Eval("IEEE_DIVIDE(1.0, 0.0)")).Should().Be(double.PositiveInfinity);

	[Fact]
	public async Task IeeeDivide_NegativeByZero_ReturnsNegInfinity()
		=> (await Eval("IEEE_DIVIDE(-1.0, 0.0)")).Should().Be(double.NegativeInfinity);

	[Fact]
	public async Task IeeeDivide_ZeroByZero_ReturnsNaN()
		=> double.IsNaN((double)(await Eval("IEEE_DIVIDE(0.0, 0.0)"))!).Should().BeTrue();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE_DIVIDE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task SafeDivide_ByZero_ReturnsNull()
		=> (await Eval("SAFE_DIVIDE(1.0, 0.0)")).Should().BeNull();

	[Fact]
	public async Task SafeDivide_Normal_ReturnsResult()
		=> (await Eval("SAFE_DIVIDE(10.0, 2.0)")).Should().Be(5.0);

	[Fact]
	public async Task SafeDivide_NullNumerator_ReturnsNull()
		=> (await Eval("SAFE_DIVIDE(NULL, 2.0)")).Should().BeNull();

	[Fact]
	public async Task SafeDivide_NullDenominator_ReturnsNull()
		=> (await Eval("SAFE_DIVIDE(1.0, NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE_NEGATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_negate
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	[InlineData("SAFE_NEGATE(100)", -100L)]
	public async Task SafeNegate_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeNegate_Null_ReturnsNull()
		=> (await Eval("SAFE_NEGATE(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE_ADD / SAFE_SUBTRACT / SAFE_MULTIPLY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_add
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_ADD(1, 2)", 3L)]
	[InlineData("SAFE_ADD(0, 0)", 0L)]
	[InlineData("SAFE_ADD(-1, 1)", 0L)]
	[InlineData("SAFE_ADD(100, 200)", 300L)]
	public async Task SafeAdd_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeAdd_Null_ReturnsNull()
		=> (await Eval("SAFE_ADD(NULL, 1)")).Should().BeNull();

	[Theory]
	[InlineData("SAFE_SUBTRACT(5, 3)", 2L)]
	[InlineData("SAFE_SUBTRACT(0, 0)", 0L)]
	[InlineData("SAFE_SUBTRACT(1, 1)", 0L)]
	[InlineData("SAFE_SUBTRACT(100, 50)", 50L)]
	public async Task SafeSubtract_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeSubtract_Null_ReturnsNull()
		=> (await Eval("SAFE_SUBTRACT(NULL, 1)")).Should().BeNull();

	[Theory]
	[InlineData("SAFE_MULTIPLY(3, 4)", 12L)]
	[InlineData("SAFE_MULTIPLY(0, 100)", 0L)]
	[InlineData("SAFE_MULTIPLY(-2, 3)", -6L)]
	[InlineData("SAFE_MULTIPLY(10, 10)", 100L)]
	public async Task SafeMultiply_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SafeMultiply_Null_ReturnsNull()
		=> (await Eval("SAFE_MULTIPLY(NULL, 1)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SQRT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sqrt
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SQRT(0.0)", 0.0)]
	[InlineData("SQRT(1.0)", 1.0)]
	[InlineData("SQRT(4.0)", 2.0)]
	[InlineData("SQRT(9.0)", 3.0)]
	[InlineData("SQRT(16.0)", 4.0)]
	[InlineData("SQRT(25.0)", 5.0)]
	[InlineData("SQRT(100.0)", 10.0)]
	[InlineData("SQRT(0.25)", 0.5)]
	public async Task Sqrt_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Sqrt_2_ReturnsApprox()
	{
		var result = (double)(await Eval("SQRT(2.0)"))!;
		result.Should().BeApproximately(1.4142135623730951, 1e-10);
	}

	[Fact]
	public async Task Sqrt_Null_ReturnsNull() => (await Eval("SQRT(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// POW / POWER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#pow
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("POW(2.0, 0.0)", 1.0)]
	[InlineData("POW(2.0, 1.0)", 2.0)]
	[InlineData("POW(2.0, 2.0)", 4.0)]
	[InlineData("POW(2.0, 3.0)", 8.0)]
	[InlineData("POW(2.0, 10.0)", 1024.0)]
	[InlineData("POW(3.0, 2.0)", 9.0)]
	[InlineData("POW(3.0, 3.0)", 27.0)]
	[InlineData("POW(10.0, 0.0)", 1.0)]
	[InlineData("POW(10.0, 1.0)", 10.0)]
	[InlineData("POW(10.0, 2.0)", 100.0)]
	[InlineData("POWER(2.0, 4.0)", 16.0)]
	[InlineData("POW(1.0, 100.0)", 1.0)]
	[InlineData("POW(0.0, 5.0)", 0.0)]
	[InlineData("POW(5.0, 0.0)", 1.0)]
	public async Task Pow_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Pow_Null_ReturnsNull() => (await Eval("POW(NULL, 2.0)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// EXP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#exp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Exp_0_Returns1() => (await Eval("EXP(0.0)")).Should().Be(1.0);

	[Fact]
	public async Task Exp_1_ReturnsE()
	{
		var result = (double)(await Eval("EXP(1.0)"))!;
		result.Should().BeApproximately(Math.E, 1e-10);
	}

	[Fact]
	public async Task Exp_2_ReturnsESquared()
	{
		var result = (double)(await Eval("EXP(2.0)"))!;
		result.Should().BeApproximately(Math.E * Math.E, 1e-10);
	}

	[Fact]
	public async Task Exp_Null_ReturnsNull() => (await Eval("EXP(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ln
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Ln_1_Returns0() => (await Eval("LN(1.0)")).Should().Be(0.0);

	[Fact]
	public async Task Ln_E_Returns1()
	{
		var result = (double)(await Eval("LN(2.718281828459045)"))!;
		result.Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	public async Task Ln_2_ReturnsApprox()
	{
		var result = (double)(await Eval("LN(2.0)"))!;
		result.Should().BeApproximately(0.6931471805599453, 1e-10);
	}

	[Fact]
	public async Task Ln_Null_ReturnsNull() => (await Eval("LN(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LOG
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#log
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LOG(100.0, 10.0)", 2.0)]
	[InlineData("LOG(8.0, 2.0)", 3.0)]
	[InlineData("LOG(1.0, 10.0)", 0.0)]
	public async Task Log_ReturnsExpected(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LOG10
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#log10
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LOG10(1.0)", 0.0)]
	[InlineData("LOG10(10.0)", 1.0)]
	[InlineData("LOG10(100.0)", 2.0)]
	[InlineData("LOG10(1000.0)", 3.0)]
	[InlineData("LOG10(10000.0)", 4.0)]
	public async Task Log10_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Log10_Null_ReturnsNull() => (await Eval("LOG10(NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IS_NAN / IS_INF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#is_nan
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[InlineData("IS_NAN(IEEE_DIVIDE(1.0, 0.0))", false)]
	public async Task IsNan_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(IEEE_DIVIDE(-1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[InlineData("IS_INF(0.0)", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(0.0, 0.0))", false)]
	public async Task IsInf_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Arithmetic operators: +, -, *, /, %
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("1 + 1", 2L)]
	[InlineData("0 + 0", 0L)]
	[InlineData("100 + 200", 300L)]
	[InlineData("-5 + 5", 0L)]
	[InlineData("-5 + -5", -10L)]
	[InlineData("1 + 2 + 3", 6L)]
	[InlineData("1 + 2 + 3 + 4", 10L)]
	public async Task Add_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("1.5 + 2.5", 4.0)]
	[InlineData("0.1 + 0.2", 0.30000000000000004)]
	[InlineData("-1.5 + 1.5", 0.0)]
	public async Task Add_Float_ReturnsExpected(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("5 - 3", 2L)]
	[InlineData("0 - 0", 0L)]
	[InlineData("100 - 200", -100L)]
	[InlineData("-5 - -5", 0L)]
	[InlineData("10 - 3 - 2", 5L)]
	public async Task Subtract_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("3 * 4", 12L)]
	[InlineData("0 * 100", 0L)]
	[InlineData("-2 * 3", -6L)]
	[InlineData("-2 * -3", 6L)]
	[InlineData("10 * 10", 100L)]
	[InlineData("1 * 1", 1L)]
	public async Task Multiply_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("10.0 / 2.0", 5.0)]
	[InlineData("7.0 / 2.0", 3.5)]
	[InlineData("1.0 / 3.0", 0.3333333333333333)]
	[InlineData("100.0 / 4.0", 25.0)]
	public async Task Divide_Float_ReturnsExpected(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Unary minus
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("-1", -1L)]
	[InlineData("-0", 0L)]
	[InlineData("-100", -100L)]
	[InlineData("-(5)", -5L)]
	[InlineData("-(1 + 2)", -3L)]
	public async Task UnaryMinus_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Comparison operators
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("0 = 0", true)]
	[InlineData("-1 = -1", true)]
	[InlineData("'a' = 'a'", true)]
	[InlineData("'a' = 'b'", false)]
	[InlineData("true = true", true)]
	[InlineData("true = false", false)]
	public async Task Equal_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("1 != 2", true)]
	[InlineData("1 != 1", false)]
	[InlineData("1 <> 2", true)]
	[InlineData("1 <> 1", false)]
	[InlineData("'a' != 'b'", true)]
	[InlineData("'a' != 'a'", false)]
	public async Task NotEqual_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("1 < 2", true)]
	[InlineData("2 < 1", false)]
	[InlineData("1 < 1", false)]
	[InlineData("1 <= 2", true)]
	[InlineData("1 <= 1", true)]
	[InlineData("2 <= 1", false)]
	[InlineData("2 > 1", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 > 1", false)]
	[InlineData("2 >= 1", true)]
	[InlineData("1 >= 1", true)]
	[InlineData("1 >= 2", false)]
	public async Task Comparison_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' < 'a'", false)]
	[InlineData("'a' <= 'a'", true)]
	[InlineData("'b' > 'a'", true)]
	[InlineData("'a' >= 'a'", true)]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'abc' < 'abc'", false)]
	public async Task StringComparison_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IS NULL / IS NOT NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_null
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("NULL IS NULL", true)]
	[InlineData("1 IS NULL", false)]
	[InlineData("'' IS NULL", false)]
	[InlineData("0 IS NULL", false)]
	[InlineData("false IS NULL", false)]
	[InlineData("NULL IS NOT NULL", false)]
	[InlineData("1 IS NOT NULL", true)]
	[InlineData("'' IS NOT NULL", true)]
	[InlineData("0 IS NOT NULL", true)]
	[InlineData("false IS NOT NULL", true)]
	public async Task IsNull_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// BETWEEN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("11 BETWEEN 1 AND 10", false)]
	[InlineData("-5 BETWEEN -10 AND 0", true)]
	[InlineData("5 NOT BETWEEN 1 AND 10", false)]
	[InlineData("0 NOT BETWEEN 1 AND 10", true)]
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	public async Task Between_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("1 IN (1)", true)]
	[InlineData("0 IN (1, 2, 3)", false)]
	[InlineData("true IN (true, false)", true)]
	[InlineData("3 IN (1, 2, 3, 4, 5)", true)]
	public async Task In_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Boolean operators: AND, OR, NOT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("true AND true", true)]
	[InlineData("true AND false", false)]
	[InlineData("false AND true", false)]
	[InlineData("false AND false", false)]
	[InlineData("true OR true", true)]
	[InlineData("true OR false", true)]
	[InlineData("false OR true", true)]
	[InlineData("false OR false", false)]
	[InlineData("NOT true", false)]
	[InlineData("NOT false", true)]
	[InlineData("NOT (1 = 2)", true)]
	[InlineData("NOT (1 = 1)", false)]
	[InlineData("true AND true AND true", true)]
	[InlineData("false OR false OR true", true)]
	[InlineData("(1 = 1) AND (2 = 2)", true)]
	[InlineData("(1 = 2) OR (2 = 2)", true)]
	[InlineData("(1 = 2) AND (2 = 2)", false)]
	public async Task BoolOperator_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Null coalesce operator ??
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("COALESCE(NULL, 1)", 1L)]
	[InlineData("COALESCE(NULL, NULL, 2)", 2L)]
	[InlineData("COALESCE(3, NULL)", 3L)]
	[InlineData("COALESCE(NULL, NULL, NULL, 4)", 4L)]
	[InlineData("COALESCE(1, 2)", 1L)]
	public async Task Coalesce_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("COALESCE(NULL, 'a')", "a")]
	[InlineData("COALESCE(NULL, NULL, 'b')", "b")]
	[InlineData("COALESCE('x', NULL)", "x")]
	public async Task Coalesce_String_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Coalesce_AllNull_ReturnsNull()
		=> (await Eval("COALESCE(NULL, NULL, NULL)")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IF(true, 1, 2)", 1L)]
	[InlineData("IF(false, 1, 2)", 2L)]
	[InlineData("IF(1 = 1, 10, 20)", 10L)]
	[InlineData("IF(1 = 2, 10, 20)", 20L)]
	[InlineData("IF(true, 0, -1)", 0L)]
	public async Task If_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("IF(true, 'yes', 'no')", "yes")]
	[InlineData("IF(false, 'yes', 'no')", "no")]
	[InlineData("IF(1 > 0, 'positive', 'non-positive')", "positive")]
	[InlineData("IF(1 < 0, 'negative', 'non-negative')", "non-negative")]
	public async Task If_String_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IFNULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IFNULL(NULL, 1)", 1L)]
	[InlineData("IFNULL(5, 1)", 5L)]
	[InlineData("IFNULL(0, 1)", 0L)]
	public async Task Ifnull_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("IFNULL(NULL, 'default')", "default")]
	[InlineData("IFNULL('value', 'default')", "value")]
	[InlineData("IFNULL('', 'default')", "")]
	public async Task Ifnull_String_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULLIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF(0, 1)", 0L)]
	[InlineData("NULLIF(5, 3)", 5L)]
	public async Task Nullif_NotEqual_ReturnsFirst(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF(0, 0)")]
	[InlineData("NULLIF(-1, -1)")]
	public async Task Nullif_Equal_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("NULLIF('a', 'b')", "a")]
	[InlineData("NULLIF('hello', 'world')", "hello")]
	public async Task Nullif_String_NotEqual_ReturnsFirst(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("NULLIF('a', 'a')")]
	[InlineData("NULLIF('hello', 'hello')")]
	public async Task Nullif_String_Equal_ReturnsNull(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CASE expression
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CASE WHEN true THEN 1 ELSE 2 END", 1L)]
	[InlineData("CASE WHEN false THEN 1 ELSE 2 END", 2L)]
	[InlineData("CASE WHEN 1 = 1 THEN 10 ELSE 20 END", 10L)]
	[InlineData("CASE WHEN 1 = 2 THEN 10 ELSE 20 END", 20L)]
	[InlineData("CASE WHEN 1 > 0 THEN 100 WHEN 1 = 0 THEN 0 ELSE -100 END", 100L)]
	[InlineData("CASE WHEN false THEN 1 WHEN true THEN 2 ELSE 3 END", 2L)]
	[InlineData("CASE WHEN false THEN 1 WHEN false THEN 2 ELSE 3 END", 3L)]
	public async Task Case_Searched_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CASE WHEN true THEN 'yes' ELSE 'no' END", "yes")]
	[InlineData("CASE WHEN false THEN 'yes' ELSE 'no' END", "no")]
	[InlineData("CASE WHEN 1 = 1 THEN 'match' ELSE 'no match' END", "match")]
	public async Task Case_Searched_String_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "other")]
	public async Task Case_Simple_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Case_NoMatch_NoElse_ReturnsNull()
		=> (await Eval("CASE WHEN false THEN 1 END")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Integer literals
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("0", 0L)]
	[InlineData("1", 1L)]
	[InlineData("-1", -1L)]
	[InlineData("42", 42L)]
	[InlineData("100", 100L)]
	[InlineData("999999", 999999L)]
	[InlineData("-999999", -999999L)]
	[InlineData("9223372036854775807", 9223372036854775807L)]
	public async Task IntLiteral_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Float literals
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("0.0", 0.0)]
	[InlineData("1.0", 1.0)]
	[InlineData("-1.0", -1.0)]
	[InlineData("3.14", 3.14)]
	[InlineData("0.5", 0.5)]
	[InlineData("-0.5", -0.5)]
	[InlineData("100.0", 100.0)]
	[InlineData("0.001", 0.001)]
	public async Task FloatLiteral_ReturnsExpected(string expr, double expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// String literals
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("'hello'", "hello")]
	[InlineData("''", "")]
	[InlineData("'a'", "a")]
	[InlineData("'hello world'", "hello world")]
	[InlineData("'123'", "123")]
	[InlineData("'  spaces  '", "  spaces  ")]
	public async Task StringLiteral_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Boolean literals
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("true", true)]
	[InlineData("false", false)]
	[InlineData("TRUE", true)]
	[InlineData("FALSE", false)]
	public async Task BoolLiteral_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL literal
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task NullLiteral_ReturnsNull()
		=> (await Eval("NULL")).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Complex arithmetic expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("(1 + 2) * 3", 9L)]
	[InlineData("1 + 2 * 3", 7L)]
	[InlineData("(10 - 5) * 2", 10L)]
	[InlineData("10 - 5 * 2", 0L)]
	[InlineData("(4 + 6) * (3 - 1)", 20L)]
	[InlineData("100 - 50 - 25", 25L)]
	[InlineData("2 * 3 + 4 * 5", 26L)]
	[InlineData("(2 + 3) * (4 + 5)", 45L)]
	public async Task ComplexArithmetic_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL propagation for all math functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(NULL)")]
	[InlineData("MOD(NULL, 3)")]
	[InlineData("MOD(3, NULL)")]
	[InlineData("CEIL(NULL)")]
	[InlineData("FLOOR(NULL)")]
	[InlineData("ROUND(NULL)")]
	[InlineData("TRUNC(NULL, 1)")]
	[InlineData("SIGN(NULL)")]
	[InlineData("DIV(NULL, 3)")]
	[InlineData("DIV(3, NULL)")]
	[InlineData("SQRT(NULL)")]
	[InlineData("POW(NULL, 2.0)")]
	[InlineData("POW(2.0, NULL)")]
	[InlineData("EXP(NULL)")]
	[InlineData("LN(NULL)")]
	[InlineData("LOG10(NULL)")]
	[InlineData("SAFE_DIVIDE(NULL, 1.0)")]
	[InlineData("SAFE_DIVIDE(1.0, NULL)")]
	[InlineData("SAFE_NEGATE(NULL)")]
	[InlineData("SAFE_ADD(NULL, 1)")]
	[InlineData("SAFE_ADD(1, NULL)")]
	[InlineData("SAFE_SUBTRACT(NULL, 1)")]
	[InlineData("SAFE_SUBTRACT(1, NULL)")]
	[InlineData("SAFE_MULTIPLY(NULL, 1)")]
	[InlineData("SAFE_MULTIPLY(1, NULL)")]
	public async Task MathFunction_NullPropagation(string expr)
		=> (await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Nested math functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(SIGN(-5))", 1L)]
	[InlineData("ABS(-ABS(-5))", 5L)]
	[InlineData("MOD(ABS(-10), 3)", 1L)]
	[InlineData("SIGN(ABS(-1))", 1L)]
	public async Task NestedMathFunctions_Int_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CEIL(FLOOR(3.5))", 3.0)]
	[InlineData("FLOOR(CEIL(3.5))", 4.0)]
	[InlineData("SQRT(POW(3.0, 2.0))", 3.0)]
	[InlineData("ROUND(SQRT(2.0), 2)", 1.41)]
	[InlineData("ABS(FLOOR(-3.7))", 4.0)]
	[InlineData("CEIL(SQRT(2.0))", 2.0)]
	[InlineData("FLOOR(SQRT(2.0))", 1.0)]
	public async Task NestedMathFunctions_Float_ReturnsExpected(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

}
