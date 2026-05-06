using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for SQL operators, comparison, boolean logic, BETWEEN, IN,
/// IS NULL/IS NOT NULL, string concatenation, arithmetic, and conditional expressions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class OperatorIntegrationTests : IntegrationTestBase
{
	public OperatorIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Comparison operators returning BOOL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
	[InlineData("1 <= 1", true)]
	[InlineData("1 <= 2", true)]
	[InlineData("2 <= 1", false)]
	[InlineData("2 > 1", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 > 1", false)]
	[InlineData("1 >= 1", true)]
	[InlineData("2 >= 1", true)]
	[InlineData("1 >= 2", false)]
	// Negative integers
	[InlineData("-1 < 0", true)]
	[InlineData("-1 < -2", false)]
	[InlineData("-2 < -1", true)]
	[InlineData("0 = -0", true)]
	// Large integers
	[InlineData("9223372036854775807 > 0", true)]
	[InlineData("-9223372036854775808 < 0", true)]
	// FLOAT64 comparisons
	[InlineData("1.0 = 1.0", true)]
	[InlineData("1.0 = 2.0", false)]
	[InlineData("1.5 < 2.5", true)]
	[InlineData("2.5 < 1.5", false)]
	[InlineData("1.5 <= 1.5", true)]
	[InlineData("2.5 > 1.5", true)]
	[InlineData("1.5 >= 1.5", true)]
	[InlineData("1.5 != 2.5", true)]
	[InlineData("0.1 + 0.2 > 0.0", true)]
	// Mixed INT64/FLOAT64
	[InlineData("1 = 1.0", true)]
	[InlineData("1 < 1.5", true)]
	[InlineData("2 > 1.5", true)]
	[InlineData("1 != 1.5", true)]
	// STRING comparisons
	[InlineData("'a' = 'a'", true)]
	[InlineData("'a' = 'b'", false)]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' < 'a'", false)]
	[InlineData("'a' <= 'a'", true)]
	[InlineData("'b' > 'a'", true)]
	[InlineData("'a' >= 'a'", true)]
	[InlineData("'a' != 'b'", true)]
	[InlineData("'' < 'a'", true)]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'abc' < 'abcd'", true)]
	[InlineData("'A' < 'a'", true)]
	// BOOL comparisons
	[InlineData("true = true", true)]
	[InlineData("false = false", true)]
	[InlineData("true = false", false)]
	[InlineData("true != false", true)]
	[InlineData("false < true", true)]
	[InlineData("true > false", true)]
	// DATE comparisons
	[InlineData("DATE '2024-01-01' = DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' < DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-12-31' > DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-02'", true)]
	// TIMESTAMP comparisons
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' = TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' < TIMESTAMP '2024-01-01T01:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T01:00:00Z' > TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	public async Task ComparisonOperators_Bool(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Arithmetic operators
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// Addition INT64
	[InlineData("1 + 1", 2L)]
	[InlineData("0 + 0", 0L)]
	[InlineData("-1 + 1", 0L)]
	[InlineData("100 + 200", 300L)]
	[InlineData("-50 + -30", -80L)]
	// Subtraction INT64
	[InlineData("5 - 3", 2L)]
	[InlineData("3 - 5", -2L)]
	[InlineData("0 - 0", 0L)]
	[InlineData("-1 - -1", 0L)]
	// Multiplication INT64
	[InlineData("3 * 4", 12L)]
	[InlineData("0 * 100", 0L)]
	[InlineData("-3 * 4", -12L)]
	[InlineData("-3 * -4", 12L)]
	// Integer division
	[InlineData("DIV(10, 3)", 3L)]
	[InlineData("DIV(9, 3)", 3L)]
	[InlineData("DIV(1, 3)", 0L)]
	[InlineData("DIV(-10, 3)", -3L)]
	[InlineData("DIV(10, -3)", -3L)]
	// Modulo
	[InlineData("MOD(10, 3)", 1L)]
	[InlineData("MOD(9, 3)", 0L)]
	[InlineData("MOD(1, 3)", 1L)]
	[InlineData("MOD(-10, 3)", -1L)]
	[InlineData("MOD(10, -3)", 1L)]
	// Unary minus
	[InlineData("-(-5)", 5L)]
	[InlineData("-(0)", 0L)]
	[InlineData("-1", -1L)]
	// Complex expressions
	[InlineData("2 + 3 * 4", 14L)]
	[InlineData("(2 + 3) * 4", 20L)]
	[InlineData("10 - 2 * 3", 4L)]
	[InlineData("(10 - 2) * 3", 24L)]
	[InlineData("2 + 3 + 4 + 5", 14L)]
	[InlineData("100 - 50 + 25", 75L)]
	public async Task ArithmeticOperators_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	// Float arithmetic
	[InlineData("1.0 + 2.0", 3.0)]
	[InlineData("1.5 + 2.5", 4.0)]
	[InlineData("5.0 - 3.0", 2.0)]
	[InlineData("3.0 * 4.0", 12.0)]
	[InlineData("10.0 / 3.0", 3.3333333333333335)]
	[InlineData("1.0 / 3.0", 0.3333333333333333)]
	[InlineData("10.0 / 2.0", 5.0)]
	[InlineData("0.0 / 1.0", 0.0)]
	// Mixed INT64/FLOAT64 arithmetic
	[InlineData("1 + 1.5", 2.5)]
	[InlineData("3 * 2.5", 7.5)]
	[InlineData("10 / 3.0", 3.3333333333333335)]
	[InlineData("7.0 - 2", 5.0)]
	// Unary minus on float
	[InlineData("-1.5", -1.5)]
	[InlineData("-(-2.5)", 2.5)]
	public async Task ArithmeticOperators_Float(string expr, double expected)
	{
		var result = (double)(await Eval(expr))!;
		result.Should().BeApproximately(expected, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Boolean / logical operators
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// AND
	[InlineData("true AND true", true)]
	[InlineData("true AND false", false)]
	[InlineData("false AND true", false)]
	[InlineData("false AND false", false)]
	// OR
	[InlineData("true OR true", true)]
	[InlineData("true OR false", true)]
	[InlineData("false OR true", true)]
	[InlineData("false OR false", false)]
	// NOT
	[InlineData("NOT true", false)]
	[InlineData("NOT false", true)]
	// Complex boolean
	[InlineData("true AND true AND true", true)]
	[InlineData("true AND false AND true", false)]
	[InlineData("false OR false OR true", true)]
	[InlineData("true AND (false OR true)", true)]
	[InlineData("false OR (true AND true)", true)]
	[InlineData("NOT (true AND false)", true)]
	[InlineData("NOT true OR true", true)]
	[InlineData("NOT (true OR false)", false)]
	[InlineData("true AND NOT false", true)]
	[InlineData("false OR NOT false", true)]
	// Boolean with comparisons
	[InlineData("1 = 1 AND 2 = 2", true)]
	[InlineData("1 = 1 AND 2 = 3", false)]
	[InlineData("1 = 2 OR 2 = 2", true)]
	[InlineData("1 = 2 OR 2 = 3", false)]
	[InlineData("NOT (1 = 2)", true)]
	[InlineData("1 < 2 AND 3 > 2", true)]
	[InlineData("1 > 2 OR 3 < 2", false)]
	public async Task BooleanOperators(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// BETWEEN operator
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// INT64 BETWEEN
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("11 BETWEEN 1 AND 10", false)]
	[InlineData("-5 BETWEEN -10 AND 0", true)]
	[InlineData("-10 BETWEEN -10 AND 0", true)]
	[InlineData("0 BETWEEN -10 AND 0", true)]
	[InlineData("-11 BETWEEN -10 AND 0", false)]
	// NOT BETWEEN
	[InlineData("0 NOT BETWEEN 1 AND 10", true)]
	[InlineData("5 NOT BETWEEN 1 AND 10", false)]
	[InlineData("11 NOT BETWEEN 1 AND 10", true)]
	// FLOAT64 BETWEEN
	[InlineData("1.5 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("1.0 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("2.0 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("0.5 BETWEEN 1.0 AND 2.0", false)]
	// STRING BETWEEN
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'a' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'c' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	// DATE BETWEEN
	[InlineData("DATE '2024-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2023-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	public async Task BetweenOperator(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IN operator
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// INT64 IN
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("1 IN (1)", true)]
	[InlineData("2 IN (1)", false)]
	// NOT IN
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	// STRING IN
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("'' IN ('', 'a')", true)]
	[InlineData("'a' NOT IN ('b', 'c')", true)]
	// BOOL IN
	[InlineData("true IN (true, false)", true)]
	[InlineData("true IN (false)", false)]
	// FLOAT64 IN
	[InlineData("1.5 IN (1.0, 1.5, 2.0)", true)]
	[InlineData("3.0 IN (1.0, 1.5, 2.0)", false)]
	// Expression in list
	[InlineData("1 + 1 IN (2, 3, 4)", true)]
	[InlineData("1 + 1 IN (1, 3, 4)", false)]
	public async Task InOperator(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IS TRUE / IS FALSE / IS NOT TRUE / IS NOT FALSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("true IS TRUE", true)]
	[InlineData("false IS TRUE", false)]
	[InlineData("true IS FALSE", false)]
	[InlineData("false IS FALSE", true)]
	[InlineData("true IS NOT TRUE", false)]
	[InlineData("false IS NOT TRUE", true)]
	[InlineData("true IS NOT FALSE", true)]
	[InlineData("false IS NOT FALSE", false)]
	public async Task IsTrueFalseOperator(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// String concatenation operator ||
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("'a' || 'b'", "ab")]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("'' || 'a'", "a")]
	[InlineData("'a' || ''", "a")]
	[InlineData("'' || ''", "")]
	[InlineData("'abc' || 'def' || 'ghi'", "abcdefghi")]
	[InlineData("CAST(1 AS STRING) || '2'", "12")]
	public async Task StringConcatOperator(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// COALESCE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("COALESCE(1, 2)", 1L)]
	[InlineData("COALESCE(NULL, 2)", 2L)]
	[InlineData("COALESCE(NULL, NULL, 3)", 3L)]
	[InlineData("COALESCE(NULL, NULL, NULL, 4)", 4L)]
	[InlineData("COALESCE(10, NULL, 20)", 10L)]
	public async Task Coalesce_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE('a', 'b')", "a")]
	[InlineData("COALESCE(NULL, 'b')", "b")]
	[InlineData("COALESCE(NULL, NULL, 'c')", "c")]
	[InlineData("COALESCE('', 'b')", "")]
	public async Task Coalesce_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task Coalesce_AllNull()
	{
		(await Eval("COALESCE(NULL, NULL, NULL)")).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IF expression
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IF(true, 1, 2)", 1L)]
	[InlineData("IF(false, 1, 2)", 2L)]
	[InlineData("IF(1 = 1, 10, 20)", 10L)]
	[InlineData("IF(1 = 2, 10, 20)", 20L)]
	[InlineData("IF(1 < 2, 100, 200)", 100L)]
	[InlineData("IF(2 < 1, 100, 200)", 200L)]
	public async Task If_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(true, 'yes', 'no')", "yes")]
	[InlineData("IF(false, 'yes', 'no')", "no")]
	[InlineData("IF(1 > 0, 'pos', 'neg')", "pos")]
	[InlineData("IF(-1 > 0, 'pos', 'neg')", "neg")]
	public async Task If_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(true, true, false)", true)]
	[InlineData("IF(false, true, false)", false)]
	public async Task If_Bool(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task If_NullCondition_ReturnsElse()
	{
		(await Eval("IF(NULL, 1, 2)")).Should().Be(2L);
	}

	[Fact]
	public async Task If_TrueNullResult()
	{
		(await Eval("IF(true, NULL, 1)")).Should().BeNull();
	}

	[Fact]
	public async Task If_FalseNullResult()
	{
		(await Eval("IF(false, 1, NULL)")).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IFNULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(NULL, 2)", 2L)]
	[InlineData("IFNULL(0, 2)", 0L)]
	[InlineData("IFNULL(-1, 2)", -1L)]
	public async Task Ifnull_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IFNULL('a', 'b')", "a")]
	[InlineData("IFNULL(NULL, 'b')", "b")]
	[InlineData("IFNULL('', 'b')", "")]
	public async Task Ifnull_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task Ifnull_BothNull()
	{
		(await Eval("IFNULL(NULL, NULL)")).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULLIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF('a', 'a')")]
	[InlineData("NULLIF(true, true)")]
	public async Task Nullif_Equal_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF(2, 1)", 2L)]
	[InlineData("NULLIF(0, 1)", 0L)]
	public async Task Nullif_NotEqual_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("NULLIF('a', 'b')", "a")]
	[InlineData("NULLIF('', 'a')", "")]
	public async Task Nullif_NotEqual_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CASE expressions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// Searched CASE
	[InlineData("CASE WHEN true THEN 1 ELSE 0 END", 1L)]
	[InlineData("CASE WHEN false THEN 1 ELSE 0 END", 0L)]
	[InlineData("CASE WHEN 1 = 1 THEN 10 ELSE 20 END", 10L)]
	[InlineData("CASE WHEN 1 = 2 THEN 10 ELSE 20 END", 20L)]
	[InlineData("CASE WHEN 1 < 2 THEN 1 WHEN 1 = 2 THEN 2 ELSE 3 END", 1L)]
	[InlineData("CASE WHEN 1 > 2 THEN 1 WHEN 1 = 1 THEN 2 ELSE 3 END", 2L)]
	[InlineData("CASE WHEN 1 > 2 THEN 1 WHEN 1 > 3 THEN 2 ELSE 3 END", 3L)]
	// Simple CASE
	[InlineData("CASE 1 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 10L)]
	[InlineData("CASE 2 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 20L)]
	[InlineData("CASE 3 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 30L)]
	[InlineData("CASE 1 + 1 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 20L)]
	// Nested CASE
	[InlineData("CASE WHEN true THEN CASE WHEN true THEN 1 ELSE 2 END ELSE 3 END", 1L)]
	[InlineData("CASE WHEN true THEN CASE WHEN false THEN 1 ELSE 2 END ELSE 3 END", 2L)]
	[InlineData("CASE WHEN false THEN 1 ELSE CASE WHEN true THEN 2 ELSE 3 END END", 2L)]
	public async Task CaseExpression_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CASE WHEN true THEN 'yes' ELSE 'no' END", "yes")]
	[InlineData("CASE WHEN false THEN 'yes' ELSE 'no' END", "no")]
	[InlineData("CASE 'a' WHEN 'a' THEN 'alpha' WHEN 'b' THEN 'beta' ELSE 'other' END", "alpha")]
	[InlineData("CASE 'b' WHEN 'a' THEN 'alpha' WHEN 'b' THEN 'beta' ELSE 'other' END", "beta")]
	[InlineData("CASE 'c' WHEN 'a' THEN 'alpha' WHEN 'b' THEN 'beta' ELSE 'other' END", "other")]
	public async Task CaseExpression_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task CaseExpression_NoElse_NoMatch_ReturnsNull()
	{
		(await Eval("CASE WHEN false THEN 1 END")).Should().BeNull();
	}

	[Fact]
	public async Task CaseExpression_SimpleNoElse_NoMatch_ReturnsNull()
	{
		(await Eval("CASE 3 WHEN 1 THEN 10 WHEN 2 THEN 20 END")).Should().BeNull();
	}

	// Three-valued logic: NULL AND false = false, NULL OR true = true
	[Theory]
	[InlineData("NULL AND false", false)]
	[InlineData("false AND NULL", false)]
	[InlineData("NULL OR true", true)]
	[InlineData("true OR NULL", true)]
	public async Task ThreeValuedLogic_Definite(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Literal expressions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#literals
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("0", 0L)]
	[InlineData("1", 1L)]
	[InlineData("-1", -1L)]
	[InlineData("42", 42L)]
	[InlineData("100", 100L)]
	[InlineData("9223372036854775807", 9223372036854775807L)]
	[InlineData("-9223372036854775808", -9223372036854775808L)]
	public async Task IntegerLiterals(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("0.0", 0.0)]
	[InlineData("1.0", 1.0)]
	[InlineData("-1.0", -1.0)]
	[InlineData("3.14", 3.14)]
	[InlineData("0.001", 0.001)]
	[InlineData("1e10", 1e10)]
	[InlineData("1.5e2", 150.0)]
	public async Task FloatLiterals(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("''", "")]
	[InlineData("'hello'", "hello")]
	[InlineData("'hello world'", "hello world")]
	[InlineData("'it''s'", "it's")]
	[InlineData("'123'", "123")]
	[InlineData("'true'", "true")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task StringLiterals(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("false", false)]
	[InlineData("TRUE", true)]
	[InlineData("FALSE", false)]
	public async Task BoolLiterals(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task NullLiteral()
	{
		(await Eval("NULL")).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Parenthesized expressions / operator precedence
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("(1)", 1L)]
	[InlineData("((1))", 1L)]
	[InlineData("(1 + 2)", 3L)]
	[InlineData("((1 + 2))", 3L)]
	[InlineData("(1 + 2) * 3", 9L)]
	[InlineData("1 + (2 * 3)", 7L)]
	[InlineData("(1 + 2) * (3 + 4)", 21L)]
	[InlineData("((1 + 2) * 3) + 4", 13L)]
	public async Task ParenthesizedExpressions(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IEEE_DIVIDE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IEEE_DIVIDE(10.0, 2.0)", 5.0)]
	[InlineData("IEEE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	[InlineData("IEEE_DIVIDE(0.0, 1.0)", 0.0)]
	[InlineData("IEEE_DIVIDE(-6.0, 2.0)", -3.0)]
	public async Task IeeeDivide_Normal(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	public async Task IeeeDivide_ByZero_ReturnsInfinity()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(1.0, 0.0)"))!;
		double.IsPositiveInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task IeeeDivide_NegativeByZero_ReturnsNegInfinity()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(-1.0, 0.0)"))!;
		double.IsNegativeInfinity(result).Should().BeTrue();
	}

	[Fact]
	public async Task IeeeDivide_ZeroByZero_ReturnsNaN()
	{
		var result = (double)(await Eval("IEEE_DIVIDE(0.0, 0.0)"))!;
		double.IsNaN(result).Should().BeTrue();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SAFE_DIVIDE(10, 2)", 5.0)]
	[InlineData("SAFE_DIVIDE(1, 3)", 0.3333333333333333)]
	[InlineData("SAFE_DIVIDE(0, 1)", 0.0)]
	public async Task SafeDivide_Normal(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		(await Eval("SAFE_DIVIDE(1, 0)")).Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_NEGATE(1)", -1L)]
	[InlineData("SAFE_NEGATE(-1)", 1L)]
	[InlineData("SAFE_NEGATE(0)", 0L)]
	public async Task SafeNegate_Normal(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_ADD(1, 2)", 3L)]
	[InlineData("SAFE_ADD(0, 0)", 0L)]
	[InlineData("SAFE_ADD(-1, 1)", 0L)]
	[InlineData("SAFE_ADD(100, -50)", 50L)]
	public async Task SafeAdd_Normal(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_SUBTRACT(5, 3)", 2L)]
	[InlineData("SAFE_SUBTRACT(3, 5)", -2L)]
	[InlineData("SAFE_SUBTRACT(0, 0)", 0L)]
	public async Task SafeSubtract_Normal(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SAFE_MULTIPLY(3, 4)", 12L)]
	[InlineData("SAFE_MULTIPLY(0, 100)", 0L)]
	[InlineData("SAFE_MULTIPLY(-2, 3)", -6L)]
	public async Task SafeMultiply_Normal(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IS_NAN / IS_INF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#is_nan
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IS_NAN(IEEE_DIVIDE(0.0, 0.0))", true)]
	[InlineData("IS_NAN(1.0)", false)]
	[InlineData("IS_NAN(0.0)", false)]
	[InlineData("IS_NAN(IEEE_DIVIDE(1.0, 0.0))", false)]
	public async Task IsNan(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IS_INF(IEEE_DIVIDE(1.0, 0.0))", true)]
	[InlineData("IS_INF(IEEE_DIVIDE(-1.0, 0.0))", true)]
	[InlineData("IS_INF(1.0)", false)]
	[InlineData("IS_INF(0.0)", false)]
	[InlineData("IS_INF(IEEE_DIVIDE(0.0, 0.0))", false)]
	public async Task IsInf(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// GREATEST / LEAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("GREATEST(1, 2, 3)", 3L)]
	[InlineData("GREATEST(3, 2, 1)", 3L)]
	[InlineData("GREATEST(1, 1, 1)", 1L)]
	[InlineData("GREATEST(-1, 0, 1)", 1L)]
	[InlineData("GREATEST(5)", 5L)]
	[InlineData("GREATEST(-100, -50, -1)", -1L)]
	public async Task Greatest_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("LEAST(1, 2, 3)", 1L)]
	[InlineData("LEAST(3, 2, 1)", 1L)]
	[InlineData("LEAST(1, 1, 1)", 1L)]
	[InlineData("LEAST(-1, 0, 1)", -1L)]
	[InlineData("LEAST(5)", 5L)]
	[InlineData("LEAST(-100, -50, -1)", -100L)]
	public async Task Least_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("GREATEST('a', 'b', 'c')", "c")]
	[InlineData("GREATEST('c', 'b', 'a')", "c")]
	[InlineData("LEAST('a', 'b', 'c')", "a")]
	[InlineData("LEAST('c', 'b', 'a')", "a")]
	public async Task GreatestLeast_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("GREATEST(1.0, 2.0, 3.0)", 3.0)]
	[InlineData("LEAST(1.0, 2.0, 3.0)", 1.0)]
	[InlineData("GREATEST(-1.5, 0.0, 1.5)", 1.5)]
	[InlineData("LEAST(-1.5, 0.0, 1.5)", -1.5)]
	public async Task GreatestLeast_Float(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("GREATEST(NULL, 1, 2)")]
	[InlineData("LEAST(NULL, 1, 2)")]
	[InlineData("GREATEST(1, NULL, 2)")]
	[InlineData("LEAST(1, NULL, 2)")]
	public async Task GreatestLeast_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SIGN function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("SIGN(10)", 1L)]
	[InlineData("SIGN(1)", 1L)]
	[InlineData("SIGN(0)", 0L)]
	[InlineData("SIGN(-1)", -1L)]
	[InlineData("SIGN(-100)", -1L)]
	public async Task Sign_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("SIGN(1.5)", 1.0)]
	[InlineData("SIGN(0.0)", 0.0)]
	[InlineData("SIGN(-0.5)", -1.0)]
	public async Task Sign_Float(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ABS function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#abs
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(0)", 0L)]
	[InlineData("ABS(1)", 1L)]
	[InlineData("ABS(-1)", 1L)]
	[InlineData("ABS(100)", 100L)]
	[InlineData("ABS(-100)", 100L)]
	[InlineData("ABS(-9223372036854775807)", 9223372036854775807L)]
	public async Task Abs_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ABS(0.0)", 0.0)]
	[InlineData("ABS(1.5)", 1.5)]
	[InlineData("ABS(-1.5)", 1.5)]
	[InlineData("ABS(-3.14)", 3.14)]
	public async Task Abs_Float(string expr, double expected)
	{
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Complex combined expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CASE WHEN 1 + 1 = 2 THEN 'math works' ELSE 'broken' END", "math works")]
	[InlineData("IF(LENGTH('hello') > 3, 'long', 'short')", "long")]
	[InlineData("IF(LENGTH('hi') > 3, 'long', 'short')", "short")]
	[InlineData("COALESCE(NULL, IF(true, 'yes', 'no'))", "yes")]
	[InlineData("CONCAT(IF(true, 'a', 'b'), IF(false, 'c', 'd'))", "ad")]
	[InlineData("CAST(IF(true, 1, 2) AS STRING)", "1")]
	public async Task CombinedExpressions_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("ABS(-1) + ABS(-2)", 3L)]
	[InlineData("GREATEST(1, 2) + LEAST(3, 4)", 5L)]
	[InlineData("IF(true, 10, 20) * 2", 20L)]
	[InlineData("COALESCE(NULL, 5) + 5", 10L)]
	[InlineData("SIGN(-5) + SIGN(5)", 0L)]
	public async Task CombinedExpressions_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Short-circuit evaluation for conditional expressions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
	//   "Conditional expressions impose constraints on the evaluation order of their inputs.
	//    In essence, they are evaluated left to right, with short-circuiting."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task If_ShortCircuits_TrueBranch()
	{
		// When condition is TRUE, else_result should NOT be evaluated
		// 1/0 would normally cause a division by zero error
		var result = await Eval("IF(TRUE, 42, 1/0)");
		result.Should().Be(42L);
	}

	[Fact]
	public async Task If_ShortCircuits_FalseBranch()
	{
		// When condition is FALSE, true_result should NOT be evaluated
		var result = await Eval("IF(FALSE, 1/0, 99)");
		result.Should().Be(99L);
	}

	[Fact]
	public async Task If_ShortCircuits_NullCondition()
	{
		// When condition is NULL, true_result should NOT be evaluated
		var result = await Eval("IF(CAST(NULL AS BOOL), 1/0, 77)");
		result.Should().Be(77L);
	}

	[Fact]
	public async Task Coalesce_ShortCircuits()
	{
		// Once a non-NULL value is found, remaining expressions should NOT be evaluated
		var result = await Eval("COALESCE(5, 1/0)");
		result.Should().Be(5L);
	}

	[Fact]
	public async Task Ifnull_ShortCircuits()
	{
		// When first arg is non-NULL, second arg should NOT be evaluated
		var result = await Eval("IFNULL(10, 1/0)");
		result.Should().Be(10L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Integer division returns FLOAT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	//   "INT64 / INT64 → FLOAT64"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Division_Int64_ReturnsFloat64()
	{
		var result = await Eval("5 / 2");
		result.Should().Be(2.5);
	}

	[Fact]
	public async Task Division_Int64_Exact()
	{
		var result = await Eval("10 / 2");
		result.Should().Be(5.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// LIKE with NULL produces an error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   "SELECT NULL LIKE 'a%'; -- Produces an error"
	//   "SELECT 'apple' LIKE NULL; -- Produces an error"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Like_NullValue_ThrowsError()
	{
		var act = () => Eval("CAST(NULL AS STRING) LIKE 'a%'");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task Like_NullPattern_ThrowsError()
	{
		var act = () => Eval("'apple' LIKE CAST(NULL AS STRING)");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// CURRENT_TIMESTAMP / CURRENT_DATE without parentheses
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#current_timestamp
	//   "CURRENT_TIMESTAMP" — no parentheses required
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CurrentTimestamp_NoParens()
	{
		var result = await Eval("CURRENT_TIMESTAMP");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	public async Task CurrentDate_NoParens()
	{
		var result = await Eval("CURRENT_DATE");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	// ═══════════════════════════════════════════════════════════════
	// LIKE ANY/ALL with NULL patterns
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LikeAny_WithNullPattern_MatchesOtherPattern()
	{
		// 'hello' matches '%' even though NULL is in pattern list
		var result = await Eval("'hello' LIKE ANY ('%', CAST(NULL AS STRING))");
		result.Should().Be(true);
	}

	[Fact]
	public async Task LikeAny_OnlyNullPatterns_ReturnsNull()
	{
		var result = await Eval("'hello' LIKE ANY (CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task LikeAll_WithNullPattern_ReturnsNull()
	{
		// Even though 'hello' matches '%', NULL pattern makes ALL return NULL
		var result = await Eval("'hello' LIKE ALL ('%', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task LikeAll_NonMatchBeforeNull_ReturnsFalse()
	{
		// 'hello' doesn't match 'xyz', so ALL short-circuits to FALSE regardless of NULL
		var result = await Eval("'hello' LIKE ALL ('xyz', CAST(NULL AS STRING))");
		result.Should().Be(false);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRUCT field access on NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#struct_field_access_operator
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StructFieldAccess_NullStruct_ReturnsNull()
	{
		// Use IF to produce a NULL struct — IF condition is false so returns NULL
		var result = await Eval("IF(FALSE, STRUCT(1 AS x), NULL).x");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// String escape sequences
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringLiteral_NewlineEscape()
	{
		var result = await Eval("LENGTH('a\\nb')");
		result.Should().Be(3L); // 'a' + newline + 'b' = 3 chars
	}

	[Fact]
	public async Task StringLiteral_TabEscape()
	{
		var result = await Eval("LENGTH('a\\tb')");
		result.Should().Be(3L); // 'a' + tab + 'b' = 3 chars
	}

	[Fact]
	public async Task StringLiteral_BackslashEscape()
	{
		var result = await Eval("LENGTH('a\\\\b')");
		result.Should().Be(3L); // 'a' + backslash + 'b' = 3 chars
	}

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT MICROSECOND/NANOSECOND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Extract_Microsecond_FromTimestamp()
	{
		var result = await Eval("EXTRACT(MICROSECOND FROM TIMESTAMP '2020-01-01 00:00:01.123456Z')");
		result.Should().Be(123456L);
	}

	[Fact]
	public async Task Extract_Nanosecond_FromTimestamp()
	{
		// .NET DateTime has 100ns precision, so 123456700 nanoseconds
		var result = await Eval("EXTRACT(NANOSECOND FROM TIMESTAMP '2020-01-01 00:00:01.1234567Z')");
		result.Should().Be(123456700L);
	}

	// ═══════════════════════════════════════════════════════════════
	// || operator array concatenation
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ConcatOperator_Arrays()
	{
		var result = await Eval("ARRAY_LENGTH([1, 2] || [3, 4])");
		result.Should().Be(4L);
	}
}
