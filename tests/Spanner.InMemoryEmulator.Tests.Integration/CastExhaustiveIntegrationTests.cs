using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive CAST integration tests — covers all source/target type pairs
/// and edge‐case literal values that Spanner supports.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastExhaustiveIntegrationTests : IntegrationTestBase
{
	public CastExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── INT64 → STRING ───
	[Theory]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(1 AS STRING)", "1")]
	[InlineData("CAST(-1 AS STRING)", "-1")]
	[InlineData("CAST(9223372036854775807 AS STRING)", "9223372036854775807")]
	[InlineData("CAST(-9223372036854775808 AS STRING)", "-9223372036854775808")]
	[InlineData("CAST(100 AS STRING)", "100")]
	[InlineData("CAST(999999 AS STRING)", "999999")]
	[InlineData("CAST(-999999 AS STRING)", "-999999")]
	[InlineData("CAST(42 AS STRING)", "42")]
	[InlineData("CAST(1000000000 AS STRING)", "1000000000")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Int64_To_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRING → INT64 ───
	[Theory]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST('1' AS INT64)", 1L)]
	[InlineData("CAST('-1' AS INT64)", -1L)]
	[InlineData("CAST('100' AS INT64)", 100L)]
	[InlineData("CAST('9223372036854775807' AS INT64)", 9223372036854775807L)]
	[InlineData("CAST('-9223372036854775808' AS INT64)", -9223372036854775808L)]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('999' AS INT64)", 999L)]
	[InlineData("CAST('0000123' AS INT64)", 123L)]
	[InlineData("CAST('  5  ' AS INT64)", 5L)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task String_To_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── FLOAT64 → STRING ───
	[Theory]
	[InlineData("CAST(0.0 AS STRING)", "0")]
	[InlineData("CAST(1.5 AS STRING)", "1.5")]
	[InlineData("CAST(-1.5 AS STRING)", "-1.5")]
	[InlineData("CAST(3.14 AS STRING)", "3.14")]
	[InlineData("CAST(0.001 AS STRING)", "0.001")]
	[InlineData("CAST(1e10 AS STRING)", "10000000000")]
	[InlineData("CAST(CAST('inf' AS FLOAT64) AS STRING)", "inf")]
	[InlineData("CAST(CAST('-inf' AS FLOAT64) AS STRING)", "-inf")]
	[InlineData("CAST(CAST('nan' AS FLOAT64) AS STRING)", "nan")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Float64_To_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRING → FLOAT64 ───
	[Theory]
	[InlineData("CAST('0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('1.5' AS FLOAT64)", 1.5)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('100' AS FLOAT64)", 100.0)]
	[InlineData("CAST('1e5' AS FLOAT64)", 100000.0)]
	[InlineData("CAST('0.001' AS FLOAT64)", 0.001)]
	[InlineData("CAST('-0.5' AS FLOAT64)", -0.5)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task String_To_Float64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	// ─── BOOL → STRING ───
	[Theory]
	[InlineData("CAST(TRUE AS STRING)", "true")]
	[InlineData("CAST(FALSE AS STRING)", "false")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Bool_To_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRING → BOOL ───
	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task String_To_Bool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── INT64 → FLOAT64 ───
	[Theory]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(1 AS FLOAT64)", 1.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	[InlineData("CAST(100 AS FLOAT64)", 100.0)]
	[InlineData("CAST(9223372036854775807 AS FLOAT64)", 9.223372036854776E18)]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Int64_To_Float64(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, expected * 1e-10 + 1e-10);
	}

	// ─── FLOAT64 → INT64 (rounds away from zero) ───
	[Theory]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	[InlineData("CAST(1.0 AS INT64)", 1L)]
	[InlineData("CAST(-1.0 AS INT64)", -1L)]
	[InlineData("CAST(1.4 AS INT64)", 1L)]
	[InlineData("CAST(1.5 AS INT64)", 2L)]
	[InlineData("CAST(1.6 AS INT64)", 2L)]
	[InlineData("CAST(2.5 AS INT64)", 3L)]
	[InlineData("CAST(-0.5 AS INT64)", -1L)]
	[InlineData("CAST(-1.5 AS INT64)", -2L)]
	[InlineData("CAST(-2.5 AS INT64)", -3L)]
	[InlineData("CAST(0.49 AS INT64)", 0L)]
	[InlineData("CAST(0.51 AS INT64)", 1L)]
	[InlineData("CAST(99.9 AS INT64)", 100L)]
	[InlineData("CAST(-99.9 AS INT64)", -100L)]
	[InlineData("CAST(3.5 AS INT64)", 4L)]
	[InlineData("CAST(4.5 AS INT64)", 5L)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Float64_To_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── BOOL → INT64 ───
	[Theory]
	[InlineData("CAST(TRUE AS INT64)", 1L)]
	[InlineData("CAST(FALSE AS INT64)", 0L)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Bool_To_Int64(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── INT64 → BOOL ───
	[Theory]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(0 AS BOOL)", false)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Int64_To_Bool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── STRING → DATE ───
	[Theory]
	[InlineData("CAST('2024-01-01' AS DATE)")]
	[InlineData("CAST('2024-12-31' AS DATE)")]
	[InlineData("CAST('2000-02-29' AS DATE)")]
	[InlineData("CAST('1970-01-01' AS DATE)")]
	[InlineData("CAST('2024-06-15' AS DATE)")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task String_To_Date(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeOfType<DateTime>();
	}

	// ─── STRING → TIMESTAMP ───
	[Theory]
	[InlineData("CAST('2024-01-01T00:00:00Z' AS TIMESTAMP)")]
	[InlineData("CAST('2024-06-15T12:30:45Z' AS TIMESTAMP)")]
	[InlineData("CAST('2024-12-31T23:59:59Z' AS TIMESTAMP)")]
	[InlineData("CAST('2000-01-01T00:00:00Z' AS TIMESTAMP)")]
	[InlineData("CAST('1970-01-01T00:00:00Z' AS TIMESTAMP)")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task String_To_Timestamp(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeOfType<DateTime>();
	}

	// ─── DATE → STRING ───
	[Theory]
	[InlineData("CAST(DATE '2024-01-01' AS STRING)", "2024-01-01")]
	[InlineData("CAST(DATE '2024-12-31' AS STRING)", "2024-12-31")]
	[InlineData("CAST(DATE '2000-02-29' AS STRING)", "2000-02-29")]
	[InlineData("CAST(DATE '1970-01-01' AS STRING)", "1970-01-01")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Date_To_String(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NULL CAST ───
	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[InlineData("CAST(NULL AS BYTES)")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Cast_Null(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── SAFE_CAST returning NULL on bad conversions ───
	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	[InlineData("SAFE_CAST('not-a-date' AS DATE)")]
	[InlineData("SAFE_CAST('not-ts' AS TIMESTAMP)")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task SafeCast_Invalid_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── SAFE_CAST valid conversions ───
	[Theory]
	[InlineData("SAFE_CAST(42 AS STRING)", "42")]
	[InlineData("SAFE_CAST('42' AS INT64)", 42L)]
	[InlineData("SAFE_CAST(1.5 AS STRING)", "1.5")]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[InlineData("SAFE_CAST(TRUE AS STRING)", "true")]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task SafeCast_Valid(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Identity casts ───
	[Theory]
	[InlineData("CAST(42 AS INT64)", 42L)]
	[InlineData("CAST(1.5 AS FLOAT64)", 1.5)]
	[InlineData("CAST('hello' AS STRING)", "hello")]
	[InlineData("CAST(TRUE AS BOOL)", true)]
	[InlineData("CAST(FALSE AS BOOL)", false)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Identity_Cast(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Chained casts ───
	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(1.5 AS STRING) AS FLOAT64)", 1.5)]
	[InlineData("CAST(CAST(TRUE AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS STRING)", "42")]
	[InlineData("CAST(CAST('100' AS INT64) AS STRING)", "100")]
	[InlineData("CAST(CAST(1 AS BOOL) AS INT64)", 1L)]
	[InlineData("CAST(CAST(0 AS BOOL) AS INT64)", 0L)]
	[InlineData("CAST(CAST(TRUE AS INT64) AS BOOL)", true)]
	[InlineData("CAST(CAST(FALSE AS INT64) AS BOOL)", false)]
	[InlineData("CAST(CAST(42 AS FLOAT64) AS INT64)", 42L)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Chained_Cast(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CAST in expressions ───
	[Theory]
	[InlineData("CAST(1 AS FLOAT64) + CAST(2 AS FLOAT64)", 3.0)]
	[InlineData("CAST('10' AS INT64) * 2", 20L)]
	[InlineData("CAST(10 AS FLOAT64) / 3", 3.3333333333333335)]
	[InlineData("CAST(TRUE AS INT64) + CAST(FALSE AS INT64)", 1L)]
	[Trait(TestTraits.Category, "CastExhaustive")]
	public async Task Cast_In_Expression(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}
}
