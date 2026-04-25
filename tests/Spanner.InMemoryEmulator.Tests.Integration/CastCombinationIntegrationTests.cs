using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive CAST and type coercion tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastCombinationIntegrationTests : IntegrationTestBase
{
	public CastCombinationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST INT64 to other types
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(1 AS STRING)", "1")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(100 AS STRING)", "100")]
	[InlineData("CAST(999999999 AS STRING)", "999999999")]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(1 AS FLOAT64)", 1.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	[InlineData("CAST(100 AS FLOAT64)", 100.0)]
	[InlineData("CAST(0 AS BOOL)", false)]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(-1 AS BOOL)", true)]
	[InlineData("CAST(42 AS BOOL)", true)]
	public async Task CastInt64_ToOtherTypes(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CAST FLOAT64 to other types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	[InlineData("CAST(1.0 AS INT64)", 1L)]
	[InlineData("CAST(1.9 AS INT64)", 2L)]
	[InlineData("CAST(-1.0 AS INT64)", -1L)]
	[InlineData("CAST(-1.9 AS INT64)", -2L)]
	[InlineData("CAST(0.5 AS INT64)", 1L)]
	[InlineData("CAST(1.5 AS INT64)", 2L)]
	[InlineData("CAST(2.5 AS INT64)", 3L)]
	[InlineData("CAST(3.5 AS INT64)", 4L)]
	public async Task CastFloat64_ToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST(3.14 AS STRING)", "3.14")]
	[InlineData("CAST(0.0 AS STRING)", "0")]
	[InlineData("CAST(-1.5 AS STRING)", "-1.5")]
	public async Task CastFloat64_ToString(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CAST STRING to other types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('1' AS INT64)", 1L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('100' AS INT64)", 100L)]
	[InlineData("CAST('999999999' AS INT64)", 999999999L)]
	public async Task CastString_ToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("CAST('0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('1.5' AS FLOAT64)", 1.5)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('1e2' AS FLOAT64)", 100.0)]
	public async Task CastString_ToFloat64(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-10);

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[InlineData("CAST('TRUE' AS BOOL)", true)]
	[InlineData("CAST('FALSE' AS BOOL)", false)]
	[InlineData("CAST('True' AS BOOL)", true)]
	[InlineData("CAST('False' AS BOOL)", false)]
	public async Task CastString_ToBool(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CAST BOOL to other types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(TRUE AS INT64)", 1L)]
	[InlineData("CAST(FALSE AS INT64)", 0L)]
	[InlineData("CAST(TRUE AS STRING)", "true")]
	[InlineData("CAST(FALSE AS STRING)", "false")]
	public async Task CastBool_ToOtherTypes(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// SAFE_CAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#safe_cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SAFE_CAST('1' AS INT64)", 1L)]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('' AS INT64)")]
	[InlineData("SAFE_CAST('1.5' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	[InlineData("SAFE_CAST('not_a_date' AS DATE)")]
	public async Task SafeCast_InvalidReturnsNull(string expr, object? expected = null) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("SAFE_CAST('123' AS INT64)", 123L)]
	[InlineData("SAFE_CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[InlineData("SAFE_CAST(42 AS STRING)", "42")]
	public async Task SafeCast_ValidReturnsValue(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Chained CASTs
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS INT64)", 42L)]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS STRING)", "42")]
	[InlineData("CAST(CAST('42' AS INT64) AS STRING)", "42")]
	[InlineData("CAST(CAST('42' AS INT64) AS FLOAT64)", 42.0)]
	[InlineData("CAST(CAST(TRUE AS INT64) AS STRING)", "1")]
	[InlineData("CAST(CAST(TRUE AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(1 AS BOOL) AS INT64)", 1L)]
	[InlineData("CAST(CAST(0 AS BOOL) AS INT64)", 0L)]
	public async Task ChainedCast_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// NULL CASTs
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[InlineData("CAST(NULL AS BYTES)")]
	[InlineData("SAFE_CAST(NULL AS INT64)")]
	[InlineData("SAFE_CAST(NULL AS STRING)")]
	public async Task CastNull_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// CAST with expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(1 + 2 AS STRING)", "3")]
	[InlineData("CAST(LENGTH('hello') AS STRING)", "5")]
	[InlineData("CAST(ABS(-42) AS STRING)", "42")]
	[InlineData("CAST(IF(TRUE, 1, 0) AS STRING)", "1")]
	[InlineData("CAST(COALESCE(CAST(NULL AS INT64), 42) AS STRING)", "42")]
	[InlineData("LENGTH(CAST(12345 AS STRING))", 5L)]
	[InlineData("ABS(CAST('-42' AS INT64))", 42L)]
	public async Task CastWithExpression_Combinations(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);
}
