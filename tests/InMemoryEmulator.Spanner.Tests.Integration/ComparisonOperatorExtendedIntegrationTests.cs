using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Extended tests for comparison operators, logical operators, and conditional expressions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ComparisonOperatorExtendedIntegrationTests : IntegrationTestBase
{
	public ComparisonOperatorExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ═══════════════════════════════════════════════════════════════
	// 1. = (equality)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("0 = 0", true)]
	[InlineData("-5 = -5", true)]
	[InlineData("-5 = 5", false)]
	[InlineData("9223372036854775807 = 9223372036854775807", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Equality_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 = 1.0", true)]
	[InlineData("1.5 = 1.5", true)]
	[InlineData("1.5 = 2.5", false)]
	[InlineData("-0.0 = 0.0", true)]
	[InlineData("CAST('inf' AS FLOAT64) = CAST('inf' AS FLOAT64)", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Equality_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abc' = 'abc'", true)]
	[InlineData("'abc' = 'ABC'", false)]
	[InlineData("'' = ''", true)]
	[InlineData("'hello' = 'world'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Equality_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRUE = TRUE", true)]
	[InlineData("FALSE = FALSE", true)]
	[InlineData("TRUE = FALSE", false)]
	[InlineData("FALSE = TRUE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Equality_Bool(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-02'", false)]
	[InlineData("DATE '2000-12-31' = DATE '2000-12-31'", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Equality_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 2. != (<>)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 != 2", true)]
	[InlineData("1 != 1", false)]
	[InlineData("0 != -1", true)]
	[InlineData("-5 != -5", false)]
	[InlineData("1 <> 2", true)]
	[InlineData("1 <> 1", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotEqual_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 != 2.0", true)]
	[InlineData("1.5 != 1.5", false)]
	[InlineData("3.14 <> 2.71", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotEqual_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abc' != 'def'", true)]
	[InlineData("'abc' != 'abc'", false)]
	[InlineData("'abc' <> 'ABC'", true)]
	[InlineData("'' != ''", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotEqual_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRUE != FALSE", true)]
	[InlineData("TRUE != TRUE", false)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-01'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotEqual_BoolAndDate(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 3. < (less than)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 < 2", true)]
	[InlineData("2 < 1", false)]
	[InlineData("1 < 1", false)]
	[InlineData("-10 < 0", true)]
	[InlineData("-10 < -20", false)]
	[InlineData("0 < 9223372036854775807", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessThan_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 < 2.0", true)]
	[InlineData("2.5 < 1.5", false)]
	[InlineData("1.5 < 1.5", false)]
	[InlineData("-1.5 < 0.0", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessThan_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'abd' < 'abc'", false)]
	[InlineData("'abc' < 'abc'", false)]
	[InlineData("'A' < 'a'", true)]
	[InlineData("'' < 'a'", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessThan_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-01-01' < DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-02' < DATE '2024-01-01'", false)]
	[InlineData("DATE '2024-01-01' < DATE '2024-01-01'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessThan_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 4. > (greater than)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("2 > 1", true)]
	[InlineData("1 > 2", false)]
	[InlineData("1 > 1", false)]
	[InlineData("0 > -10", true)]
	[InlineData("-20 > -10", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterThan_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("2.0 > 1.0", true)]
	[InlineData("1.5 > 2.5", false)]
	[InlineData("1.5 > 1.5", false)]
	[InlineData("0.0 > -1.5", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterThan_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abd' > 'abc'", true)]
	[InlineData("'abc' > 'abd'", false)]
	[InlineData("'abc' > 'abc'", false)]
	[InlineData("'a' > 'A'", true)]
	[InlineData("'a' > ''", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterThan_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-01-02' > DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' > DATE '2024-01-02'", false)]
	[InlineData("DATE '2024-01-01' > DATE '2024-01-01'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterThan_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 5. <= (less than or equal)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 <= 2", true)]
	[InlineData("1 <= 1", true)]
	[InlineData("2 <= 1", false)]
	[InlineData("-5 <= -5", true)]
	[InlineData("-5 <= 0", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessOrEqual_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 <= 2.0", true)]
	[InlineData("1.5 <= 1.5", true)]
	[InlineData("2.5 <= 1.5", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessOrEqual_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abc' <= 'abd'", true)]
	[InlineData("'abc' <= 'abc'", true)]
	[InlineData("'abd' <= 'abc'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessOrEqual_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-01-01' <= DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-01' <= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-02' <= DATE '2024-01-01'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task LessOrEqual_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 6. >= (greater than or equal)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("2 >= 1", true)]
	[InlineData("1 >= 1", true)]
	[InlineData("1 >= 2", false)]
	[InlineData("-5 >= -5", true)]
	[InlineData("0 >= -5", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterOrEqual_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("2.0 >= 1.0", true)]
	[InlineData("1.5 >= 1.5", true)]
	[InlineData("1.5 >= 2.5", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterOrEqual_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'abd' >= 'abc'", true)]
	[InlineData("'abc' >= 'abc'", true)]
	[InlineData("'abc' >= 'abd'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterOrEqual_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-01-02' >= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' >= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' >= DATE '2024-01-02'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task GreaterOrEqual_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 7. BETWEEN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("11 BETWEEN 1 AND 10", false)]
	[InlineData("-5 BETWEEN -10 AND 0", true)]
	[InlineData("-11 BETWEEN -10 AND 0", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Between_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.5 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("1.0 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("2.0 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("0.5 BETWEEN 1.0 AND 2.0", false)]
	[InlineData("2.5 BETWEEN 1.0 AND 2.0", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Between_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'a' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'c' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Between_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2024-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2023-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	[InlineData("DATE '2024-01-01' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2024-12-31' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Between_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 8. NOT BETWEEN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("5 NOT BETWEEN 1 AND 10", false)]
	[InlineData("0 NOT BETWEEN 1 AND 10", true)]
	[InlineData("11 NOT BETWEEN 1 AND 10", true)]
	[InlineData("1 NOT BETWEEN 1 AND 10", false)]
	[InlineData("10 NOT BETWEEN 1 AND 10", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotBetween_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'d' NOT BETWEEN 'a' AND 'c'", true)]
	[InlineData("'b' NOT BETWEEN 'a' AND 'c'", false)]
	[InlineData("'a' NOT BETWEEN 'a' AND 'c'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotBetween_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE '2023-06-15' NOT BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2024-06-15' NOT BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotBetween_Date(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 9. IN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("1 IN (1)", true)]
	[InlineData("2 IN (1)", false)]
	[InlineData("0 IN (0, -1, -2)", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task In_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("'hello' IN ('hello', 'world')", true)]
	[InlineData("'' IN ('', 'x')", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task In_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.5 IN (1.0, 1.5, 2.0)", true)]
	[InlineData("3.0 IN (1.0, 1.5, 2.0)", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task In_Float64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRUE IN (TRUE, FALSE)", true)]
	[InlineData("DATE '2024-01-01' IN (DATE '2024-01-01', DATE '2024-06-15')", true)]
	[InlineData("DATE '2024-03-01' IN (DATE '2024-01-01', DATE '2024-06-15')", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task In_BoolAndDate(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 10. NOT IN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("0 NOT IN (1, 2, 3)", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotIn_Int64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("'d' NOT IN ('a', 'b', 'c')", true)]
	[InlineData("'a' NOT IN ('a', 'b', 'c')", false)]
	[InlineData("'z' NOT IN ('x', 'y')", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotIn_String(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 11. LIKE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'hello' LIKE 'hello'", true)]
	[InlineData("'hello' LIKE 'world'", false)]
	[InlineData("'hello' LIKE 'h%'", true)]
	[InlineData("'hello' LIKE '%llo'", true)]
	[InlineData("'hello' LIKE '%ell%'", true)]
	[InlineData("'hello' LIKE '%xyz%'", false)]
	[InlineData("'hello' LIKE '%'", true)]
	[InlineData("'' LIKE '%'", true)]
	[InlineData("'' LIKE ''", true)]
	[InlineData("'hello' LIKE '_ello'", true)]
	[InlineData("'hello' LIKE 'hell_'", true)]
	[InlineData("'hello' LIKE '_____'", true)]
	[InlineData("'hello' LIKE '____'", false)]
	[InlineData("'hello' LIKE '______'", false)]
	[InlineData("'hello' LIKE 'H%'", false)]
	[InlineData("'Hello' LIKE 'hello'", false)]
	[InlineData("'abc' LIKE 'a_c'", true)]
	[InlineData("'ac' LIKE 'a_c'", false)]
	[InlineData("'abbc' LIKE 'a%c'", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Like_Patterns(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 12. NOT LIKE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'hello' NOT LIKE 'world'", true)]
	[InlineData("'hello' NOT LIKE 'hello'", false)]
	[InlineData("'hello' NOT LIKE 'h%'", false)]
	[InlineData("'hello' NOT LIKE '%xyz%'", true)]
	[InlineData("'hello' NOT LIKE '_____'", false)]
	[InlineData("'hello' NOT LIKE '____'", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NotLike_Patterns(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 13. IS NULL / IS NOT NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) IS NULL", true)]
	[InlineData("1 IS NULL", false)]
	[InlineData("CAST(NULL AS STRING) IS NULL", true)]
	[InlineData("'hello' IS NULL", false)]
	[InlineData("CAST(NULL AS BOOL) IS NULL", true)]
	[InlineData("TRUE IS NULL", false)]
	[InlineData("CAST(NULL AS FLOAT64) IS NULL", true)]
	[InlineData("1.5 IS NULL", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IsNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(NULL AS INT64) IS NOT NULL", false)]
	[InlineData("1 IS NOT NULL", true)]
	[InlineData("CAST(NULL AS STRING) IS NOT NULL", false)]
	[InlineData("'hello' IS NOT NULL", true)]
	[InlineData("CAST(NULL AS BOOL) IS NOT NULL", false)]
	[InlineData("TRUE IS NOT NULL", true)]
	[InlineData("CAST(NULL AS FLOAT64) IS NOT NULL", false)]
	[InlineData("1.5 IS NOT NULL", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IsNotNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 14. IS TRUE / IS FALSE / IS NOT TRUE / IS NOT FALSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUE IS TRUE", true)]
	[InlineData("FALSE IS TRUE", false)]
	[InlineData("TRUE IS FALSE", false)]
	[InlineData("FALSE IS FALSE", true)]
	[InlineData("TRUE IS NOT TRUE", false)]
	[InlineData("FALSE IS NOT TRUE", true)]
	[InlineData("TRUE IS NOT FALSE", true)]
	[InlineData("FALSE IS NOT FALSE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IsTrueIsFalse_NonNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(NULL AS BOOL) IS TRUE", false)]
	[InlineData("CAST(NULL AS BOOL) IS FALSE", false)]
	[InlineData("CAST(NULL AS BOOL) IS NOT TRUE", true)]
	[InlineData("CAST(NULL AS BOOL) IS NOT FALSE", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IsTrueIsFalse_Null(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 15. AND — full truth table
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	//   AND: TRUE AND TRUE → TRUE, TRUE AND FALSE → FALSE,
	//        FALSE AND anything → FALSE, TRUE AND NULL → NULL,
	//        NULL AND NULL → NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUE AND TRUE", true)]
	[InlineData("TRUE AND FALSE", false)]
	[InlineData("FALSE AND TRUE", false)]
	[InlineData("FALSE AND FALSE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task And_NonNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRUE AND CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS BOOL) AND TRUE")]
	[InlineData("CAST(NULL AS BOOL) AND CAST(NULL AS BOOL)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task And_NullResults(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("FALSE AND CAST(NULL AS BOOL)", false)]
	[InlineData("CAST(NULL AS BOOL) AND FALSE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task And_FalseShortCircuit(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 16. OR — full truth table
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	//   OR: FALSE OR FALSE → FALSE, TRUE OR anything → TRUE,
	//       FALSE OR NULL → NULL, NULL OR NULL → NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUE OR TRUE", true)]
	[InlineData("TRUE OR FALSE", true)]
	[InlineData("FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Or_NonNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("TRUE OR CAST(NULL AS BOOL)", true)]
	[InlineData("CAST(NULL AS BOOL) OR TRUE", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Or_TrueShortCircuit(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("FALSE OR CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS BOOL) OR FALSE")]
	[InlineData("CAST(NULL AS BOOL) OR CAST(NULL AS BOOL)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Or_NullResults(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 17. NOT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NOT TRUE", false)]
	[InlineData("NOT FALSE", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Not_NonNull(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Not_Null_ReturnsNull()
	{
		var result = await Eval("NOT CAST(NULL AS BOOL)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 18. IF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(TRUE, 1, 2)", 1L)]
	[InlineData("IF(FALSE, 1, 2)", 2L)]
	[InlineData("IF(1 = 1, 10, 20)", 10L)]
	[InlineData("IF(1 = 2, 10, 20)", 20L)]
	[InlineData("IF(TRUE, 100, 200)", 100L)]
	[InlineData("IF(FALSE, 100, 200)", 200L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task If_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(TRUE, 'yes', 'no')", "yes")]
	[InlineData("IF(FALSE, 'yes', 'no')", "no")]
	[InlineData("IF(1 > 0, 'positive', 'non-positive')", "positive")]
	[InlineData("IF(1 < 0, 'positive', 'non-positive')", "non-positive")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task If_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(TRUE, TRUE, FALSE)", true)]
	[InlineData("IF(FALSE, TRUE, FALSE)", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task If_Bool(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task If_NullCondition_ReturnsFalse()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
		//   If expr is NULL, the else branch is evaluated.
		var result = await Eval("IF(CAST(NULL AS BOOL), 1, 2)");
		result.Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task If_ThenNull()
	{
		var result = await Eval("IF(FALSE, 1, CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 19. IFNULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("IFNULL(5, 10)", 5L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IfNull_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("IFNULL('a', 'b')", "a")]
	[InlineData("IFNULL(CAST(NULL AS STRING), 'b')", "b")]
	[InlineData("IFNULL('', 'fallback')", "")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IfNull_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IfNull_BothNull_ReturnsNull()
	{
		var result = await Eval("IFNULL(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 20. NULLIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF(5, 10)", 5L)]
	[InlineData("NULLIF(0, 1)", 0L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullIf_Different_ReturnsFirst(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF(0, 0)")]
	[InlineData("NULLIF(-5, -5)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullIf_Equal_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("NULLIF('a', 'b')", "a")]
	[InlineData("NULLIF('hello', 'world')", "hello")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullIf_String_Different(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("NULLIF('abc', 'abc')")]
	[InlineData("NULLIF('', '')")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullIf_String_Equal_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 21. COALESCE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COALESCE(1, 2, 3)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), 2, 3)", 2L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64), 3)", 3L)]
	[InlineData("COALESCE(10, CAST(NULL AS INT64))", 10L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Coalesce_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE('a', 'b')", "a")]
	[InlineData("COALESCE(CAST(NULL AS STRING), 'b')", "b")]
	[InlineData("COALESCE(CAST(NULL AS STRING), CAST(NULL AS STRING), 'c')", "c")]
	[InlineData("COALESCE('x', CAST(NULL AS STRING), 'z')", "x")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Coalesce_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Coalesce_AllNull_ReturnsNull()
	{
		var result = await Eval("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Coalesce_SingleValue()
	{
		var result = await Eval("COALESCE(42)");
		result.Should().Be(42L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 22. CASE WHEN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case_expr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CASE WHEN TRUE THEN 1 ELSE 2 END", 1L)]
	[InlineData("CASE WHEN FALSE THEN 1 ELSE 2 END", 2L)]
	[InlineData("CASE WHEN 1 = 1 THEN 10 ELSE 20 END", 10L)]
	[InlineData("CASE WHEN 1 = 2 THEN 10 ELSE 20 END", 20L)]
	[InlineData("CASE WHEN 1 > 0 THEN 100 WHEN 1 < 0 THEN -100 ELSE 0 END", 100L)]
	[InlineData("CASE WHEN 1 < 0 THEN 100 WHEN 1 > 0 THEN -100 ELSE 0 END", -100L)]
	[InlineData("CASE WHEN FALSE THEN 100 WHEN FALSE THEN -100 ELSE 0 END", 0L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWhen_SearchedForm(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "other")]
	[InlineData("CASE 'x' WHEN 'a' THEN 'alpha' WHEN 'x' THEN 'xray' ELSE 'unknown' END", "xray")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWhen_SimpleForm(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWhen_NoElse_ReturnsNull()
	{
		var result = await Eval("CASE WHEN FALSE THEN 1 END");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWhen_FirstMatchWins()
	{
		var result = await Eval("CASE WHEN TRUE THEN 1 WHEN TRUE THEN 2 ELSE 3 END");
		result.Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWhen_NullCondition_SkipsToElse()
	{
		// NULL is not TRUE, so the WHEN clause is not matched.
		var result = await Eval("CASE WHEN CAST(NULL AS BOOL) THEN 1 ELSE 2 END");
		result.Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 23. Cross-type comparisons — INT64 vs FLOAT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "INT64 and FLOAT64 are comparable and can be compared using = etc."
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 = 1.0", true)]
	[InlineData("1 != 1.0", false)]
	[InlineData("1 < 1.5", true)]
	[InlineData("2 > 1.5", true)]
	[InlineData("1 <= 1.0", true)]
	[InlineData("1 >= 1.0", true)]
	[InlineData("2 < 1.5", false)]
	[InlineData("0 > 0.5", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CrossType_Int64VsFloat64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1.0 = 1", true)]
	[InlineData("1.5 > 1", true)]
	[InlineData("0.5 < 1", true)]
	[InlineData("2.0 >= 2", true)]
	[InlineData("2.0 <= 2", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CrossType_Float64VsInt64(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 24. NULL comparisons
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "Any comparison with NULL returns NULL."
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) = CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) != CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) < CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) > CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) <= CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) >= CAST(NULL AS INT64)")]
	[InlineData("1 = CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) = 1")]
	[InlineData("1 != CAST(NULL AS INT64)")]
	[InlineData("1 < CAST(NULL AS INT64)")]
	[InlineData("1 > CAST(NULL AS INT64)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullComparisons_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS STRING) = 'hello'")]
	[InlineData("'hello' = CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS STRING) != 'hello'")]
	[InlineData("CAST(NULL AS STRING) < 'hello'")]
	[InlineData("CAST(NULL AS STRING) > 'hello'")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullComparisons_String_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS BOOL) = TRUE")]
	[InlineData("TRUE = CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS BOOL) != FALSE")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullComparisons_Bool_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS INT64) BETWEEN 1 AND 10")]
	[InlineData("5 BETWEEN CAST(NULL AS INT64) AND 10")]
	[InlineData("5 BETWEEN 1 AND CAST(NULL AS INT64)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullComparisons_Between_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS INT64) IN (1, 2, 3)")]
	[InlineData("CAST(NULL AS STRING) IN ('a', 'b')")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullComparisons_In_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS STRING) LIKE 'hello'")]
	[InlineData("'hello' LIKE CAST(NULL AS STRING)")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   Literal NULL produces an error, but typed NULL (from CAST) returns NULL.
	//   Verified against real Cloud Spanner.
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NullComparisons_Like_ReturnNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 25. String comparison — lexicographic ordering
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "Strings are compared byte-by-byte."
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' < 'a'", false)]
	[InlineData("'a' < 'aa'", true)]
	[InlineData("'aa' < 'ab'", true)]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'Z' < 'a'", true)]
	[InlineData("'' < 'a'", true)]
	[InlineData("'a' < ''", false)]
	[InlineData("'' = ''", true)]
	[InlineData("'abc' = 'abc'", true)]
	[InlineData("'abc' > 'ab'", true)]
	[InlineData("'ab' > 'abc'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task StringComparison_Lexicographic(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 26. Boolean expressions in SELECT — complex nested boolean logic
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("(TRUE AND TRUE) OR FALSE", true)]
	[InlineData("(TRUE AND FALSE) OR TRUE", true)]
	[InlineData("(FALSE AND FALSE) OR FALSE", false)]
	[InlineData("TRUE AND (TRUE OR FALSE)", true)]
	[InlineData("FALSE AND (TRUE OR FALSE)", false)]
	[InlineData("NOT (TRUE AND FALSE)", true)]
	[InlineData("NOT (TRUE OR FALSE)", false)]
	[InlineData("NOT FALSE AND TRUE", true)]
	[InlineData("NOT TRUE OR FALSE", false)]
	[InlineData("(1 = 1) AND (2 = 2)", true)]
	[InlineData("(1 = 1) AND (2 = 3)", false)]
	[InlineData("(1 = 2) OR (3 = 3)", true)]
	[InlineData("(1 = 2) OR (3 = 4)", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NestedBooleanLogic(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("(1 < 2) AND (3 > 2) AND ('a' < 'b')", true)]
	[InlineData("(1 < 2) AND (3 > 2) AND ('a' > 'b')", false)]
	[InlineData("(1 > 2) OR (3 > 2) OR ('a' > 'b')", true)]
	[InlineData("(1 > 2) OR (3 < 2) OR ('a' > 'b')", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task MultiClauseBoolean(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(1 < 2 AND 3 > 2, 'yes', 'no')", "yes")]
	[InlineData("IF(1 > 2 OR 3 < 2, 'yes', 'no')", "no")]
	[InlineData("CASE WHEN 1 < 2 AND 3 > 2 THEN 'both' ELSE 'nope' END", "both")]
	[InlineData("CASE WHEN 1 > 2 OR 3 < 2 THEN 'either' ELSE 'neither' END", "neither")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task BooleanInConditionals(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("NOT NOT TRUE", true)]
	[InlineData("NOT NOT FALSE", false)]
	[InlineData("NOT (NOT TRUE AND FALSE)", true)]
	[InlineData("NOT (TRUE AND NOT TRUE)", true)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task DoubleNegation(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("1 IN (1, 2) AND 3 NOT IN (1, 2)", true)]
	[InlineData("1 IN (1, 2) AND 3 IN (1, 2)", false)]
	[InlineData("5 BETWEEN 1 AND 10 AND 15 NOT BETWEEN 1 AND 10", true)]
	[InlineData("'hello' LIKE 'h%' AND 'world' NOT LIKE 'h%'", true)]
	[InlineData("'hello' LIKE 'h%' AND 'world' LIKE 'h%'", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CombinedOperators(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE(CAST(NULL AS INT64), IF(TRUE, 42, 0))", 42L)]
	[InlineData("IFNULL(CAST(NULL AS INT64), CASE WHEN TRUE THEN 99 ELSE 0 END)", 99L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NestedConditionals_NonNull(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NestedConditionals_NullIfCoalesce_ReturnsNull()
	{
		// COALESCE(NULL, 5) = 5, NULLIF(5, 5) = NULL
		var result = await Eval("NULLIF(COALESCE(CAST(NULL AS INT64), 5), 5)");
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("1 = 1 AND 'a' = 'a' AND TRUE = TRUE", true)]
	[InlineData("1 = 1 AND 'a' = 'b' AND TRUE = TRUE", false)]
	[InlineData("1 = 2 OR 'a' = 'a' OR FALSE = TRUE", true)]
	[InlineData("1 = 2 OR 'a' = 'b' OR FALSE = TRUE", false)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task MixedTypeBooleanExpressions(string expr, bool expected)
	{
		var result = await Eval(expr);
		((bool)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("CASE WHEN 1 IN (1, 2, 3) THEN 'found' ELSE 'missing' END", "found")]
	[InlineData("CASE WHEN 4 IN (1, 2, 3) THEN 'found' ELSE 'missing' END", "missing")]
	[InlineData("CASE WHEN 'abc' LIKE 'a%' THEN 'match' ELSE 'no' END", "match")]
	[InlineData("CASE WHEN 'abc' LIKE 'x%' THEN 'match' ELSE 'no' END", "no")]
	[InlineData("CASE WHEN 5 BETWEEN 1 AND 10 THEN 'in range' ELSE 'out' END", "in range")]
	[InlineData("CASE WHEN 15 BETWEEN 1 AND 10 THEN 'in range' ELSE 'out' END", "out")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CaseWithComparisonOperators(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task ComplexNestedExpression()
	{
		var result = await Eval(
			"CASE " +
			"  WHEN 1 > 2 THEN 'a' " +
			"  WHEN 'hello' LIKE 'h%' AND 5 BETWEEN 1 AND 10 THEN 'b' " +
			"  ELSE 'c' " +
			"END");
		result.Should().Be("b");
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullPropagation_FullChain()
	{
		// NULL comparison → NULL, NOT NULL → NULL, NULL AND TRUE → NULL
		var result = await Eval("(CAST(NULL AS INT64) = 1) AND TRUE");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullPropagation_OrFalse()
	{
		// NULL OR FALSE → NULL
		var result = await Eval("(CAST(NULL AS INT64) = 1) OR FALSE");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullPropagation_OrTrue()
	{
		// NULL OR TRUE → TRUE (short circuit)
		var result = await Eval("(CAST(NULL AS INT64) = 1) OR TRUE");
		((bool)result!).Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task NullPropagation_AndFalse()
	{
		// NULL AND FALSE → FALSE (short circuit)
		var result = await Eval("(CAST(NULL AS INT64) = 1) AND FALSE");
		((bool)result!).Should().Be(false);
	}

	[Theory]
	[InlineData("IF(1 IS NOT NULL AND 'a' IS NOT NULL, 'all set', 'missing')", "all set")]
	[InlineData("IF(CAST(NULL AS INT64) IS NOT NULL AND 'a' IS NOT NULL, 'all set', 'missing')", "missing")]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task IsNullInConditionals(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 2), 3)", 3L)]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 3), 99)", 2L)]
	[InlineData("COALESCE(NULLIF(1, 2), NULLIF(2, 2), 99)", 1L)]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task CoalesceWithNullIf(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRUCT comparisons
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Struct_Equality_SameValues_ReturnsTrue()
	{
		var result = await Eval("STRUCT(1, 'a') = STRUCT(1, 'a')");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Struct_Equality_DifferentValues_ReturnsFalse()
	{
		var result = await Eval("STRUCT(1, 'a') = STRUCT(1, 'b')");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	public async Task Struct_NotEqual_ReturnsTrue()
	{
		var result = await Eval("STRUCT(1, 'a') != STRUCT(2, 'a')");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Array_Equality_SameElements_ReturnsTrue()
	{
		var result = await Eval("[1, 2, 3] = [1, 2, 3]");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Array_Equality_DifferentElements_ReturnsFalse()
	{
		var result = await Eval("[1, 2, 3] = [1, 2, 4]");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "ComparisonOperatorExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Array_Equality_DifferentLength_ReturnsFalse()
	{
		var result = await Eval("[1, 2] = [1, 2, 3]");
		result.Should().Be(false);
	}
}
