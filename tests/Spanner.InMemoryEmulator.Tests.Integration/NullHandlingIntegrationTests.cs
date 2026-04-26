using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive NULL propagation tests across all function categories and operators.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NullHandlingIntegrationTests : IntegrationTestBase
{
	public NullHandlingIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Math functions with NULL input
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("ABS(NULL)")]
	[InlineData("SIGN(NULL)")]
	[InlineData("MOD(NULL, 3)")]
	[InlineData("MOD(10, NULL)")]
	[InlineData("MOD(NULL, NULL)")]
	[InlineData("CEIL(NULL)")]
	[InlineData("CEILING(NULL)")]
	[InlineData("FLOOR(NULL)")]
	[InlineData("ROUND(NULL)")]
	[InlineData("ROUND(NULL, 2)")]
	[InlineData("ROUND(1.5, NULL)")]
	[InlineData("TRUNC(NULL)")]
	[InlineData("TRUNC(NULL, 2)")]
	[InlineData("TRUNC(1.5, NULL)")]
	[InlineData("GREATEST(NULL)")]
	[InlineData("GREATEST(NULL, NULL)")]
	[InlineData("LEAST(NULL)")]
	[InlineData("LEAST(NULL, NULL)")]
	[InlineData("DIV(NULL, 3)")]
	[InlineData("DIV(10, NULL)")]
	[InlineData("SQRT(NULL)")]
	[InlineData("POW(NULL, 2)")]
	[InlineData("POW(2, NULL)")]
	[InlineData("POWER(NULL, 2)")]
	[InlineData("EXP(NULL)")]
	[InlineData("LN(NULL)")]
	[InlineData("LOG(NULL)")]
	[InlineData("LOG(NULL, 10)")]
	[InlineData("LOG(100, NULL)")]
	[InlineData("LOG10(NULL)")]
	[InlineData("IEEE_DIVIDE(NULL, 1.0)")]
	[InlineData("IEEE_DIVIDE(1.0, NULL)")]
	[InlineData("SAFE_DIVIDE(NULL, 1)")]
	[InlineData("SAFE_DIVIDE(1, NULL)")]
	[InlineData("SAFE_NEGATE(NULL)")]
	[InlineData("SAFE_ADD(NULL, 1)")]
	[InlineData("SAFE_ADD(1, NULL)")]
	[InlineData("SAFE_SUBTRACT(NULL, 1)")]
	[InlineData("SAFE_SUBTRACT(1, NULL)")]
	[InlineData("SAFE_MULTIPLY(NULL, 1)")]
	[InlineData("SAFE_MULTIPLY(1, NULL)")]
	[InlineData("IS_NAN(NULL)")]
	[InlineData("IS_INF(NULL)")]
	public async Task MathFunctions_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Date/Time functions with NULL input
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(DAY FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("TIMESTAMP_ADD(NULL, INTERVAL 1 DAY)")]
	[InlineData("TIMESTAMP_SUB(NULL, INTERVAL 1 DAY)")]
	[InlineData("TIMESTAMP_TRUNC(NULL, DAY)")]
	[InlineData("DATE_ADD(NULL, INTERVAL 1 DAY)")]
	[InlineData("DATE_SUB(NULL, INTERVAL 1 DAY)")]
	[InlineData("DATE_TRUNC(NULL, MONTH)")]
	[InlineData("FORMAT_TIMESTAMP('%Y', NULL)")]
	[InlineData("FORMAT_DATE('%Y', NULL)")]
	[InlineData("UNIX_SECONDS(NULL)")]
	[InlineData("UNIX_MILLIS(NULL)")]
	[InlineData("UNIX_MICROS(NULL)")]
	[InlineData("TIMESTAMP_SECONDS(NULL)")]
	[InlineData("TIMESTAMP_MILLIS(NULL)")]
	[InlineData("TIMESTAMP_MICROS(NULL)")]
	public async Task DateTimeFunctions_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST with NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[InlineData("SAFE_CAST(NULL AS INT64)")]
	[InlineData("SAFE_CAST(NULL AS FLOAT64)")]
	[InlineData("SAFE_CAST(NULL AS STRING)")]
	[InlineData("SAFE_CAST(NULL AS BOOL)")]
	public async Task Cast_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Conditional functions with NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// COALESCE returns NULL only if all args are NULL
	[InlineData("COALESCE(NULL)")]
	[InlineData("COALESCE(NULL, NULL)")]
	[InlineData("COALESCE(NULL, NULL, NULL)")]
	[InlineData("COALESCE(NULL, NULL, NULL, NULL)")]
	// IF with NULL condition goes to else branch; else can be NULL
	[InlineData("IF(true, NULL, 1)")]
	[InlineData("IF(false, 1, NULL)")]
	// IFNULL both NULL
	[InlineData("IFNULL(NULL, NULL)")]
	// NULLIF when args are equal
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF('a', 'a')")]
	// CASE no match and no ELSE
	[InlineData("CASE WHEN false THEN 1 END")]
	[InlineData("CASE 99 WHEN 1 THEN 'a' WHEN 2 THEN 'b' END")]
	public async Task ConditionalFunctions_NullResult(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL in CASE WHEN conditions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Case_NullCondition_SkipsToNextWhen()
	{
		// CASE WHEN NULL THEN 1 WHEN true THEN 2 END = 2
		// NULL is not true, so the first branch is skipped
		(await Eval("CASE WHEN NULL THEN 1 WHEN true THEN 2 END")).Should().Be(2L);
	}

	[Fact]
	public async Task Case_NullValue_InSimpleCase()
	{
		// CASE NULL WHEN NULL THEN 1 ELSE 2 END = 2
		// NULL = NULL is NULL (not true), so no match
		(await Eval("CASE NULL WHEN NULL THEN 1 ELSE 2 END")).Should().Be(2L);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL in nested function calls
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("UPPER(LOWER(NULL))")]
	[InlineData("LENGTH(CONCAT(NULL, 'a'))")]
	[InlineData("ABS(SIGN(NULL))")]
	[InlineData("CAST(CAST(NULL AS STRING) AS INT64)")]
	[InlineData("SUBSTR(NULL, 1, LENGTH('abc'))")]
	[InlineData("REPLACE(UPPER(NULL), 'A', 'B')")]
	[InlineData("REVERSE(LOWER(NULL))")]
	[InlineData("TRIM(CONCAT(NULL, ' '))")]
	public async Task NestedFunctions_NullPropagation(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Three-valued logic detailed cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// NULL AND x:
	// NULL AND TRUE = NULL
	// NULL AND FALSE = FALSE
	// NULL AND NULL = NULL
	[InlineData("CAST(NULL AS BOOL) AND true")]
	[InlineData("true AND CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS BOOL) AND CAST(NULL AS BOOL)")]
	public async Task ThreeValuedLogic_NullResults(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("CAST(NULL AS BOOL) AND false", false)]
	[InlineData("false AND CAST(NULL AS BOOL)", false)]
	[InlineData("CAST(NULL AS BOOL) OR true", true)]
	[InlineData("true OR CAST(NULL AS BOOL)", true)]
	public async Task ThreeValuedLogic_DefiniteResults(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	// NULL OR FALSE = NULL, NULL OR NULL = NULL
	[InlineData("CAST(NULL AS BOOL) OR false")]
	[InlineData("false OR CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS BOOL) OR CAST(NULL AS BOOL)")]
	[InlineData("NOT CAST(NULL AS BOOL)")]
	public async Task ThreeValuedLogic_NullBooleanResults(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// COALESCE with mixed null/non-null
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("COALESCE(NULL, 1)", 1L)]
	[InlineData("COALESCE(NULL, NULL, 2)", 2L)]
	[InlineData("COALESCE(NULL, NULL, NULL, 3)", 3L)]
	[InlineData("COALESCE(1, NULL)", 1L)]
	[InlineData("COALESCE(1, 2)", 1L)]
	[InlineData("COALESCE(NULL, 1, 2)", 1L)]
	public async Task Coalesce_MixedNullNonNull_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE(NULL, 'a')", "a")]
	[InlineData("COALESCE(NULL, NULL, 'b')", "b")]
	[InlineData("COALESCE('x', NULL, 'y')", "x")]
	[InlineData("COALESCE('', NULL)", "")]
	public async Task Coalesce_MixedNullNonNull_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// IFNULL detailed combinations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("IFNULL(NULL, 42)", 42L)]
	[InlineData("IFNULL(0, 42)", 0L)]
	[InlineData("IFNULL(1, 42)", 1L)]
	[InlineData("IFNULL(-1, 42)", -1L)]
	public async Task Ifnull_Detailed_Int(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IFNULL(NULL, 'default')", "default")]
	[InlineData("IFNULL('value', 'default')", "value")]
	[InlineData("IFNULL('', 'default')", "")]
	public async Task Ifnull_Detailed_String(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IFNULL(NULL, true)", true)]
	[InlineData("IFNULL(false, true)", false)]
	[InlineData("IFNULL(true, false)", true)]
	public async Task Ifnull_Detailed_Bool(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULLIF detailed combinations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF(0, 0)")]
	[InlineData("NULLIF(-1, -1)")]
	[InlineData("NULLIF('abc', 'abc')")]
	[InlineData("NULLIF('', '')")]
	[InlineData("NULLIF(true, true)")]
	[InlineData("NULLIF(false, false)")]
	public async Task Nullif_Equal_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Fact]
	public async Task Nullif_WithNullFirst_ReturnsNull()
	{
		// NULLIF(NULL, 1) -> NULL (since first expr is NULL)
		(await Eval("NULLIF(CAST(NULL AS INT64), 1)")).Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL in BETWEEN / IN
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("NULL BETWEEN 1 AND 10")]
	[InlineData("5 BETWEEN NULL AND 10")]
	[InlineData("5 BETWEEN 1 AND NULL")]
	[InlineData("NULL BETWEEN NULL AND NULL")]
	[InlineData("NULL NOT BETWEEN 1 AND 10")]
	public async Task Between_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("NULL IN (1, 2, 3)")]
	[InlineData("NULL NOT IN (1, 2, 3)")]
	public async Task In_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

}
