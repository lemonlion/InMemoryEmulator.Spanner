using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extremely dense scalar expression tests. Each InlineData is one test.
/// Tests cross-type expressions, edge cases, and unusual combinations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DenseScalarIntegrationTests : IntegrationTestBase
{
	public DenseScalarIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Integer expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("0", 0L)]
	[InlineData("1", 1L)]
	[InlineData("-1", -1L)]
	[InlineData("42", 42L)]
	[InlineData("100", 100L)]
	[InlineData("-100", -100L)]
	[InlineData("999999999", 999999999L)]
	[InlineData("1 + 0", 1L)]
	[InlineData("0 + 0", 0L)]
	[InlineData("1 + 1", 2L)]
	[InlineData("100 + 200", 300L)]
	[InlineData("1000 - 1", 999L)]
	[InlineData("5 - 10", -5L)]
	[InlineData("0 - 0", 0L)]
	[InlineData("2 * 3", 6L)]
	[InlineData("0 * 100", 0L)]
	[InlineData("100 * 0", 0L)]
	[InlineData("-2 * 3", -6L)]
	[InlineData("-2 * -3", 6L)]
	[InlineData("10 / 2", 5L)]
	[InlineData("10 / 3", 3L)]
	[InlineData("0 / 1", 0L)]
	[InlineData("-10 / 3", -3L)]
	[InlineData("10 / -3", -3L)]
	[InlineData("1 + 2 * 3", 7L)]
	[InlineData("(1 + 2) * 3", 9L)]
	[InlineData("10 - 2 * 3", 4L)]
	[InlineData("(10 - 2) * 3", 24L)]
	[InlineData("2 + 3 * 4 - 5", 9L)]
	[InlineData("(2 + 3) * (4 - 5)", -5L)]
	[InlineData("-(-1)", 1L)]
	[InlineData("-(-(-1))", -1L)]
	[InlineData("ABS(-42)", 42L)]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(42)", 42L)]
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(9, 3)", 0L)]
	[InlineData("MOD(0, 1)", 0L)]
	[InlineData("MOD(-7, 3)", -1L)]
	[InlineData("MOD(7, -3)", 1L)]
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(9, 3)", 3L)]
	[InlineData("DIV(10, 10)", 1L)]
	[InlineData("DIV(0, 1)", 0L)]
	[InlineData("SIGN(42)", 1L)]
	[InlineData("SIGN(-42)", -1L)]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(-1, -2, -3)", -1L)]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(-1, -2, -3)", -3L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("LEAST(5, 5, 5)", 5L)]
	[InlineData("ABS(MOD(-10, 3))", 1L)]
	[InlineData("MOD(ABS(-10), 3)", 1L)]
	[InlineData("ABS(SIGN(-5))", 1L)]
	[InlineData("SIGN(ABS(-5))", 1L)]
	[InlineData("GREATEST(ABS(-5), ABS(-3))", 5L)]
	[InlineData("LEAST(ABS(-5), ABS(-3))", 3L)]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task IntegerExpressions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Float expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("0.0", 0.0)]
	[InlineData("1.0", 1.0)]
	[InlineData("-1.0", -1.0)]
	[InlineData("3.14", 3.14)]
	[InlineData("1.0 + 1.0", 2.0)]
	[InlineData("1.5 + 2.5", 4.0)]
	[InlineData("10.0 - 3.5", 6.5)]
	[InlineData("2.5 * 4.0", 10.0)]
	[InlineData("10.0 / 4.0", 2.5)]
	[InlineData("CEIL(0.1)", 1.0)]
	[InlineData("CEIL(0.9)", 1.0)]
	[InlineData("CEIL(1.0)", 1.0)]
	[InlineData("CEIL(-0.1)", 0.0)]
	[InlineData("CEIL(-0.9)", 0.0)]
	[InlineData("FLOOR(0.1)", 0.0)]
	[InlineData("FLOOR(0.9)", 0.0)]
	[InlineData("FLOOR(1.0)", 1.0)]
	[InlineData("FLOOR(-0.1)", -1.0)]
	[InlineData("FLOOR(-0.9)", -1.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(1.5)", 2.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	[InlineData("ROUND(3.14159, 2)", 3.14)]
	[InlineData("ROUND(3.14159, 4)", 3.1416)]
	[InlineData("TRUNC(1.9)", 1.0)]
	[InlineData("TRUNC(-1.9)", -1.0)]
	[InlineData("TRUNC(3.14159, 2)", 3.14)]
	[InlineData("ABS(-3.14)", 3.14)]
	[InlineData("ABS(3.14)", 3.14)]
	[InlineData("SIGN(3.14)", 1.0)]
	[InlineData("SIGN(-3.14)", -1.0)]
	[InlineData("SIGN(0.0)", 0.0)]
	[InlineData("GREATEST(1.5, 2.5)", 2.5)]
	[InlineData("LEAST(1.5, 2.5)", 1.5)]
	[InlineData("IEEE_DIVIDE(1.0, 2.0)", 0.5)]
	[InlineData("IEEE_DIVIDE(10.0, 3.0)", 3.3333333333333335)]
	public async Task FloatExpressions(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("SQRT(0.0)", 0.0)]
	[InlineData("SQRT(1.0)", 1.0)]
	[InlineData("SQRT(4.0)", 2.0)]
	[InlineData("SQRT(9.0)", 3.0)]
	[InlineData("SQRT(16.0)", 4.0)]
	[InlineData("SQRT(25.0)", 5.0)]
	[InlineData("SQRT(100.0)", 10.0)]
	[InlineData("POW(2, 0)", 1.0)]
	[InlineData("POW(2, 1)", 2.0)]
	[InlineData("POW(2, 8)", 256.0)]
	[InlineData("POW(2, 10)", 1024.0)]
	[InlineData("POW(3, 3)", 27.0)]
	[InlineData("POW(10, 0)", 1.0)]
	[InlineData("POW(10, 1)", 10.0)]
	[InlineData("POW(10, 2)", 100.0)]
	[InlineData("POW(10, 3)", 1000.0)]
	[InlineData("EXP(0)", 1.0)]
	[InlineData("LN(1)", 0.0)]
	[InlineData("LOG10(1)", 0.0)]
	[InlineData("LOG10(10)", 1.0)]
	[InlineData("LOG10(100)", 2.0)]
	[InlineData("LOG(8, 2)", 3.0)]
	[InlineData("LOG(1000, 10)", 3.0)]
	[InlineData("SQRT(POW(3.0, 2) + POW(4.0, 2))", 5.0)]
	[InlineData("POW(SQRT(16.0), 2)", 16.0)]
	[InlineData("EXP(LN(5.0))", 5.0)]
	[InlineData("ROUND(SQRT(2.0), 4)", 1.4142)]
	public async Task MathFunctionExpressions(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-4);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Boolean expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TRUE", true)]
	[InlineData("FALSE", false)]
	[InlineData("NOT TRUE", false)]
	[InlineData("NOT FALSE", true)]
	[InlineData("TRUE AND TRUE", true)]
	[InlineData("TRUE AND FALSE", false)]
	[InlineData("FALSE AND TRUE", false)]
	[InlineData("FALSE AND FALSE", false)]
	[InlineData("TRUE OR TRUE", true)]
	[InlineData("TRUE OR FALSE", true)]
	[InlineData("FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE", false)]
	[InlineData("NOT NOT TRUE", true)]
	[InlineData("NOT NOT FALSE", false)]
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("1 != 2", true)]
	[InlineData("1 < 2", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 <= 1", true)]
	[InlineData("1 >= 1", true)]
	[InlineData("1 < 1", false)]
	[InlineData("1 > 1", false)]
	[InlineData("'a' = 'a'", true)]
	[InlineData("'a' = 'b'", false)]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' > 'a'", true)]
	[InlineData("1.0 = 1.0", true)]
	[InlineData("1.0 < 2.0", true)]
	[InlineData("2.0 > 1.0", true)]
	[InlineData("1 BETWEEN 1 AND 3", true)]
	[InlineData("2 BETWEEN 1 AND 3", true)]
	[InlineData("3 BETWEEN 1 AND 3", true)]
	[InlineData("0 BETWEEN 1 AND 3", false)]
	[InlineData("4 BETWEEN 1 AND 3", false)]
	[InlineData("2 NOT BETWEEN 1 AND 3", false)]
	[InlineData("0 NOT BETWEEN 1 AND 3", true)]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("'a' IN ('a', 'b')", true)]
	[InlineData("'c' IN ('a', 'b')", false)]
	[InlineData("CAST(NULL AS INT64) IS NULL", true)]
	[InlineData("1 IS NULL", false)]
	[InlineData("CAST(NULL AS INT64) IS NOT NULL", false)]
	[InlineData("1 IS NOT NULL", true)]
	[InlineData("STARTS_WITH('abc', 'ab')", true)]
	[InlineData("STARTS_WITH('abc', 'bc')", false)]
	[InlineData("ENDS_WITH('abc', 'bc')", true)]
	[InlineData("ENDS_WITH('abc', 'ab')", false)]
	[InlineData("REGEXP_CONTAINS('abc', 'a')", true)]
	[InlineData("REGEXP_CONTAINS('abc', 'd')", false)]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	public async Task BooleanExpressions(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Conditional expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IF(TRUE, 1, 2)", 1L)]
	[InlineData("IF(FALSE, 1, 2)", 2L)]
	[InlineData("IF(1=1, 'y', 'n')", "y")]
	[InlineData("IF(1=2, 'y', 'n')", "n")]
	[InlineData("COALESCE(1, 2)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64), 3)", 3L)]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("NULLIF(1, 1)", null)]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("IF(TRUE, IF(TRUE, 1, 2), 3)", 1L)]
	[InlineData("IF(TRUE, IF(FALSE, 1, 2), 3)", 2L)]
	[InlineData("IF(FALSE, 1, IF(TRUE, 2, 3))", 2L)]
	[InlineData("COALESCE(NULLIF(1, 1), 2)", 2L)]
	[InlineData("IFNULL(NULLIF(1, 1), 99)", 99L)]
	public async Task ConditionalExpressions(string expr, object? expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'x' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'x' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'x' END", "x")]
	[InlineData("CASE WHEN 1=1 THEN 'a' WHEN 2=2 THEN 'b' ELSE 'c' END", "a")]
	[InlineData("CASE WHEN 1=2 THEN 'a' WHEN 2=2 THEN 'b' ELSE 'c' END", "b")]
	[InlineData("CASE WHEN 1=2 THEN 'a' WHEN 2=3 THEN 'b' ELSE 'c' END", "c")]
	[InlineData("CASE WHEN TRUE THEN 'yes' ELSE 'no' END", "yes")]
	[InlineData("CASE WHEN FALSE THEN 'yes' ELSE 'no' END", "no")]
	public async Task CaseExpressions(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(1 AS STRING)", "1")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(TRUE AS STRING)", "true")]
	[InlineData("CAST(FALSE AS STRING)", "false")]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(0 AS BOOL)", false)]
	[InlineData("CAST(TRUE AS INT64)", 1L)]
	[InlineData("CAST(FALSE AS INT64)", 0L)]
	[InlineData("CAST('1' AS INT64)", 1L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(TRUE AS INT64) AS STRING)", "1")]
	public async Task CastExpressions(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 0)")]
	[InlineData("SAFE_DIVIDE(0, 0)")]
	public async Task SafeFunctions_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 2)", 5.0)]
	[InlineData("SAFE_DIVIDE(10, 4)", 2.5)]
	[InlineData("SAFE_DIVIDE(0, 1)", 0.0)]
	public async Task SafeFunctions_ValidResults(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	public async Task SafeNegate_Results(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Complex nested expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(ROUND(-3.7))", 4.0)]
	[InlineData("CEIL(SQRT(10.0))", 4.0)]
	[InlineData("FLOOR(SQRT(10.0))", 3.0)]
	[InlineData("ROUND(SQRT(2.0), 2)", 1.41)]
	[InlineData("ROUND(POW(2.0, 0.5), 2)", 1.41)]
	[InlineData("CEIL(LN(10.0))", 3.0)]
	[InlineData("FLOOR(LOG10(500.0))", 2.0)]
	[InlineData("ROUND(EXP(1.0), 2)", 2.72)]
	public async Task NestedMathExpressions(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-2);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE_CAST
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	[InlineData("SAFE_CAST('not_a_date' AS DATE)")]
	public async Task SafeCast_InvalidReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// REGEXP functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("REGEXP_EXTRACT('abc123', '[0-9]+')", "123")]
	[InlineData("REGEXP_EXTRACT('hello world', '[a-z]+')", "hello")]
	[InlineData("REGEXP_REPLACE('abc', 'b', 'X')", "aXc")]
	[InlineData("REGEXP_REPLACE('aabbcc', 'b+', 'X')", "aaXcc")]
	[InlineData("REGEXP_REPLACE('abc123', '[0-9]+', '')", "abc")]
	public async Task RegexpFunctionResults(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Hex/bytes
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x0f')", "0f")]
	[InlineData("TO_HEX(b'\\xab\\xcd')", "abcd")]
	public async Task HexExpressions(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Mixed type expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("LENGTH(CAST(12345 AS STRING))", 5L)]
	[InlineData("ABS(CAST('-42' AS INT64))", 42L)]
	[InlineData("CAST(LENGTH('hello') AS STRING)", "5")]
	[InlineData("CAST(ABS(-42) AS STRING)", "42")]
	[InlineData("IF(LENGTH('abc') > 2, 'long', 'short')", "long")]
	[InlineData("IF(LENGTH('a') > 2, 'long', 'short')", "short")]
	[InlineData("CONCAT(CAST(1+2 AS STRING), CAST(3+4 AS STRING))", "37")]
	[InlineData("LENGTH(CONCAT(REPEAT('a', 10), REPEAT('b', 10)))", 20L)]
	public async Task MixedTypeExpressions(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);
}
