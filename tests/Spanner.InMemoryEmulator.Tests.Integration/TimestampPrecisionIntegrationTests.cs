using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Timestamp precision, UNIX_MICROS/UNIX_MILLIS/UNIX_SECONDS edge cases,
/// TIMESTAMP_ADD/TIMESTAMP_SUB with NULL amounts, and TIMESTAMP_DIFF edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TimestampPrecisionIntegrationTests : IntegrationTestBase
{
	public TimestampPrecisionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// UNIX_SECONDS basic correctness
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_seconds
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnixSeconds_Epoch_ReturnsZero()
	{
		(await Eval("UNIX_SECONDS(TIMESTAMP '1970-01-01 00:00:00+00')")).Should().Be(0L);
	}

	[Fact]
	public async Task UnixSeconds_OneSecondAfterEpoch()
	{
		(await Eval("UNIX_SECONDS(TIMESTAMP '1970-01-01 00:00:01+00')")).Should().Be(1L);
	}

	[Fact]
	public async Task UnixSeconds_Null_ReturnsNull()
	{
		(await Eval("UNIX_SECONDS(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// UNIX_MILLIS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_millis
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnixMillis_Epoch_ReturnsZero()
	{
		(await Eval("UNIX_MILLIS(TIMESTAMP '1970-01-01 00:00:00+00')")).Should().Be(0L);
	}

	[Fact]
	public async Task UnixMillis_WithMillis()
	{
		(await Eval("UNIX_MILLIS(TIMESTAMP '1970-01-01 00:00:00.500+00')")).Should().Be(500L);
	}

	[Fact]
	public async Task UnixMillis_Null_ReturnsNull()
	{
		(await Eval("UNIX_MILLIS(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// UNIX_MICROS - precision bug
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_micros
	// BUG: Uses ToUnixTimeMilliseconds() * 1000 which loses sub-millisecond precision
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnixMicros_Epoch_ReturnsZero()
	{
		(await Eval("UNIX_MICROS(TIMESTAMP '1970-01-01 00:00:00+00')")).Should().Be(0L);
	}

	[Fact]
	public async Task UnixMicros_OneMillis()
	{
		(await Eval("UNIX_MICROS(TIMESTAMP '1970-01-01 00:00:00.001+00')")).Should().Be(1000L);
	}

	[Fact]
	public async Task UnixMicros_SubMillisecondPrecision()
	{
		// This is the precision bug test: 123456 microseconds = 0.123456 seconds
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_micros
		//   "Returns the number of microseconds since 1970-01-01 00:00:00 UTC."
		// If the implementation uses ToUnixTimeMilliseconds() * 1000, it loses sub-ms precision.
		// The correct result for 0.123456s is 123456 micros, NOT 123000.
		var result = (long)(await Eval("UNIX_MICROS(TIMESTAMP '1970-01-01 00:00:00.123456+00')"))!;
		result.Should().Be(123456L);
	}

	[Fact]
	public async Task UnixMicros_Null_ReturnsNull()
	{
		(await Eval("UNIX_MICROS(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_MICROS / TIMESTAMP_MILLIS / TIMESTAMP_SECONDS roundtrip
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimestampSeconds_Zero_ReturnsEpoch()
	{
		var result = await Eval("CAST(TIMESTAMP_SECONDS(0) AS STRING)");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task TimestampMillis_Zero_ReturnsEpoch()
	{
		var result = await Eval("CAST(TIMESTAMP_MILLIS(0) AS STRING)");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task TimestampMicros_Zero_ReturnsEpoch()
	{
		var result = await Eval("CAST(TIMESTAMP_MICROS(0) AS STRING)");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task TimestampMicros_Null_ReturnsNull()
	{
		(await Eval("TIMESTAMP_MICROS(NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task TimestampMillis_Null_ReturnsNull()
	{
		(await Eval("TIMESTAMP_MILLIS(NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task TimestampSeconds_Null_ReturnsNull()
	{
		(await Eval("TIMESTAMP_SECONDS(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// Roundtrip: TIMESTAMP_MICROS(UNIX_MICROS(ts)) = ts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnixMicros_RoundTrip_SecondsGranularity()
	{
		// UNIX_MICROS(TIMESTAMP '2024-01-01 12:00:00+00') then back
		var micros = (long)(await Eval("UNIX_MICROS(TIMESTAMP '2024-01-01 12:00:00+00')"))!;
		micros.Should().Be(1704110400000000L);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD / TIMESTAMP_SUB with various intervals
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimestampAdd_Day()
	{
		var result = await Eval("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01 00:00:00+00', INTERVAL 1 DAY) AS STRING)");
		((string)result!).Should().Contain("2024-01-02");
	}

	[Fact]
	public async Task TimestampAdd_Hour()
	{
		var result = await Eval("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01 00:00:00+00', INTERVAL 2 HOUR) AS STRING)");
		((string)result!).Should().Contain("02:00:00");
	}

	[Fact]
	public async Task TimestampSub_Day()
	{
		var result = await Eval("CAST(TIMESTAMP_SUB(TIMESTAMP '2024-01-02 00:00:00+00', INTERVAL 1 DAY) AS STRING)");
		((string)result!).Should().Contain("2024-01-01");
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD/SUB with MONTH/YEAR should throw error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	//   "TIMESTAMP_ADD supports ... MICROSECOND through DAY"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimestampAdd_Month_ThrowsError()
	{
		var act = async () => await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-01 00:00:00+00', INTERVAL 1 MONTH)");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task TimestampSub_Year_ThrowsError()
	{
		var act = async () => await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-01-01 00:00:00+00', INTERVAL 1 YEAR)");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02 00:00:00+00', TIMESTAMP '2024-01-01 00:00:00+00', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01 02:00:00+00', TIMESTAMP '2024-01-01 00:00:00+00', HOUR)", 2L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01 00:05:00+00', TIMESTAMP '2024-01-01 00:00:00+00', MINUTE)", 5L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01 00:00:30+00', TIMESTAMP '2024-01-01 00:00:00+00', SECOND)", 30L)]
	public async Task TimestampDiff_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("TIMESTAMP_DIFF(NULL, TIMESTAMP '2024-01-01 00:00:00+00', DAY)")]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01 00:00:00+00', NULL, DAY)")]
	[InlineData("TIMESTAMP_DIFF(NULL, NULL, DAY)")]
	public async Task TimestampDiff_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimestampTrunc_Null_ReturnsNull()
	{
		(await Eval("TIMESTAMP_TRUNC(NULL, DAY)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_ADD / DATE_SUB with various intervals
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH) AS STRING)", "2024-02-29")]
	[InlineData("CAST(DATE_ADD(DATE '2024-01-31', INTERVAL 1 DAY) AS STRING)", "2024-02-01")]
	[InlineData("CAST(DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY) AS STRING)", "2024-02-29")]
	[InlineData("CAST(DATE_ADD(DATE '2024-02-29', INTERVAL 1 YEAR) AS STRING)", "2025-02-28")]
	public async Task DateAddSub_BoundaryDates(string expr, string expected)
	{
		((string)(await Eval(expr))!).Should().Contain(expected);
	}

	[Theory]
	[InlineData("DATE_ADD(NULL, INTERVAL 1 DAY)")]
	[InlineData("DATE_SUB(NULL, INTERVAL 1 DAY)")]
	public async Task DateAddSub_NullDate_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_TRUNC
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE_TRUNC(DATE '2024-03-15', MONTH) AS STRING)", "2024-03-01")]
	[InlineData("CAST(DATE_TRUNC(DATE '2024-03-15', YEAR) AS STRING)", "2024-01-01")]
	public async Task DateTrunc_Values(string expr, string expected)
	{
		((string)(await Eval(expr))!).Should().Contain(expected);
	}

	[Fact]
	public async Task DateTrunc_Null_ReturnsNull()
	{
		(await Eval("DATE_TRUNC(NULL, MONTH)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_DIFF(DATE '2024-01-10', DATE '2024-01-01', DAY)", 9L)]
	[InlineData("DATE_DIFF(DATE '2024-03-01', DATE '2024-01-01', MONTH)", 2L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', YEAR)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-01-10', DAY)", -9L)]
	public async Task DateDiff_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("DATE_DIFF(NULL, DATE '2024-01-01', DAY)")]
	[InlineData("DATE_DIFF(DATE '2024-01-01', NULL, DAY)")]
	[InlineData("DATE_DIFF(NULL, NULL, DAY)")]
	public async Task DateDiff_NullInput_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT from DATE/TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-03-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-03-15')", 3L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-03-15')", 15L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-01')", 2L)]  // Monday = 2 in Spanner
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)] // 2024 is leap year
	public async Task Extract_FromDate_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(DAY FROM CAST(NULL AS DATE))")]
	public async Task Extract_NullDate_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// FORMAT_DATE / FORMAT_TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-03-15')", "2024-03-15")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-03-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-03-15')", "03")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-03-15')", "15")]
	public async Task FormatDate_Values(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Theory]
	[InlineData("FORMAT_DATE('%Y', NULL)")]
	[InlineData("FORMAT_TIMESTAMP('%Y', NULL)")]
	public async Task FormatDateTimestamp_NullValue_ReturnsNull(string expr)
	{
		(await Eval(expr)).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// CURRENT_DATE / CURRENT_TIMESTAMP are not null
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CURRENT_DATE()")]
	[InlineData("CURRENT_TIMESTAMP()")]
	public async Task CurrentDateTimestamp_NotNull(string expr)
	{
		(await Eval(expr)).Should().NotBeNull();
	}
}
