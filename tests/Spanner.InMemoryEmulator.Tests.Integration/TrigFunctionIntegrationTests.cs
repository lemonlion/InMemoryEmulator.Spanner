using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for trigonometric and hyperbolic SQL functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TrigFunctionIntegrationTests : IntegrationTestBase
{
	public TrigFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// SIN — sine function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sin
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SIN(0)", 0.0)]
	[InlineData("SIN(1)", 0.8414709848078965)]
	[InlineData("SIN(-1)", -0.8414709848078965)]
	[InlineData("SIN(0.5)", 0.479425538604203)]
	[InlineData("SIN(-0.5)", -0.479425538604203)]
	[InlineData("SIN(2)", 0.9092974268256817)]
	[InlineData("SIN(-2)", -0.9092974268256817)]
	[InlineData("SIN(3)", 0.1411200080598672)]
	[InlineData("SIN(10)", -0.5440211108893698)]
	[InlineData("SIN(100)", -0.5063656411097588)]
	[InlineData("SIN(0.001)", 0.0009999998333334)]
	[InlineData("SIN(0.0001)", 0.0000999999999983)]
	public async Task Sin_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Sin_Pi_IsZero()
	{
		// Ref: SIN(π) ≈ 0 (not exactly 0 due to floating point)
		var result = (double)(await Eval("SIN(ACOS(-1))"))!;
		result.Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	public async Task Sin_PiOver2_IsOne()
	{
		var result = (double)(await Eval("SIN(ACOS(-1) / 2)"))!;
		result.Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	public async Task Sin_Null_IsNull()
	{
		var result = await Eval("SIN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Sin_NaN_IsNaN()
	{
		var result = (double)(await Eval("SIN(CAST('nan' AS FLOAT64))"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	[Fact]
	public async Task Sin_Inf_IsNaN()
	{
		var result = (double)(await Eval("SIN(CAST('inf' AS FLOAT64))"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// COS — cosine function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cos
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COS(0)", 1.0)]
	[InlineData("COS(1)", 0.5403023058681398)]
	[InlineData("COS(-1)", 0.5403023058681398)]
	[InlineData("COS(0.5)", 0.8775825618903728)]
	[InlineData("COS(-0.5)", 0.8775825618903728)]
	[InlineData("COS(2)", -0.4161468365471424)]
	[InlineData("COS(-2)", -0.4161468365471424)]
	[InlineData("COS(3)", -0.9899924966004454)]
	[InlineData("COS(10)", -0.8390715290764524)]
	[InlineData("COS(100)", 0.8623188722876839)]
	[InlineData("COS(0.001)", 0.9999995000000417)]
	public async Task Cos_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Cos_Pi_IsMinusOne()
	{
		var result = (double)(await Eval("COS(ACOS(-1))"))!;
		result.Should().BeApproximately(-1.0, 1e-10);
	}

	[Fact]
	public async Task Cos_PiOver2_IsZero()
	{
		var result = (double)(await Eval("COS(ACOS(-1) / 2)"))!;
		result.Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	public async Task Cos_Null_IsNull()
	{
		var result = await Eval("COS(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Cos_NaN_IsNaN()
	{
		var result = (double)(await Eval("COS(CAST('nan' AS FLOAT64))"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// TAN — tangent function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#tan
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TAN(0)", 0.0)]
	[InlineData("TAN(1)", 1.5574077246549023)]
	[InlineData("TAN(-1)", -1.5574077246549023)]
	[InlineData("TAN(0.5)", 0.5463024898437905)]
	[InlineData("TAN(-0.5)", -0.5463024898437905)]
	[InlineData("TAN(0.25)", 0.25534192122103627)]
	[InlineData("TAN(0.001)", 0.0010000003333334)]
	public async Task Tan_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Tan_Null_IsNull()
	{
		var result = await Eval("TAN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Tan_NaN_IsNaN()
	{
		var result = (double)(await Eval("TAN(CAST('nan' AS FLOAT64))"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// ASIN — arcsine function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#asin
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ASIN(0)", 0.0)]
	[InlineData("ASIN(1)", 1.5707963267948966)]
	[InlineData("ASIN(-1)", -1.5707963267948966)]
	[InlineData("ASIN(0.5)", 0.5235987755982989)]
	[InlineData("ASIN(-0.5)", -0.5235987755982989)]
	[InlineData("ASIN(0.1)", 0.1001674211615598)]
	[InlineData("ASIN(-0.1)", -0.1001674211615598)]
	[InlineData("ASIN(0.99)", 1.42925685347047)]
	[InlineData("ASIN(0.001)", 0.001000000166667)]
	public async Task Asin_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Asin_Null_IsNull()
	{
		var result = await Eval("ASIN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Asin_OutOfDomain_ReturnsNaN()
	{
		// Ref: ASIN with |x| > 1 returns NaN
		var result = (double)(await Eval("ASIN(2.0)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	[Fact]
	public async Task Asin_NegOutOfDomain_ReturnsNaN()
	{
		var result = (double)(await Eval("ASIN(-1.5)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// ACOS — arccosine function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#acos
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ACOS(1)", 0.0)]
	[InlineData("ACOS(-1)", 3.141592653589793)]
	[InlineData("ACOS(0)", 1.5707963267948966)]
	[InlineData("ACOS(0.5)", 1.0471975511965979)]
	[InlineData("ACOS(-0.5)", 2.0943951023931957)]
	[InlineData("ACOS(0.1)", 1.4706289056333368)]
	[InlineData("ACOS(-0.1)", 1.6709637479564563)]
	[InlineData("ACOS(0.99)", 0.141539473324427)]
	public async Task Acos_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Acos_Null_IsNull()
	{
		var result = await Eval("ACOS(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Acos_OutOfDomain_ReturnsNaN()
	{
		var result = (double)(await Eval("ACOS(2.0)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// ATAN — arctangent function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#atan
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ATAN(0)", 0.0)]
	[InlineData("ATAN(1)", 0.7853981633974483)]
	[InlineData("ATAN(-1)", -0.7853981633974483)]
	[InlineData("ATAN(0.5)", 0.4636476090008061)]
	[InlineData("ATAN(-0.5)", -0.4636476090008061)]
	[InlineData("ATAN(10)", 1.4711276743037347)]
	[InlineData("ATAN(100)", 1.5607966601082315)]
	[InlineData("ATAN(1000)", 1.5697963271282298)]
	[InlineData("ATAN(0.001)", 0.0009999996666667)]
	public async Task Atan_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Atan_Null_IsNull()
	{
		var result = await Eval("ATAN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Atan_Inf_IsPiOver2()
	{
		var result = (double)(await Eval("ATAN(CAST('inf' AS FLOAT64))"))!;
		result.Should().BeApproximately(Math.PI / 2, 1e-10);
	}

	[Fact]
	public async Task Atan_NegInf_IsMinusPiOver2()
	{
		var result = (double)(await Eval("ATAN(CAST('-inf' AS FLOAT64))"))!;
		result.Should().BeApproximately(-Math.PI / 2, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// ATAN2 — two-argument arctangent
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#atan2
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ATAN2(0, 1)", 0.0)]
	[InlineData("ATAN2(1, 0)", 1.5707963267948966)]
	[InlineData("ATAN2(0, -1)", 3.141592653589793)]
	[InlineData("ATAN2(-1, 0)", -1.5707963267948966)]
	[InlineData("ATAN2(1, 1)", 0.7853981633974483)]
	[InlineData("ATAN2(-1, -1)", -2.356194490192345)]
	[InlineData("ATAN2(1, -1)", 2.356194490192345)]
	[InlineData("ATAN2(-1, 1)", -0.7853981633974483)]
	[InlineData("ATAN2(3, 4)", 0.6435011087932844)]
	[InlineData("ATAN2(0.5, 0.5)", 0.7853981633974483)]
	public async Task Atan2_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Atan2_BothZero()
	{
		var result = (double)(await Eval("ATAN2(0, 0)"))!;
		result.Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	public async Task Atan2_Null_IsNull()
	{
		var result = await Eval("ATAN2(CAST(NULL AS FLOAT64), 1)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Atan2_SecondArgNull_IsNull()
	{
		var result = await Eval("ATAN2(1, CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// SINH — hyperbolic sine
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sinh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SINH(0)", 0.0)]
	[InlineData("SINH(1)", 1.1752011936438014)]
	[InlineData("SINH(-1)", -1.1752011936438014)]
	[InlineData("SINH(0.5)", 0.5210953054937474)]
	[InlineData("SINH(-0.5)", -0.5210953054937474)]
	[InlineData("SINH(2)", 3.6268604078470186)]
	[InlineData("SINH(-2)", -3.6268604078470186)]
	[InlineData("SINH(3)", 10.017874927409903)]
	[InlineData("SINH(0.001)", 0.0010000001666667)]
	public async Task Sinh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Sinh_Null_IsNull()
	{
		var result = await Eval("SINH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Sinh_Inf_IsInf()
	{
		var result = (double)(await Eval("SINH(CAST('inf' AS FLOAT64))"))!;
		double.IsPositiveInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task Sinh_NegInf_IsNegInf()
	{
		var result = (double)(await Eval("SINH(CAST('-inf' AS FLOAT64))"))!;
		double.IsNegativeInfinity(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// COSH — hyperbolic cosine
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COSH(0)", 1.0)]
	[InlineData("COSH(1)", 1.5430806348152437)]
	[InlineData("COSH(-1)", 1.5430806348152437)]
	[InlineData("COSH(0.5)", 1.1276259652063807)]
	[InlineData("COSH(-0.5)", 1.1276259652063807)]
	[InlineData("COSH(2)", 3.7621956910836314)]
	[InlineData("COSH(3)", 10.067661995777765)]
	[InlineData("COSH(0.001)", 1.0000005)]
	public async Task Cosh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-8);

	[Fact]
	public async Task Cosh_Null_IsNull()
	{
		var result = await Eval("COSH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Cosh_Symmetry()
	{
		// COSH is an even function: COSH(x) = COSH(-x)
		var pos = (double)(await Eval("COSH(1.5)"))!;
		var neg = (double)(await Eval("COSH(-1.5)"))!;
		pos.Should().BeApproximately(neg, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// TANH — hyperbolic tangent
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#tanh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TANH(0)", 0.0)]
	[InlineData("TANH(1)", 0.7615941559557649)]
	[InlineData("TANH(-1)", -0.7615941559557649)]
	[InlineData("TANH(0.5)", 0.46211715726000974)]
	[InlineData("TANH(-0.5)", -0.46211715726000974)]
	[InlineData("TANH(2)", 0.9640275800758169)]
	[InlineData("TANH(10)", 0.9999999958776927)]
	[InlineData("TANH(100)", 1.0)]
	public async Task Tanh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Tanh_Null_IsNull()
	{
		var result = await Eval("TANH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Tanh_Inf_IsOne()
	{
		var result = (double)(await Eval("TANH(CAST('inf' AS FLOAT64))"))!;
		result.Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	public async Task Tanh_NegInf_IsMinusOne()
	{
		var result = (double)(await Eval("TANH(CAST('-inf' AS FLOAT64))"))!;
		result.Should().BeApproximately(-1.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// ASINH — inverse hyperbolic sine
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#asinh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ASINH(0)", 0.0)]
	[InlineData("ASINH(1)", 0.881373587019543)]
	[InlineData("ASINH(-1)", -0.881373587019543)]
	[InlineData("ASINH(0.5)", 0.481211825059603)]
	[InlineData("ASINH(-0.5)", -0.481211825059603)]
	[InlineData("ASINH(2)", 1.44363547517881)]
	[InlineData("ASINH(10)", 2.99822295029797)]
	[InlineData("ASINH(100)", 5.29834236561059)]
	public async Task Asinh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Asinh_Null_IsNull()
	{
		var result = await Eval("ASINH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ACOSH — inverse hyperbolic cosine
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#acosh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ACOSH(1)", 0.0)]
	[InlineData("ACOSH(2)", 1.3169578969248166)]
	[InlineData("ACOSH(10)", 2.993222846126381)]
	[InlineData("ACOSH(1.5)", 0.9624236501192069)]
	[InlineData("ACOSH(100)", 5.298292365610484)]
	public async Task Acosh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Acosh_Null_IsNull()
	{
		var result = await Eval("ACOSH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Acosh_LessThanOne_ReturnsNaN()
	{
		// Ref: ACOSH(x) where x < 1 is outside the domain
		var result = (double)(await Eval("ACOSH(0.5)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// ATANH — inverse hyperbolic tangent
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#atanh
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ATANH(0)", 0.0)]
	[InlineData("ATANH(0.5)", 0.5493061443340549)]
	[InlineData("ATANH(-0.5)", -0.5493061443340549)]
	[InlineData("ATANH(0.1)", 0.10033534773107558)]
	[InlineData("ATANH(-0.1)", -0.10033534773107558)]
	[InlineData("ATANH(0.9)", 1.4722194895832204)]
	[InlineData("ATANH(0.99)", 2.6466524123622457)]
	public async Task Atanh_BasicValues(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Fact]
	public async Task Atanh_One_IsInf()
	{
		var result = (double)(await Eval("ATANH(1.0)"))!;
		double.IsPositiveInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task Atanh_MinusOne_IsNegInf()
	{
		var result = (double)(await Eval("ATANH(-1.0)"))!;
		double.IsNegativeInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task Atanh_OutOfDomain_ReturnsNaN()
	{
		var result = (double)(await Eval("ATANH(2.0)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	[Fact]
	public async Task Atanh_Null_IsNull()
	{
		var result = await Eval("ATANH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Pythagorean identity: sin²(x) + cos²(x) = 1
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("0")]
	[InlineData("1")]
	[InlineData("2")]
	[InlineData("0.5")]
	[InlineData("3.14")]
	[InlineData("10")]
	[InlineData("-1")]
	[InlineData("-0.5")]
	public async Task PythagoreanIdentity(string x)
	{
		var result = (double)(await Eval($"SIN({x}) * SIN({x}) + COS({x}) * COS({x})"))!;
		result.Should().BeApproximately(1.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Hyperbolic identity: cosh²(x) - sinh²(x) = 1
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("0")]
	[InlineData("1")]
	[InlineData("2")]
	[InlineData("0.5")]
	[InlineData("-1")]
	[InlineData("-0.5")]
	public async Task HyperbolicIdentity(string x)
	{
		var result = (double)(await Eval($"COSH({x}) * COSH({x}) - SINH({x}) * SINH({x})"))!;
		result.Should().BeApproximately(1.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Inverse function roundtrips
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("0")]
	[InlineData("0.5")]
	[InlineData("-0.5")]
	[InlineData("0.99")]
	[InlineData("-0.99")]
	public async Task Asin_Sin_Roundtrip(string x)
	{
		var result = (double)(await Eval($"SIN(ASIN({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("0.5")]
	[InlineData("-0.5")]
	[InlineData("0.99")]
	[InlineData("-0.99")]
	public async Task Acos_Cos_Roundtrip(string x)
	{
		var result = (double)(await Eval($"COS(ACOS({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("1")]
	[InlineData("-1")]
	[InlineData("0.5")]
	[InlineData("10")]
	public async Task Atan_Tan_Roundtrip(string x)
	{
		var result = (double)(await Eval($"TAN(ATAN({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("1")]
	[InlineData("-1")]
	[InlineData("0.5")]
	[InlineData("10")]
	public async Task Asinh_Sinh_Roundtrip(string x)
	{
		var result = (double)(await Eval($"SINH(ASINH({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("2")]
	[InlineData("10")]
	[InlineData("1.5")]
	public async Task Acosh_Cosh_Roundtrip(string x)
	{
		var result = (double)(await Eval($"COSH(ACOSH({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("0.5")]
	[InlineData("-0.5")]
	[InlineData("0.9")]
	[InlineData("-0.9")]
	public async Task Atanh_Tanh_Roundtrip(string x)
	{
		var result = (double)(await Eval($"TANH(ATANH({x}))"))!;
		result.Should().BeApproximately(double.Parse(x), 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Composed trig expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SIN(0) + COS(0)", 1.0)]
	[InlineData("SIN(1) * SIN(1)", 0.7080734182735712)]
	[InlineData("COS(1) * COS(1)", 0.2919265817264289)]
	[InlineData("TAN(0.5) - SIN(0.5) / COS(0.5)", 0.0)]
	[InlineData("SINH(0) + COSH(0)", 1.0)]
	[InlineData("TANH(0.5) - SINH(0.5) / COSH(0.5)", 0.0)]
	public async Task ComposedTrigExpressions(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	// ═══════════════════════════════════════════════════════════════
	// Integer arguments (implicit conversion to FLOAT64)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sin_IntegerArg()
	{
		var result = (double)(await Eval("SIN(CAST(1 AS INT64))"))!;
		result.Should().BeApproximately(Math.Sin(1), 1e-10);
	}

	[Fact]
	public async Task Cos_IntegerArg()
	{
		var result = (double)(await Eval("COS(CAST(2 AS INT64))"))!;
		result.Should().BeApproximately(Math.Cos(2), 1e-10);
	}

	[Fact]
	public async Task Atan2_IntegerArgs()
	{
		var result = (double)(await Eval("ATAN2(CAST(3 AS INT64), CAST(4 AS INT64))"))!;
		result.Should().BeApproximately(Math.Atan2(3, 4), 1e-10);
	}
}
