using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for trigonometric and hyperbolic functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TrigFunctionCoreIntegrationTests : IntegrationTestBase
{
	public TrigFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private const double Pi = Math.PI;
	private const double Tolerance = 1e-10;

	// ─── SIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sin

	[Theory]
	[InlineData("SIN(0.0)", 0.0)]
	[InlineData("SIN(1.0)", 0.8414709848078965)]
	[InlineData("SIN(-1.0)", -0.8414709848078965)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sin_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sin_PiOverTwo_IsOne()
	{
		var result = await Eval($"SIN(ACOS(-1.0) / 2)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sin_Null_ReturnsNull()
	{
		var result = await Eval("SIN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── COS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cos

	[Theory]
	[InlineData("COS(0.0)", 1.0)]
	[InlineData("COS(1.0)", 0.5403023058681398)]
	[InlineData("COS(-1.0)", 0.5403023058681398)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cos_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cos_Pi_IsMinusOne()
	{
		var result = await Eval("COS(ACOS(-1.0))");
		((double)result!).Should().BeApproximately(-1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cos_Null_ReturnsNull()
	{
		var result = await Eval("COS(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── TAN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#tan

	[Theory]
	[InlineData("TAN(0.0)", 0.0)]
	[InlineData("TAN(1.0)", 1.5574077246549023)]
	[InlineData("TAN(-1.0)", -1.5574077246549023)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tan_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tan_Null_ReturnsNull()
	{
		var result = await Eval("TAN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ASIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#asin

	[Theory]
	[InlineData("ASIN(0.0)", 0.0)]
	[InlineData("ASIN(1.0)", 1.5707963267948966)]
	[InlineData("ASIN(-1.0)", -1.5707963267948966)]
	[InlineData("ASIN(0.5)", 0.5235987755982988)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Asin_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Asin_Null_ReturnsNull()
	{
		var result = await Eval("ASIN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ACOS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#acos

	[Theory]
	[InlineData("ACOS(1.0)", 0.0)]
	[InlineData("ACOS(-1.0)", 3.141592653589793)]
	[InlineData("ACOS(0.0)", 1.5707963267948966)]
	[InlineData("ACOS(0.5)", 1.0471975511965979)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Acos_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Acos_Null_ReturnsNull()
	{
		var result = await Eval("ACOS(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ATAN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#atan

	[Theory]
	[InlineData("ATAN(0.0)", 0.0)]
	[InlineData("ATAN(1.0)", 0.7853981633974483)]
	[InlineData("ATAN(-1.0)", -0.7853981633974483)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Atan_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Atan_Null_ReturnsNull()
	{
		var result = await Eval("ATAN(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── SINH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sinh

	[Theory]
	[InlineData("SINH(0.0)", 0.0)]
	[InlineData("SINH(1.0)", 1.1752011936438014)]
	[InlineData("SINH(-1.0)", -1.1752011936438014)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sinh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sinh_Null_ReturnsNull()
	{
		var result = await Eval("SINH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── COSH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosh

	[Theory]
	[InlineData("COSH(0.0)", 1.0)]
	[InlineData("COSH(1.0)", 1.5430806348152437)]
	[InlineData("COSH(-1.0)", 1.5430806348152437)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cosh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cosh_Null_ReturnsNull()
	{
		var result = await Eval("COSH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── TANH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#tanh

	[Theory]
	[InlineData("TANH(0.0)", 0.0)]
	[InlineData("TANH(1.0)", 0.7615941559557649)]
	[InlineData("TANH(-1.0)", -0.7615941559557649)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tanh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tanh_LargePositive_ApproachesOne()
	{
		var result = await Eval("TANH(100.0)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tanh_LargeNegative_ApproachesMinusOne()
	{
		var result = await Eval("TANH(-100.0)");
		((double)result!).Should().BeApproximately(-1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tanh_Null_ReturnsNull()
	{
		var result = await Eval("TANH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ASINH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#asinh

	[Theory]
	[InlineData("ASINH(0.0)", 0.0)]
	[InlineData("ASINH(1.0)", 0.881373587019543)]
	[InlineData("ASINH(-1.0)", -0.881373587019543)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Asinh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Asinh_Null_ReturnsNull()
	{
		var result = await Eval("ASINH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ACOSH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#acosh

	[Theory]
	[InlineData("ACOSH(1.0)", 0.0)]
	[InlineData("ACOSH(2.0)", 1.3169578969248166)]
	[InlineData("ACOSH(10.0)", 2.993222846126381)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Acosh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Acosh_Null_ReturnsNull()
	{
		var result = await Eval("ACOSH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── ATANH ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#atanh

	[Theory]
	[InlineData("ATANH(0.0)", 0.0)]
	[InlineData("ATANH(0.5)", 0.5493061443340549)]
	[InlineData("ATANH(-0.5)", -0.5493061443340549)]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Atanh_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Atanh_Null_ReturnsNull()
	{
		var result = await Eval("ATANH(CAST(NULL AS FLOAT64))");
		result.Should().BeNull();
	}

	// ─── Trig identities ───

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task SinSquaredPlusCosSquared_IsOne()
	{
		// sin²(x) + cos²(x) = 1 for any x
		var result = await Eval("SIN(1.0) * SIN(1.0) + COS(1.0) * COS(1.0)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task CoshSquaredMinusSinhSquared_IsOne()
	{
		// cosh²(x) - sinh²(x) = 1
		var result = await Eval("COSH(1.0) * COSH(1.0) - SINH(1.0) * SINH(1.0)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AsinOfSin_IsIdentity()
	{
		// asin(sin(0.5)) ≈ 0.5
		var result = await Eval("ASIN(SIN(0.5))");
		((double)result!).Should().BeApproximately(0.5, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AcosOfCos_IsIdentity()
	{
		// acos(cos(1.0)) ≈ 1.0
		var result = await Eval("ACOS(COS(1.0))");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AtanOfTan_IsIdentity()
	{
		// atan(tan(0.5)) ≈ 0.5
		var result = await Eval("ATAN(TAN(0.5))");
		((double)result!).Should().BeApproximately(0.5, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AsinhOfSinh_IsIdentity()
	{
		var result = await Eval("ASINH(SINH(2.0))");
		((double)result!).Should().BeApproximately(2.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AcoshOfCosh_IsIdentity()
	{
		var result = await Eval("ACOSH(COSH(2.0))");
		((double)result!).Should().BeApproximately(2.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task AtanhOfTanh_IsIdentity()
	{
		var result = await Eval("ATANH(TANH(0.5))");
		((double)result!).Should().BeApproximately(0.5, Tolerance);
	}

	// ─── Trig functions in WHERE ───

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task TrigFunction_InWhereClause()
	{
		var table = "TrigTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Angle FLOAT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Angle"] = 0.0 });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Angle"] = 1.0 });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Angle"] = 2.0 });

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE SIN(Angle) > 0.5 ORDER BY Id");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 3L);
	}

	// ─── Trig with special values ───

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sin_Zero_ExactlyZero()
	{
		var result = await Eval("SIN(0.0)");
		((double)result!).Should().Be(0.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cos_Zero_ExactlyOne()
	{
		var result = await Eval("COS(0.0)");
		((double)result!).Should().Be(1.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tan_Zero_ExactlyZero()
	{
		var result = await Eval("TAN(0.0)");
		((double)result!).Should().Be(0.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Sinh_Zero_ExactlyZero()
	{
		var result = await Eval("SINH(0.0)");
		((double)result!).Should().Be(0.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Cosh_Zero_ExactlyOne()
	{
		var result = await Eval("COSH(0.0)");
		((double)result!).Should().Be(1.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task Tanh_Zero_ExactlyZero()
	{
		var result = await Eval("TANH(0.0)");
		((double)result!).Should().Be(0.0);
	}

	// ─── Trig in computed columns ───

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task TrigFunctions_InSelectExpression()
	{
		var result = await Eval("SIN(0.0) + COS(0.0) + TAN(0.0)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}

	[Fact]
	[Trait(TestTraits.Category, "TrigFunction")]
	public async Task HyperbolicFunctions_InSelectExpression()
	{
		var result = await Eval("SINH(0.0) + COSH(0.0) + TANH(0.0)");
		((double)result!).Should().BeApproximately(1.0, Tolerance);
	}
}
