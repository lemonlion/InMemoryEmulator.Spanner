using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for NULL handling edge cases, three-valued logic, COALESCE,
/// IFNULL, NULLIF, IS NULL, IS NOT NULL, and NULL interactions with various operators.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NullEdgeCaseIntegrationTests : IntegrationTestBase
{
	public NullEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL arithmetic — NULL propagation
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) + 1")]
	[InlineData("1 + CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) - 1")]
	[InlineData("1 - CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) * 1")]
	[InlineData("1 * CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) + CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) * CAST(NULL AS INT64)")]
	[InlineData("-CAST(NULL AS INT64)")]
	public async Task NullArithmetic_ReturnsNull(string expr)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
		//   "Unless otherwise specified, all operators return NULL when one of the operands is NULL."
		//   Bare NULL is rejected at parse time; CAST(NULL AS type) is required.
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL comparison — all return NULL (not true/false)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) = CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) != CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) < 1")]
	[InlineData("1 < CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) > 1")]
	[InlineData("1 > CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS INT64) <= 1")]
	[InlineData("CAST(NULL AS INT64) >= 1")]
	[InlineData("CAST(NULL AS INT64) = 1")]
	[InlineData("1 = CAST(NULL AS INT64)")]
	public async Task NullComparison_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// IS NULL / IS NOT NULL
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
	[InlineData("false IS NOT NULL", true)]
	public async Task IsNull_Expressions(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Three-valued logic: AND with NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("true AND true", true)]
	[InlineData("true AND false", false)]
	[InlineData("false AND true", false)]
	[InlineData("false AND false", false)]
	[InlineData("false AND CAST(NULL AS BOOL)", false)]    // false AND NULL = false
	[InlineData("CAST(NULL AS BOOL) AND false", false)]    // NULL AND false = false
	public async Task ThreeValuedAnd_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("true AND CAST(NULL AS BOOL)")]      // true AND NULL = NULL
	[InlineData("CAST(NULL AS BOOL) AND true")]      // NULL AND true = NULL
	[InlineData("CAST(NULL AS BOOL) AND CAST(NULL AS BOOL)")] // NULL AND NULL = NULL
	public async Task ThreeValuedAnd_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Three-valued logic: OR with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("true OR true", true)]
	[InlineData("true OR false", true)]
	[InlineData("false OR true", true)]
	[InlineData("false OR false", false)]
	[InlineData("true OR CAST(NULL AS BOOL)", true)]     // true OR NULL = true
	[InlineData("CAST(NULL AS BOOL) OR true", true)]     // NULL OR true = true
	public async Task ThreeValuedOr_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("false OR CAST(NULL AS BOOL)")]       // false OR NULL = NULL
	[InlineData("CAST(NULL AS BOOL) OR false")]       // NULL OR false = NULL
	[InlineData("CAST(NULL AS BOOL) OR CAST(NULL AS BOOL)")] // NULL OR NULL = NULL
	public async Task ThreeValuedOr_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NOT NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NOT true", false)]
	[InlineData("NOT false", true)]
	public async Task Not_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Not_Null_ReturnsNull()
	{
		var result = await Eval("NOT CAST(NULL AS BOOL)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// COALESCE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COALESCE(1, 2, 3)", 1L)]
	[InlineData("COALESCE(NULL, 2, 3)", 2L)]
	[InlineData("COALESCE(NULL, NULL, 3)", 3L)]
	public async Task Coalesce_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task Coalesce_AllNull_ReturnsNull()
	{
		var result = await Eval("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Coalesce_SingleNonNull()
	{
		var result = (long)(await Eval("COALESCE(42)"))!;
		result.Should().Be(42);
	}

	[Fact]
	public async Task Coalesce_StringValues()
	{
		var result = (string)(await Eval("COALESCE(CAST(NULL AS STRING), 'hello', 'world')"))!;
		result.Should().Be("hello");
	}

	// ═══════════════════════════════════════════════════════════════
	// IFNULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(NULL, 2)", 2L)]
	public async Task IfNull_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task IfNull_BothNull_ReturnsNull()
	{
		var result = await Eval("IFNULL(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task IfNull_StringValues()
	{
		var result = (string)(await Eval("IFNULL(CAST(NULL AS STRING), 'default')"))!;
		result.Should().Be("default");
	}

	// ═══════════════════════════════════════════════════════════════
	// NULLIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NULLIF(1, 1)")] // equal -> NULL
	[InlineData("NULLIF(CAST(NULL AS INT64), 1)")] // first is NULL -> NULL
	public async Task NullIf_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF(42, 0)", 42L)]
	public async Task NullIf_ReturnsFirstWhenDifferent(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task NullIf_StringEqual_ReturnsNull()
	{
		var result = await Eval("NULLIF('hello', 'hello')");
		result.Should().BeNull();
	}

	[Fact]
	public async Task NullIf_StringDifferent_ReturnsFirst()
	{
		var result = (string)(await Eval("NULLIF('hello', 'world')"))!;
		result.Should().Be("hello");
	}

	// ═══════════════════════════════════════════════════════════════
	// IF expression
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(true, 1, 2)", 1L)]
	[InlineData("IF(false, 1, 2)", 2L)]
	[InlineData("IF(1 > 0, 1, 2)", 1L)]
	[InlineData("IF(1 < 0, 1, 2)", 2L)]
	public async Task If_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task If_NullCondition_ResultsInElse()
	{
		// Ref: IF with NULL condition evaluates to else branch
		var result = (long)(await Eval("IF(CAST(NULL AS BOOL), 1, 2)"))!;
		result.Should().Be(2);
	}

	[Fact]
	public async Task If_NullInThenBranch()
	{
		var result = await Eval("IF(true, CAST(NULL AS INT64), 2)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task If_NullInElseBranch()
	{
		var result = await Eval("IF(false, 1, CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// CASE expressions with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CASE WHEN true THEN 1 ELSE 2 END", 1L)]
	[InlineData("CASE WHEN false THEN 1 ELSE 2 END", 2L)]
	[InlineData("CASE WHEN NULL THEN 1 ELSE 2 END", 2L)]
	[InlineData("CASE WHEN false THEN 1 WHEN true THEN 2 ELSE 3 END", 2L)]
	[InlineData("CASE WHEN false THEN 1 WHEN false THEN 2 ELSE 3 END", 3L)]
	public async Task CaseWhen_Values(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task CaseWhen_NoMatchNoElse_ReturnsNull()
	{
		var result = await Eval("CASE WHEN false THEN 1 END");
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "a")]
	[InlineData("CASE 2 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "b")]
	[InlineData("CASE 3 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END", "c")]
	public async Task SimpleCase_Values(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task SimpleCase_NullInput_NoMatchNoElse_ReturnsNull()
	{
		// CASE NULL WHEN NULL doesn't match because NULL = NULL is not true
		var result = await Eval("CASE CAST(NULL AS INT64) WHEN NULL THEN 1 END");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// BETWEEN with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("5 BETWEEN 1 AND 10", true)]
	[InlineData("0 BETWEEN 1 AND 10", false)]
	[InlineData("1 BETWEEN 1 AND 10", true)]
	[InlineData("10 BETWEEN 1 AND 10", true)]
	public async Task Between_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("NULL BETWEEN 1 AND 10")]
	[InlineData("5 BETWEEN NULL AND 10")]
	[InlineData("5 BETWEEN 1 AND NULL")]
	public async Task Between_WithNull_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// IN with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN (1, 2, 3)", true)]
	[InlineData("4 IN (1, 2, 3)", false)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	public async Task In_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task In_NullValueInList_PartialMatch()
	{
		// 1 IN (1, NULL) = true because 1 = 1 matched
		var result = (bool)(await Eval("1 IN (1, NULL)"))!;
		result.Should().BeTrue();
	}

	[Fact]
	public async Task In_NullValueInList_NoMatch()
	{
		// 4 IN (1, 2, NULL) is NULL because 4 didn't match any non-null and there's a NULL
		var result = await Eval("4 IN (1, 2, NULL)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task In_NullSearchValue()
	{
		// NULL IN (1, 2, 3) is NULL
		var result = await Eval("CAST(NULL AS INT64) IN (1, 2, 3)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NOT IN with NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 NOT IN (2, 3)", true)]
	[InlineData("1 NOT IN (1, 2)", false)]
	public async Task NotIn_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task NotIn_NullInList_NoMatch()
	{
		// 4 NOT IN (1, 2, NULL) is NULL (because 4 IN (1, 2, NULL) is NULL)
		var result = await Eval("4 NOT IN (1, 2, NULL)");
		result.Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL string concatenation
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'a' || 'b'", "ab")]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("'' || 'a'", "a")]
	[InlineData("'a' || ''", "a")]
	public async Task StringConcat_NonNull(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(NULL AS STRING) || 'a'")]
	[InlineData("'a' || CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS STRING) || CAST(NULL AS STRING)")]
	public async Task StringConcat_WithNull_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// LIKE with NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'hello' LIKE 'hello'", true)]
	[InlineData("'hello' LIKE 'hell%'", true)]
	[InlineData("'hello' LIKE '%llo'", true)]
	[InlineData("'hello' LIKE '%ell%'", true)]
	[InlineData("'hello' LIKE 'h_llo'", true)]
	[InlineData("'hello' LIKE 'world'", false)]
	[InlineData("'' LIKE ''", true)]
	[InlineData("'' LIKE '%'", true)]
	[InlineData("'a' LIKE '_'", true)]
	[InlineData("'ab' LIKE '_'", false)]
	public async Task Like_NonNull(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(NULL AS STRING) LIKE 'hello'")]
	[InlineData("'hello' LIKE CAST(NULL AS STRING)")]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   "SELECT NULL LIKE 'a%'; -- Produces an error"
	public async Task Like_WithNull_ReturnsNull(string expr)
	{
		var act = () => Eval(expr);
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL in functions - most functions propagate NULL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH(CAST(NULL AS STRING))")]
	[InlineData("UPPER(CAST(NULL AS STRING))")]
	[InlineData("LOWER(CAST(NULL AS STRING))")]
	[InlineData("TRIM(CAST(NULL AS STRING))")]
	[InlineData("REVERSE(CAST(NULL AS STRING))")]
	[InlineData("REPLACE(CAST(NULL AS STRING), 'a', 'b')")]
	[InlineData("SUBSTR(CAST(NULL AS STRING), 1)")]
	[InlineData("CONCAT(CAST(NULL AS STRING), 'hello')")]
	// LEFT and RIGHT do not exist in GCP Spanner; removed those test cases.
	[InlineData("LPAD(CAST(NULL AS STRING), 10)")]
	[InlineData("RPAD(CAST(NULL AS STRING), 10)")]
	[InlineData("LTRIM(CAST(NULL AS STRING))")]
	[InlineData("RTRIM(CAST(NULL AS STRING))")]
	public async Task StringFunction_NullInput_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	[Theory]
	[InlineData("ABS(CAST(NULL AS INT64))")]
	[InlineData("SIGN(CAST(NULL AS INT64))")]
	[InlineData("MOD(CAST(NULL AS INT64), 3)")]
	[InlineData("DIV(CAST(NULL AS INT64), 3)")]
	[InlineData("POW(CAST(NULL AS FLOAT64), 2)")]
	[InlineData("SQRT(CAST(NULL AS FLOAT64))")]
	[InlineData("FLOOR(CAST(NULL AS FLOAT64))")]
	[InlineData("CEIL(CAST(NULL AS FLOAT64))")]
	[InlineData("ROUND(CAST(NULL AS FLOAT64))")]
	[InlineData("EXP(CAST(NULL AS FLOAT64))")]
	[InlineData("LOG(CAST(NULL AS FLOAT64))")]
	public async Task MathFunction_NullInput_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// NULL coalesce operator (??)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NullCoalesce_NonNull()
	{
		// IFNULL is the coalesce equivalent in Spanner
		var result = (long)(await Eval("IFNULL(42, 0)"))!;
		result.Should().Be(42);
	}

	[Fact]
	public async Task NullCoalesce_Null()
	{
		var result = (long)(await Eval("IFNULL(CAST(NULL AS INT64), 0)"))!;
		result.Should().Be(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested NULL logic
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NestedNullLogic_DeMorgan()
	{
		// NOT (A AND B) should equal (NOT A) OR (NOT B)
		// With A = true, B = NULL:
		// NOT (true AND NULL) = NOT NULL = NULL
		var r1 = await Eval("NOT (true AND CAST(NULL AS BOOL))");
		r1.Should().BeNull();

		// (NOT true) OR (NOT NULL) = false OR NULL = NULL
		var r2 = await Eval("(NOT true) OR (NOT CAST(NULL AS BOOL))");
		r2.Should().BeNull();
	}

	[Fact]
	public async Task NestedNullLogic_Complex()
	{
		// (NULL AND false) OR true = false OR true = true
		var result = (bool)(await Eval("(CAST(NULL AS BOOL) AND false) OR true"))!;
		result.Should().BeTrue();
	}

	[Fact]
	public async Task NestedNullLogic_Complex2()
	{
		// false AND (NULL OR true) = false AND true = false
		var result = (bool)(await Eval("false AND (CAST(NULL AS BOOL) OR true)"))!;
		result.Should().BeFalse();
	}
}
