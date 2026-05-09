using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for math and numeric functions, boundary values, and precision.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MathFunctionCoreIntegrationTests : IntegrationTestBase
{
	public MathFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── ABS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#abs

	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(42)", 42L)]
	[InlineData("ABS(-42)", 42L)]
	[InlineData("ABS(9223372036854775807)", 9223372036854775807L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Abs_IntegerValues(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	[InlineData("ABS(-3.14)", 3.14)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Abs_FloatValues(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Abs_Null_ReturnsNull()
	{
		var result = await Eval("ABS(CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ─── MOD ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod

	[Theory]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(10, 5)", 0L)]
	[InlineData("MOD(0, 5)", 0L)]
	[InlineData("MOD(7, 2)", 1L)]
	[InlineData("MOD(-7, 2)", -1L)]
	[InlineData("MOD(7, -2)", 1L)]
	[InlineData("MOD(-7, -2)", -1L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Mod_BasicCases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CEIL / FLOOR ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ceil

	[Theory]
	[InlineData("CEIL(1.5)", 2.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(-1.5)", -1.0)]
	[InlineData("CEIL(0.0)", 0.0)]
	[InlineData("CEIL(0.1)", 1.0)]
	[InlineData("CEILING(2.3)", 3.0)]
	[InlineData("FLOOR(1.5)", 1.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(-1.5)", -2.0)]
	[InlineData("FLOOR(0.0)", 0.0)]
	[InlineData("FLOOR(0.9)", 0.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task CeilFloor_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── ROUND ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round

	[Theory]
	[InlineData("ROUND(1.5)", 2.0)]
	[InlineData("ROUND(2.5)", 3.0)]  // Ref: Spanner ROUND rounds halfway cases away from zero
	[InlineData("ROUND(-1.5)", -2.0)]
	[InlineData("ROUND(1.0)", 1.0)]
	[InlineData("ROUND(0.0)", 0.0)]
	[InlineData("ROUND(3.14159, 2)", 3.14)]
	[InlineData("ROUND(3.14159, 0)", 3.0)]
	[InlineData("ROUND(314.159, -2)", 300.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Round_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── TRUNC ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc

	[Theory]
	[InlineData("TRUNC(1.9)", 1.0)]
	[InlineData("TRUNC(-1.9)", -1.0)]
	[InlineData("TRUNC(1.0)", 1.0)]
	[InlineData("TRUNC(0.0)", 0.0)]
	[InlineData("TRUNC(3.14159, 2)", 3.14)]
	[InlineData("TRUNC(3.14159, 0)", 3.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Trunc_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── SIGN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign

	[Theory]
	[InlineData("SIGN(42)", 1L)]
	[InlineData("SIGN(-42)", -1L)]
	[InlineData("SIGN(0)", 0L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Sign_IntValues(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("SIGN(3.14)", 1)]
	[InlineData("SIGN(-3.14)", -1)]
	[InlineData("SIGN(0.0)", 0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Sign_FloatValues(string expr, int expected)
	{
		var result = await Eval(expr);
		Convert.ToInt32(result).Should().Be(expected);
	}

	// ─── DIV ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div

	[Theory]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(10, 5)", 2L)]
	[InlineData("DIV(0, 5)", 0L)]
	[InlineData("DIV(7, 2)", 3L)]
	[InlineData("DIV(-7, 2)", -3L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Div_BasicCases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── IEEE_DIVIDE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide

	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 3.0)", 3.3333333333333335)]
	[InlineData("IEEE_DIVIDE(1.0, 0.0)", double.PositiveInfinity)]
	[InlineData("IEEE_DIVIDE(-1.0, 0.0)", double.NegativeInfinity)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task IeeeDivide_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		if (double.IsPositiveInfinity(expected))
			double.IsPositiveInfinity((double)result!).Should().BeTrue();
		else if (double.IsNegativeInfinity(expected))
			double.IsNegativeInfinity((double)result!).Should().BeTrue();
		else
			((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task IeeeDivide_ZeroByZero_IsNaN()
	{
		var result = await Eval("IEEE_DIVIDE(0.0, 0.0)");
		double.IsNaN((double)result!).Should().BeTrue();
	}

	// ─── SAFE_DIVIDE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		var result = await Eval("SAFE_DIVIDE(10, 0)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeDivide_Normal_ReturnsResult()
	{
		var result = await Eval("SAFE_DIVIDE(10.0, 3.0)");
		((double)result!).Should().BeApproximately(3.333333, 0.001);
	}

	// ─── SAFE_ADD / SAFE_SUBTRACT / SAFE_MULTIPLY ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_add

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeAdd_Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_ADD(9223372036854775807, 1)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeAdd_Normal_ReturnsResult()
	{
		var result = await Eval("SAFE_ADD(5, 3)");
		result.Should().Be(8L);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeSubtract_Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_SUBTRACT(-9223372036854775808, 1)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeSubtract_Normal_ReturnsResult()
	{
		var result = await Eval("SAFE_SUBTRACT(10, 3)");
		result.Should().Be(7L);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeMultiply_Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_MULTIPLY(9223372036854775807, 2)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeMultiply_Normal_ReturnsResult()
	{
		var result = await Eval("SAFE_MULTIPLY(5, 3)");
		result.Should().Be(15L);
	}

	// ─── SAFE_NEGATE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_negate

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeNegate_MinInt64_ReturnsNull()
	{
		var result = await Eval("SAFE_NEGATE(-9223372036854775808)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task SafeNegate_Normal_ReturnsResult()
	{
		var result = await Eval("SAFE_NEGATE(42)");
		result.Should().Be(-42L);
	}

	// ─── SQRT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sqrt

	[Theory]
	[InlineData("SQRT(4.0)", 2.0)]
	[InlineData("SQRT(9.0)", 3.0)]
	[InlineData("SQRT(0.0)", 0.0)]
	[InlineData("SQRT(1.0)", 1.0)]
	[InlineData("SQRT(2.0)", 1.4142135623730951)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Sqrt_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── POW / POWER ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#pow

	[Theory]
	[InlineData("POW(2.0, 3.0)", 8.0)]
	[InlineData("POW(2.0, 0.0)", 1.0)]
	[InlineData("POW(2.0, -1.0)", 0.5)]
	[InlineData("POWER(3.0, 2.0)", 9.0)]
	[InlineData("POW(10.0, 0.0)", 1.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Pow_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── EXP ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#exp

	[Theory]
	[InlineData("EXP(0.0)", 1.0)]
	[InlineData("EXP(1.0)", 2.718281828459045)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Exp_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── LN / LOG ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ln

	[Theory]
	[InlineData("LN(1.0)", 0.0)]
	[InlineData("LN(2.718281828459045)", 1.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Ln_BasicCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("LOG(100.0, 10.0)", 2.0)]
	[InlineData("LOG(8.0, 2.0)", 3.0)]
	[InlineData("LOG(1.0, 10.0)", 0.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task Log_WithBase(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── IS_NAN / IS_INF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#is_nan

	[Theory]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task IsNan_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(IEEE_DIVIDE(-1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task IsInf_Cases(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── GREATEST / LEAST ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest

	[Theory]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(-1, 0, 1)", 1L)]
	[InlineData("GREATEST(42)", 42L)]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(-1, 0, 1)", -1L)]
	[InlineData("LEAST(42)", 42L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task GreatestLeast_IntegerCases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("GREATEST(1.0, 2.0, 3.0)", 3.0)]
	[InlineData("LEAST(1.0, 2.0, 3.0)", 1.0)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task GreatestLeast_FloatCases(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("GREATEST('a', 'b', 'c')", "c")]
	[InlineData("LEAST('a', 'b', 'c')", "a")]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task GreatestLeast_StringCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Arithmetic with table data ───

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task ArithmeticInSelect_WithTableData()
	{
		var table = "MathTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync($"SELECT Val * 2 AS doubled, Val + 5 AS plus5, Val - 1 AS minus1 FROM {table} ORDER BY Id");
		rows.Should().HaveCount(3);
		rows[0]["doubled"].Should().Be(20L);
		rows[0]["plus5"].Should().Be(15L);
		rows[0]["minus1"].Should().Be(9L);
		rows[2]["doubled"].Should().Be(60L);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task MathFunction_InWhereClause()
	{
		var table = "MathTbl2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = -5L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 25L });

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE ABS(Val) > 10 ORDER BY Id");
		rows.Select(r => (long)r["Id"]!).Should().Equal(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task MathFunction_InOrderBy()
	{
		var table = "MathTbl3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = -30L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = -20L });

		var rows = await QueryAsync($"SELECT Id FROM {table} ORDER BY ABS(Val)");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 3L, 1L);
	}

	// ─── Unary minus ───

	[Theory]
	[InlineData("-0", 0L)]
	[InlineData("-1", -1L)]
	[InlineData("-(-1)", 1L)]
	[InlineData("-(-(-5))", -5L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task UnaryMinus_IntCases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Arithmetic operators ───

	[Theory]
	[InlineData("1 + 2", 3L)]
	[InlineData("10 - 3", 7L)]
	[InlineData("4 * 5", 20L)]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("2 + 3 * 4", 14L)]
	[InlineData("(2 + 3) * 4", 20L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task ArithmeticOperators_Precedence(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 + 2.0", 3.0)]
	[InlineData("10.0 - 3.0", 7.0)]
	[InlineData("4.0 * 5.0", 20.0)]
	[InlineData("10.0 / 3.0", 3.3333333333333335)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task ArithmeticOperators_Float(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── BIT_COUNT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_count

	[Theory]
	[InlineData("BIT_COUNT(0)", 0L)]
	[InlineData("BIT_COUNT(1)", 1L)]
	[InlineData("BIT_COUNT(7)", 3L)]
	[InlineData("BIT_COUNT(255)", 8L)]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task BitCount_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── BIT_REVERSE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_reverse

	[Theory]
	[InlineData("BIT_REVERSE(0, true)", 0L)]
	[InlineData("BIT_REVERSE(1, true)", 4611686018427387904L)]      // 0x4000000000000000 — sign bit preserved
	[Trait(TestTraits.Category, "MathFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task BitReverse_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── FARM_FINGERPRINT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions#farm_fingerprint

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task FarmFingerprint_DeterministicForSameInput()
	{
		var r1 = await Eval("FARM_FINGERPRINT('hello')");
		var r2 = await Eval("FARM_FINGERPRINT('hello')");
		r1.Should().Be(r2);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task FarmFingerprint_DifferentInputsDifferentResults()
	{
		var r1 = await Eval("FARM_FINGERPRINT('hello')");
		var r2 = await Eval("FARM_FINGERPRINT('world')");
		r1.Should().NotBe(r2);
	}

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task FarmFingerprint_Null_ReturnsNull()
	{
		var result = await Eval("FARM_FINGERPRINT(CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	// ─── Null propagation in math ───

	[Theory]
	[InlineData("CAST(NULL AS INT64) + 1")]
	[InlineData("1 + CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) * 2")]
	[InlineData("CAST(NULL AS INT64) - 1")]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task NullPropagation_Arithmetic(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── GENERATE_UUID ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#generate_uuid

	[Fact]
	[Trait(TestTraits.Category, "MathFunction")]
	public async Task GenerateUuid_ReturnsDifferentValues()
	{
		var r1 = (string)(await Eval("GENERATE_UUID()"))!;
		var r2 = (string)(await Eval("GENERATE_UUID()"))!;
		r1.Should().NotBe(r2);
		Guid.TryParse(r1, out _).Should().BeTrue();
		Guid.TryParse(r2, out _).Should().BeTrue();
	}
}
