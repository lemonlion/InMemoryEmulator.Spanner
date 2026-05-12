using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE constructors and literals
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// EXTRACT from DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// EXTRACT from TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP constructors
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// Ref: TIMESTAMP_TRUNC truncates in the default timezone (America/Los_Angeles), then converts back to UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', HOUR)", "2024-06-15T12:00:00Z")]   // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', MINUTE)", "2024-06-15T12:30:00Z")] // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', SECOND)", "2024-06-15T12:30:45Z")] // no-op
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', DAY)", "2024-06-15T07:00:00Z")]    // LA midnight Jun 15 â†’ UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', MONTH)", "2024-06-01T07:00:00Z")]  // LA midnight Jun 1 â†’ UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', YEAR)", "2024-01-01T08:00:00Z")]   // LA midnight Jan 1 â†’ UTC+8
	// Ref: WEEK truncates to preceding Sunday in LA timezone
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', WEEK)", "2024-06-09T07:00:00Z")]   // prev Sunday Jun 9 → UTC+7
	// Ref: ISOWEEK truncates to preceding Monday in LA timezone
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', ISOWEEK)", "2024-06-10T07:00:00Z")]  // prev Monday Jun 10 → UTC+7
	// Ref: QUARTER truncates to first day of quarter in LA timezone
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', QUARTER)", "2024-04-01T07:00:00Z")]  // Q2 starts Apr 1 → UTC+7
	// Ref: ISOYEAR truncates to start of ISO year (Monday of first week with Thu in year)
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:30:45Z', ISOYEAR)", "2024-01-01T08:00:00Z")]  // ISO 2024 starts Jan 1 (Mon)
	public async Task TimestampTrunc_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UNIX epoch functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_seconds
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_SECONDS / TIMESTAMP_MILLIS / TIMESTAMP_MICROS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_seconds
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CURRENT_DATE / CURRENT_TIMESTAMP (just verify they return non-null)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#current_date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE comparisons
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP comparisons
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CAST to/from dates
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL propagation through date/time functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT_DATE and PARSE_DATE (if supported)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT_TIMESTAMP and PARSE_TIMESTAMP (if supported)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	//   Default timezone is America/Los_Angeles (UTC-7 in June PDT).
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', TIMESTAMP '2024-06-15T12:30:45Z')", "2024-06-15T05:30:45Z")]
	[InlineData("FORMAT_TIMESTAMP('%Y', TIMESTAMP '2024-06-15T12:30:45Z')", "2024")]
	[InlineData("FORMAT_TIMESTAMP('%H:%M:%S', TIMESTAMP '2024-06-15T12:30:45Z')", "05:30:45")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FormatTimestamp_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	//   Default timezone is America/Los_Angeles. June = PDT (UTC-7).
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', '2024-06-15T12:30:45Z')", "2024-06-15T19:30:45Z")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ParseTimestamp_Combinations(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}
}
