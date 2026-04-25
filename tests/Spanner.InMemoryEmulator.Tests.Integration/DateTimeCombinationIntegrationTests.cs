using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense date/time function combination tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeCombinationIntegrationTests : IntegrationTestBase
{
	public DateTimeCombinationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE constructors and literals
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE '2024-01-01'", "2024-01-01")]
	[InlineData("DATE '2024-06-15'", "2024-06-15")]
	[InlineData("DATE '2024-12-31'", "2024-12-31")]
	[InlineData("DATE '2000-01-01'", "2000-01-01")]
	[InlineData("DATE '1999-12-31'", "1999-12-31")]
	[InlineData("DATE '2024-02-29'", "2024-02-29")]  // Leap year
	[InlineData("DATE '2023-02-28'", "2023-02-28")]  // Non-leap year
	public async Task DateLiteral_Combinations(string expr, string expected)
	{
		var result = await Eval(expr);
		var dt = (DateTime)result!;
		dt.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT from DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-06-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-06-15')", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-06-15')", 15L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-06-15')", 7L)]  // Saturday = 7
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)]  // Leap year
	[InlineData("EXTRACT(YEAR FROM DATE '2000-01-01')", 2000L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-15')", 1L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-15')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-31')", 31L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1970-01-01')", 1970L)]
	[InlineData("EXTRACT(ISOWEEK FROM DATE '2024-01-01')", 1L)]
	public async Task ExtractDate_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT from TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: default timezone is America/Los_Angeles (Jan=UTC-8, Jun=UTC-7 DST)
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-06-15T12:30:45Z')", 2024L)]  // LA: 05:30:45 Jun 15
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-06-15T12:30:45Z')", 6L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-06-15T12:30:45Z')", 15L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-06-15T12:30:45Z')", 5L)]     // 12-7=5
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-06-15T12:30:45Z')", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-06-15T12:30:45Z')", 45L)]
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2000-01-01T00:00:00Z')", 1999L)]  // LA: 1999-12-31 16:00
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-01T00:00:00Z')", 16L)]    // LA: 2023-12-31 16:00
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-01T23:59:59Z')", 15L)]    // LA: 2024-01-01 15:59:59
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-01T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-01T00:59:00Z')", 59L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-01T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-01T00:00:59Z')", 59L)]
	public async Task ExtractTimestamp_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// DATE_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 DAY)", "2024-01-02")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 30 DAY)", "2024-01-31")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 31 DAY)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 366 DAY)", "2025-01-01")]  // Leap year
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH)", "2024-02-29")]  // Clamped to end of Feb (leap year)
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 12 MONTH)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 YEAR)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 YEAR)", "2025-02-28")]  // Leap to non-leap
	[InlineData("DATE_ADD(DATE '2024-06-15', INTERVAL -1 DAY)", "2024-06-14")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL -1 DAY)", "2023-12-31")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 0 DAY)", "2024-01-01")]
	public async Task DateAdd_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_sub
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_SUB(DATE '2024-01-02', INTERVAL 1 DAY)", "2024-01-01")]
	[InlineData("DATE_SUB(DATE '2024-02-01', INTERVAL 1 DAY)", "2024-01-31")]
	[InlineData("DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY)", "2024-02-29")]  // Leap year
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 MONTH)", "2023-12-01")]
	[InlineData("DATE_SUB(DATE '2024-03-31', INTERVAL 1 MONTH)", "2024-02-29")]  // Clamped
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 YEAR)", "2023-01-01")]
	[InlineData("DATE_SUB(DATE '2024-02-29', INTERVAL 1 YEAR)", "2023-02-28")]
	[InlineData("DATE_SUB(DATE '2024-06-15', INTERVAL 0 DAY)", "2024-06-15")]
	public async Task DateSub_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_DIFF(DATE '2024-01-02', DATE '2024-01-01', DAY)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-01-02', DAY)", -1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-01-01', DAY)", 0L)]
	[InlineData("DATE_DIFF(DATE '2024-02-01', DATE '2024-01-01', DAY)", 31L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', DAY)", 366L)]  // Leap year
	[InlineData("DATE_DIFF(DATE '2024-06-01', DATE '2024-01-01', MONTH)", 5L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', MONTH)", 12L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', YEAR)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2025-01-01', YEAR)", -1L)]
	public async Task DateDiff_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 HOUR)", "2024-01-01T01:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 24 HOUR)", "2024-01-02T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 MINUTE)", "2024-01-01T00:01:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 60 MINUTE)", "2024-01-01T01:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 SECOND)", "2024-01-01T00:00:01Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 3600 SECOND)", "2024-01-01T01:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY)", "2024-01-02T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T23:59:59Z', INTERVAL 1 SECOND)", "2024-01-02T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-12-31T23:59:59Z', INTERVAL 1 SECOND)", "2025-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL -1 HOUR)", "2023-12-31T23:00:00Z")]
	public async Task TimestampAdd_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:00:00Z', INTERVAL 1 HOUR)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-02T00:00:00Z', INTERVAL 24 HOUR)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:00:00Z', INTERVAL 60 MINUTE)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:01:00Z', INTERVAL 1 MINUTE)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:01Z', INTERVAL 1 SECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY)", "2023-12-31T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 SECOND)", "2023-12-31T23:59:59Z")]
	public async Task TimestampSub_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T01:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', HOUR)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T01:00:00Z', HOUR)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:01:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MINUTE)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 0L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02T12:00:00Z', TIMESTAMP '2024-01-01T12:00:00Z', HOUR)", 24L)]
	public async Task TimestampDiff_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP constructors
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z'", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP '2024-06-15T12:30:45Z'", "2024-06-15T12:30:45Z")]
	[InlineData("TIMESTAMP '2024-12-31T23:59:59Z'", "2024-12-31T23:59:59Z")]
	[InlineData("TIMESTAMP '2000-01-01T00:00:00Z'", "2000-01-01T00:00:00Z")]
	public async Task TimestampLiteral_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', MONTH)", "2024-06-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', DAY)", "2024-06-15")]
	[InlineData("DATE_TRUNC(DATE '2024-12-31', MONTH)", "2024-12-01")]
	[InlineData("DATE_TRUNC(DATE '2024-12-31', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-01-01', MONTH)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-01-01', YEAR)", "2024-01-01")]
	public async Task DateTrunc_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: TIMESTAMP_TRUNC truncates in the default timezone (America/Los_Angeles), then converts back to UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', HOUR)", "2024-06-15T12:00:00Z")]   // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', MINUTE)", "2024-06-15T12:30:00Z")] // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', SECOND)", "2024-06-15T12:30:45Z")] // no-op
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', DAY)", "2024-06-15T07:00:00Z")]    // LA midnight Jun 15 → UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', MONTH)", "2024-06-01T07:00:00Z")]  // LA midnight Jun 1 → UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', YEAR)", "2024-01-01T08:00:00Z")]   // LA midnight Jan 1 → UTC+8
	public async Task TimestampTrunc_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// UNIX epoch functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_seconds
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:01Z')", 1L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T01:00:00Z')", 3600L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-02T00:00:00Z')", 86400L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000000L)]
	public async Task UnixEpoch_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_SECONDS / TIMESTAMP_MILLIS / TIMESTAMP_MICROS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_seconds
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_SECONDS(0)", "1970-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SECONDS(86400)", "1970-01-02T00:00:00Z")]
	[InlineData("TIMESTAMP_SECONDS(3600)", "1970-01-01T01:00:00Z")]
	[InlineData("TIMESTAMP_MILLIS(0)", "1970-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_MILLIS(1000)", "1970-01-01T00:00:01Z")]
	[InlineData("TIMESTAMP_MICROS(0)", "1970-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_MICROS(1000000)", "1970-01-01T00:00:01Z")]
	public async Task TimestampFrom_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CURRENT_DATE / CURRENT_TIMESTAMP (just verify they return non-null)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#current_date
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CurrentDate_ReturnsNonNull()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	public async Task CurrentTimestamp_ReturnsNonNull()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE comparisons
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-02'", false)]
	[InlineData("DATE '2024-01-01' < DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-02' > DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' <= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' >= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-01'", false)]
	[InlineData("DATE '2024-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2023-06-15' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	public async Task DateComparison_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP comparisons
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' = TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' = TIMESTAMP '2024-01-01T00:00:01Z'", false)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' < TIMESTAMP '2024-01-01T00:00:01Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:01Z' > TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' <= TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' >= TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' != TIMESTAMP '2024-01-01T00:00:01Z'", true)]
	public async Task TimestampComparison_Combinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CAST to/from dates
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('2024-01-01' AS DATE)", "2024-01-01")]
	[InlineData("CAST('2024-06-15' AS DATE)", "2024-06-15")]
	[InlineData("CAST('2024-12-31' AS DATE)", "2024-12-31")]
	public async Task CastToDate_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(DATE '2024-01-01' AS STRING)", "2024-01-01")]
	[InlineData("CAST(DATE '2024-06-15' AS STRING)", "2024-06-15")]
	[InlineData("CAST(DATE '2024-12-31' AS STRING)", "2024-12-31")]
	public async Task CastDateToString_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// NULL propagation through date/time functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(DAY FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(HOUR FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("DATE_ADD(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("DATE_SUB(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("DATE_DIFF(CAST(NULL AS DATE), DATE '2024-01-01', DAY)")]
	[InlineData("DATE_DIFF(DATE '2024-01-01', CAST(NULL AS DATE), DAY)")]
	[InlineData("DATE_TRUNC(CAST(NULL AS DATE), MONTH)")]
	[InlineData("TIMESTAMP_ADD(CAST(NULL AS TIMESTAMP), INTERVAL 1 HOUR)")]
	[InlineData("TIMESTAMP_SUB(CAST(NULL AS TIMESTAMP), INTERVAL 1 HOUR)")]
	[InlineData("TIMESTAMP_DIFF(CAST(NULL AS TIMESTAMP), TIMESTAMP '2024-01-01T00:00:00Z', SECOND)")]
	[InlineData("TIMESTAMP_TRUNC(CAST(NULL AS TIMESTAMP), HOUR)")]
	[InlineData("UNIX_SECONDS(CAST(NULL AS TIMESTAMP))")]
	[InlineData("UNIX_MILLIS(CAST(NULL AS TIMESTAMP))")]
	[InlineData("UNIX_MICROS(CAST(NULL AS TIMESTAMP))")]
	public async Task DateTimeFunction_NullInput_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Complex date/time pipelines
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE_ADD(DATE '2024-12-31', INTERVAL 1 DAY))", 2025L)]
	[InlineData("EXTRACT(MONTH FROM DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH))", 2L)]
	[InlineData("EXTRACT(DAY FROM DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY))", 29L)]
	[InlineData("DATE_DIFF(DATE_ADD(DATE '2024-01-01', INTERVAL 10 DAY), DATE '2024-01-01', DAY)", 10L)]
	[InlineData("DATE_DIFF(DATE_ADD(DATE '2024-01-01', INTERVAL 1 YEAR), DATE '2024-01-01', DAY)", 366L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP_ADD(TIMESTAMP '2024-01-01T23:00:00Z', INTERVAL 2 HOUR))", 17L)] // Result: 2024-01-02T01:00Z → LA: 2024-01-01 17:00
	public async Task DateTimePipeline_Combinations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// FORMAT_DATE and PARSE_DATE (if supported)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-06-15')", "2024-06-15")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-06-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-06-15')", "06")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-06-15')", "15")]
	public async Task FormatDate_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-06-15')", "2024-06-15")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-01-01')", "2024-01-01")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-12-31')", "2024-12-31")]
	public async Task ParseDate_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// FORMAT_TIMESTAMP and PARSE_TIMESTAMP (if supported)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', TIMESTAMP '2024-06-15T12:30:45Z')", "2024-06-15T12:30:45Z")]
	[InlineData("FORMAT_TIMESTAMP('%Y', TIMESTAMP '2024-06-15T12:30:45Z')", "2024")]
	[InlineData("FORMAT_TIMESTAMP('%H:%M:%S', TIMESTAMP '2024-06-15T12:30:45Z')", "12:30:45")]
	public async Task FormatTimestamp_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', '2024-06-15T12:30:45Z')", "2024-06-15T12:30:45Z")]
	public async Task ParseTimestamp_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}
}
