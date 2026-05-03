using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Edge-case tests for comparison operators, BETWEEN, IN, IS NULL/TRUE/FALSE, and three-valued logic.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ComparisonOperatorEdgeCaseIntegrationTests : IntegrationTestBase
{
	public ComparisonOperatorEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// BETWEEN operator
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
	[InlineData("11 NOT BETWEEN 1 AND 10", true)]
	public async Task Between_IntValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'a' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'c' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	public async Task Between_StringValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("NULL BETWEEN 1 AND 10")]
	[InlineData("5 BETWEEN NULL AND 10")]
	[InlineData("5 BETWEEN 1 AND NULL")]
	[InlineData("NULL BETWEEN NULL AND NULL")]
	public async Task Between_WithNull_ReturnsNull(string expr)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
		//   "If any operand is NULL, the result is NULL."
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// IN operator
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("1 NOT IN (1, 2, 3)", false)]
	[InlineData("4 NOT IN (1, 2, 3)", true)]
	public async Task In_BasicValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("NULL IN (1, 2, 3)")]
	[InlineData("1 IN (NULL, 2, 3)")]
	[InlineData("4 IN (NULL, 2, 3)")]
	public async Task In_WithNull_ReturnsNullOrBool(string expr)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
		//   NULL comparisons evaluate to NULL; result is NULL unless a match is found.
		// NULL IN (1,2,3) → NULL (no match possible)
		// 1 IN (NULL, 2, 3) → NULL (1 doesn't match 2 or 3, but 1 vs NULL is NULL)
		// 4 IN (NULL, 2, 3) → NULL (4 doesn't match 2 or 3, and 4 vs NULL is NULL)
		// Wait, actually for "1 IN (1, 2, 3)" Spanner returns TRUE because 1=1.
		// But "1 IN (NULL, 2, 3)" — 1!=NULL → NULL, 1!=2 → false, 1!=3 → false. OR of (NULL, false, false) = NULL
		// Hmm, but note: if 1 is NOT in the explicit list (NULL, 2, 3), the comparison with NULL
		// makes the overall result NULL, not FALSE.
		(await Eval(expr)).Should().BeNull();
	}

	[Fact]
	public async Task In_MatchFoundDespiteNull_ReturnsTrue()
	{
		// 1 IN (NULL, 1, 3): 1=NULL → NULL, 1=1 → true. OR is true.
		(await Eval("1 IN (NULL, 1, 3)")).Should().Be(true);
	}

	// ═══════════════════════════════════════════════════════════════
	// IS NULL / IS NOT NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_null
	// ═══════════════════════════════════════════════════════════════

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
	public async Task IsNull_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// IS TRUE / IS FALSE / IS NOT TRUE / IS NOT FALSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#is_true_is_false
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("true IS TRUE", true)]
	[InlineData("false IS TRUE", false)]
	[InlineData("NULL IS TRUE", false)]
	[InlineData("true IS FALSE", false)]
	[InlineData("false IS FALSE", true)]
	[InlineData("NULL IS FALSE", false)]
	[InlineData("true IS NOT TRUE", false)]
	[InlineData("false IS NOT TRUE", true)]
	[InlineData("NULL IS NOT TRUE", true)]
	[InlineData("true IS NOT FALSE", true)]
	[InlineData("false IS NOT FALSE", false)]
	[InlineData("NULL IS NOT FALSE", true)]
	public async Task IsTrueIsFalse_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Comparison operators with NULLs
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) = CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) != CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) < 1")]
	[InlineData("CAST(NULL AS INT64) > 1")]
	[InlineData("CAST(NULL AS INT64) <= 1")]
	[InlineData("CAST(NULL AS INT64) >= 1")]
	[InlineData("1 = CAST(NULL AS INT64)")]
	[InlineData("1 != CAST(NULL AS INT64)")]
	[InlineData("1 < CAST(NULL AS INT64)")]
	[InlineData("1 > CAST(NULL AS INT64)")]
	public async Task Comparison_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Basic comparison operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 = 1", true)]
	[InlineData("1 = 2", false)]
	[InlineData("1 != 1", false)]
	[InlineData("1 != 2", true)]
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
	public async Task Comparison_IntValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("'a' = 'a'", true)]
	[InlineData("'a' = 'b'", false)]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' < 'a'", false)]
	[InlineData("'a' != 'b'", true)]
	[InlineData("'' = ''", true)]
	[InlineData("'' < 'a'", true)]
	public async Task Comparison_StringValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("true = true", true)]
	[InlineData("true = false", false)]
	[InlineData("false = false", true)]
	[InlineData("true != false", true)]
	public async Task Comparison_BoolValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unary NOT
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NOT true", false)]
	[InlineData("NOT false", true)]
	public async Task Not_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task Not_Null_ReturnsNull()
	{
		(await Eval("NOT CAST(NULL AS BOOL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Unary minus
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("-1", -1L)]
	[InlineData("-(-1)", 1L)]
	[InlineData("-0", 0L)]
	[InlineData("-(1 + 2)", -3L)]
	public async Task UnaryMinus_IntValues(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// LIKE operator
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'abc' LIKE 'abc'", true)]
	[InlineData("'abc' LIKE 'ab%'", true)]
	[InlineData("'abc' LIKE '%bc'", true)]
	[InlineData("'abc' LIKE '%b%'", true)]
	[InlineData("'abc' LIKE 'a_c'", true)]
	[InlineData("'abc' LIKE 'a__'", true)]
	[InlineData("'abc' LIKE 'xyz'", false)]
	[InlineData("'abc' LIKE '%xyz%'", false)]
	[InlineData("'' LIKE ''", true)]
	[InlineData("'' LIKE '%'", true)]
	[InlineData("'abc' LIKE ''", false)]
	[InlineData("'abc' NOT LIKE 'abc'", false)]
	[InlineData("'abc' NOT LIKE 'xyz'", true)]
	public async Task Like_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("NULL LIKE 'abc'")]
	[InlineData("'abc' LIKE NULL")]
	[InlineData("NULL LIKE NULL")]
	public async Task Like_WithNull_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// IN with subquery-like constructs via UNNEST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	// BUG: NULL IN UNNEST returns FALSE instead of NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN UNNEST([1, 2, 3])", true)]
	[InlineData("4 IN UNNEST([1, 2, 3])", false)]
	[InlineData("1 NOT IN UNNEST([1, 2, 3])", false)]
	[InlineData("4 NOT IN UNNEST([1, 2, 3])", true)]
	public async Task InUnnest_BasicValues(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task InUnnest_NullValue_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
		//   "NULL IN UNNEST(array) returns NULL"
		//   Because NULL cannot be compared to anything.
		(await Eval("NULL IN UNNEST([1, 2, 3])")).Should().BeNull();
	}

	[Fact]
	public async Task NotInUnnest_NullValue_ReturnsNull()
	{
		// NOT NULL IN UNNEST(...) = NOT NULL = NULL
		(await Eval("NULL NOT IN UNNEST([1, 2, 3])")).Should().BeNull();
	}

	[Fact]
	public async Task InUnnest_NullArray_ReturnsNull()
	{
		// Ref: NULL array → NULL result
		(await Eval("1 IN UNNEST(CAST(NULL AS ARRAY<INT64>))")).Should().BeNull();
	}

	[Fact]
	public async Task InUnnest_EmptyArray_ReturnsFalse()
	{
		(await Eval("1 IN UNNEST([])")).Should().Be(false);
	}

	[Fact]
	public async Task InUnnest_ArrayWithNulls_MatchFound_ReturnsTrue()
	{
		(await Eval("1 IN UNNEST([NULL, 1, 3])")).Should().Be(true);
	}

	[Fact]
	public async Task InUnnest_ArrayWithNulls_NoMatch_ReturnsNull()
	{
		// 4 IN UNNEST([NULL, 1, 3]): 4!=NULL → NULL, 4!=1 → false, 4!=3 → false => OR = NULL
		(await Eval("4 IN UNNEST([NULL, 1, 3])")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// CASE expressions - additional edge cases
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CASE WHEN true THEN 1 ELSE 2 END", 1L)]
	[InlineData("CASE WHEN false THEN 1 ELSE 2 END", 2L)]
	[InlineData("CASE WHEN true THEN 1 WHEN true THEN 2 ELSE 3 END", 1L)]
	[InlineData("CASE WHEN false THEN 1 WHEN true THEN 2 ELSE 3 END", 2L)]
	[InlineData("CASE WHEN false THEN 1 WHEN false THEN 2 ELSE 3 END", 3L)]
	public async Task Case_WhenThenElse(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "a")]
	[InlineData("CASE 2 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "b")]
	[InlineData("CASE 3 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "c")]
	public async Task Case_SimpleForm(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task Case_NoMatchNoElse_ReturnsNull()
	{
		(await Eval("CASE WHEN false THEN 1 END")).Should().BeNull();
	}
}
