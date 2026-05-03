using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Additional CAST/conversion edge cases: type combos, SAFE_CAST, overflow, string parsing.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastConversionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public CastConversionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST INT64 <-> STRING
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(1 AS STRING)", "1")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(9223372036854775807 AS STRING)", "9223372036854775807")]
	public async Task Cast_Int64ToString(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('1' AS INT64)", 1L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('123' AS INT64)", 123L)]
	public async Task Cast_StringToInt64(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST FLOAT64 <-> STRING
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('1.5' AS FLOAT64)", 1.5)]
	[InlineData("CAST('0.0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	public async Task Cast_StringToFloat64(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST BOOL <-> STRING
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(true AS STRING)", "true")]
	[InlineData("CAST(false AS STRING)", "false")]
	public async Task Cast_BoolToString(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	public async Task Cast_StringToBool(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST INT64 <-> FLOAT64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(1 AS FLOAT64)", 1.0)]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	public async Task Cast_Int64ToFloat64(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("CAST(1.0 AS INT64)", 1L)]
	[InlineData("CAST(1.9 AS INT64)", 2L)]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	[InlineData("CAST(-1.5 AS INT64)", -2L)]
	public async Task Cast_Float64ToInt64(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST BOOL <-> INT64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(true AS INT64)", 1L)]
	[InlineData("CAST(false AS INT64)", 0L)]
	public async Task Cast_BoolToInt64(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_CAST - returns NULL instead of error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#safe_casting
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	[InlineData("SAFE_CAST('not-a-date' AS DATE)")]
	public async Task SafeCast_InvalidInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	[Theory]
	[InlineData("SAFE_CAST('123' AS INT64)", 123L)]
	[InlineData("SAFE_CAST('1.5' AS FLOAT64)", 1.5)]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	public async Task SafeCast_ValidInput_ReturnsCasted(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			Convert.ToDouble(result).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE to STRING and back
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE '2024-01-01' AS STRING)", "2024-01-01")]
	[InlineData("CAST(DATE '2024-12-31' AS STRING)", "2024-12-31")]
	[InlineData("CAST(DATE '2024-02-29' AS STRING)", "2024-02-29")]
	public async Task Cast_DateToString(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Implicit coercion in arithmetic (INT64 + FLOAT64 → FLOAT64)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 1.0", 2.0)]
	[InlineData("2 * 1.5", 3.0)]
	[InlineData("10 / 3.0", 3.3333333333333335)]
	public async Task ImplicitCoercion_IntAndFloat(string expr, double expected)
	{
		Convert.ToDouble(await Eval(expr)).Should().BeApproximately(expected, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// COALESCE type coercion
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("COALESCE(CAST(NULL AS INT64), 1)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS STRING), 'a')", "a")]
	[InlineData("COALESCE(CAST(NULL AS BOOL), true)", true)]
	public async Task Coalesce_TypeCoercion(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// IF type - both branches must match
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(true, 1, 2)", 1L)]
	[InlineData("IF(false, 1, 2)", 2L)]
	[InlineData("IF(true, 'a', 'b')", "a")]
	[InlineData("IF(false, 'a', 'b')", "b")]
	[InlineData("IF(true, true, false)", true)]
	[InlineData("IF(false, true, false)", false)]
	public async Task If_Values(string expr, object expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("IF(NULL, 1, 2)", 2L)]
	public async Task If_NullCondition_ReturnElse(string expr, long expected)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
		//   "If condition evaluates to NULL, then else_result is returned."
		(await Eval(expr)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRUCT / type literals not covered elsewhere
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('2024-01-01' AS DATE)")]
	[InlineData("DATE '2024-01-01'")]
	public async Task DateLiterals_NotNull(string expr)
	{
		(await Eval(expr)).Should().NotBeNull();
	}

	[Theory]
	[InlineData("TIMESTAMP '2024-01-01 00:00:00+00'")]
	[InlineData("CAST('2024-01-01 00:00:00+00' AS TIMESTAMP)")]
	public async Task TimestampLiterals_NotNull(string expr)
	{
		(await Eval(expr)).Should().NotBeNull();
	}
}
