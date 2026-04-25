using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense tests for date/timestamp functions. Each InlineData = 1 test.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeDenseIntegrationTests : IntegrationTestBase
{
	public DateTimeDenseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT from DATE
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-01-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-15')", 1L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-15')", 15L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2000-01-01')", 2000L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-31')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-12-31')", 31L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1970-01-01')", 1970L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-06-15')", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-02-29')", 29L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-15')", 2L)] // Monday = 2
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)] // 2024 is leap year
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-01')", 0L)]
	[InlineData("EXTRACT(ISOWEEK FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(ISOYEAR FROM DATE '2024-01-01')", 2024L)]
	public async Task ExtractFromDate(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// EXTRACT from TIMESTAMP
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: default timezone is America/Los_Angeles (Jan=UTC-8)
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-01-15T10:30:45Z')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-01-15T10:30:45Z')", 1L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-01-15T10:30:45Z')", 15L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-15T10:30:45Z')", 2L)]       // 10-8=2
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-15T10:30:45Z')", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-15T10:30:45Z')", 45L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-15T00:00:00Z')", 16L)]      // LA: Jan 14 16:00
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-15T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-15T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-15T23:59:59Z')", 15L)]      // 23-8=15
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-15T23:59:59Z')", 59L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-15T23:59:59Z')", 59L)]
	public async Task ExtractFromTimestamp(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// DATE_ADD / DATE_SUB
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 DAY)", "2024-01-02")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 30 DAY)", "2024-01-31")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 31 DAY)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 12 MONTH)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 YEAR)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH)", "2024-02-29")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 YEAR)", "2025-02-28")]
	[InlineData("DATE_SUB(DATE '2024-01-15', INTERVAL 1 DAY)", "2024-01-14")]
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 DAY)", "2023-12-31")]
	[InlineData("DATE_SUB(DATE '2024-01-15', INTERVAL 1 MONTH)", "2023-12-15")]
	[InlineData("DATE_SUB(DATE '2024-01-15', INTERVAL 1 YEAR)", "2023-01-15")]
	[InlineData("DATE_SUB(DATE '2024-03-31', INTERVAL 1 MONTH)", "2024-02-29")]
	public async Task DateAddSub(string expr, string expected)
	{
		var result = await Eval(expr);
		var date = result switch
		{
			DateTime dt => dt,
			DateOnly d => d.ToDateTime(TimeOnly.MinValue),
			_ => throw new InvalidOperationException($"Unexpected type: {result?.GetType()}")
		};
		date.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE_DIFF
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_DIFF(DATE '2024-01-15', DATE '2024-01-15', DAY)", 0L)]
	[InlineData("DATE_DIFF(DATE '2024-01-16', DATE '2024-01-15', DAY)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-15', DATE '2024-01-16', DAY)", -1L)]
	[InlineData("DATE_DIFF(DATE '2024-02-01', DATE '2024-01-01', DAY)", 31L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', DAY)", 366L)]
	[InlineData("DATE_DIFF(DATE '2024-02-01', DATE '2024-01-01', MONTH)", 1L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', MONTH)", 12L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', YEAR)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE '2024-01-15', MONTH)", 5L)]
	public async Task DateDiff(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// DATE_TRUNC
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', MONTH)", "2024-06-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', DAY)", "2024-06-15")]
	[InlineData("DATE_TRUNC(DATE '2024-12-31', MONTH)", "2024-12-01")]
	[InlineData("DATE_TRUNC(DATE '2024-12-31', YEAR)", "2024-01-01")]
	public async Task DateTrunc(string expr, string expected)
	{
		var result = await Eval(expr);
		var date = result switch
		{
			DateTime dt => dt,
			DateOnly d => d.ToDateTime(TimeOnly.MinValue),
			_ => throw new InvalidOperationException($"Unexpected type: {result?.GetType()}")
		};
		date.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD / TIMESTAMP_SUB
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1 HOUR)", "2024-01-15T11:00:00")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 30 MINUTE)", "2024-01-15T10:30:00")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 30 SECOND)", "2024-01-15T10:00:30")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T23:00:00Z', INTERVAL 2 HOUR)", "2024-01-16T01:00:00")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1 DAY)", "2024-01-16T10:00:00")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1 HOUR)", "2024-01-15T09:00:00")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 30 MINUTE)", "2024-01-15T09:30:00")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T00:00:00Z', INTERVAL 1 HOUR)", "2024-01-14T23:00:00")]
	public async Task TimestampAddSub(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-ddTHH:mm:ss").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_DIFF
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T11:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', HOUR)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:30:00Z', TIMESTAMP '2024-01-15T10:00:00Z', MINUTE)", 30L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:30Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)", 30L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-16T10:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', HOUR)", 24L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)", 0L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:00Z', TIMESTAMP '2024-01-15T11:00:00Z', HOUR)", -1L)]
	public async Task TimestampDiff(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_TRUNC
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	// Ref: TIMESTAMP_TRUNC truncates in the default timezone (America/Los_Angeles), then converts back to UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T10:30:45Z', HOUR)", "2024-01-15T10:00:00")]   // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T10:30:45Z', MINUTE)", "2024-01-15T10:30:00")] // sub-hour: same UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T10:30:45Z', SECOND)", "2024-01-15T10:30:45")] // no-op
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T10:30:45Z', DAY)", "2024-01-15T08:00:00")]    // LA midnight Jan 15 → UTC+8
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T10:30:45Z', MONTH)", "2024-06-01T07:00:00")]  // LA midnight Jun 1 → UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T10:30:45Z', YEAR)", "2024-01-01T08:00:00")]   // LA midnight Jan 1 → UTC+8
	public async Task TimestampTrunc(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-ddTHH:mm:ss").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unix epoch functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:01Z')", 1L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:01:00Z')", 60L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T01:00:00Z')", 3600L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000000L)]
	public async Task UnixEpochFunctions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP from seconds/millis/micros
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP_SECONDS(0)", "1970-01-01T00:00:00")]
	[InlineData("TIMESTAMP_SECONDS(1)", "1970-01-01T00:00:01")]
	[InlineData("TIMESTAMP_SECONDS(3600)", "1970-01-01T01:00:00")]
	[InlineData("TIMESTAMP_SECONDS(86400)", "1970-01-02T00:00:00")]
	[InlineData("TIMESTAMP_MILLIS(0)", "1970-01-01T00:00:00")]
	[InlineData("TIMESTAMP_MILLIS(1000)", "1970-01-01T00:00:01")]
	[InlineData("TIMESTAMP_MICROS(0)", "1970-01-01T00:00:00")]
	[InlineData("TIMESTAMP_MICROS(1000000)", "1970-01-01T00:00:01")]
	public async Task TimestampFromEpoch(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-ddTHH:mm:ss").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE comparisons
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE '2024-01-15' = DATE '2024-01-15'", true)]
	[InlineData("DATE '2024-01-15' != DATE '2024-01-16'", true)]
	[InlineData("DATE '2024-01-15' < DATE '2024-01-16'", true)]
	[InlineData("DATE '2024-01-16' > DATE '2024-01-15'", true)]
	[InlineData("DATE '2024-01-15' <= DATE '2024-01-15'", true)]
	[InlineData("DATE '2024-01-15' >= DATE '2024-01-15'", true)]
	[InlineData("DATE '2024-01-16' < DATE '2024-01-15'", false)]
	[InlineData("DATE '2024-01-15' > DATE '2024-01-16'", false)]
	public async Task DateComparisons(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP comparisons
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TIMESTAMP '2024-01-15T10:00:00Z' = TIMESTAMP '2024-01-15T10:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-15T10:00:00Z' != TIMESTAMP '2024-01-15T11:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-15T10:00:00Z' < TIMESTAMP '2024-01-15T11:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-15T11:00:00Z' > TIMESTAMP '2024-01-15T10:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-15T10:00:00Z' <= TIMESTAMP '2024-01-15T10:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-15T10:00:00Z' >= TIMESTAMP '2024-01-15T10:00:00Z'", true)]
	public async Task TimestampComparisons(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CAST DATE / TIMESTAMP
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(DATE '2024-01-15' AS STRING)", "2024-01-15")]
	[InlineData("CAST(DATE '2000-01-01' AS STRING)", "2000-01-01")]
	[InlineData("CAST('2024-01-15' AS DATE)", "2024-01-15")]
	[InlineData("CAST('2000-01-01' AS DATE)", "2000-01-01")]
	public async Task CastDate(string expr, string expected)
	{
		var result = await Eval(expr);
		if (result is string s)
			s.Should().Be(expected);
		else
		{
			var date = result switch
			{
				DateTime dt => dt,
				DateOnly d => d.ToDateTime(TimeOnly.MinValue),
				_ => throw new InvalidOperationException($"Unexpected type: {result?.GetType()}")
			};
			date.ToString("yyyy-MM-dd").Should().Be(expected);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// FORMAT_DATE / FORMAT_TIMESTAMP
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-01-15')", "2024-01-15")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-01-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-01-15')", "01")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-01-15')", "15")]
	public async Task FormatDate(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// PARSE_DATE
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-01-15')", "2024-01-15")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2000-01-01')", "2000-01-01")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-12-31')", "2024-12-31")]
	public async Task ParseDate(string expr, string expected)
	{
		var result = await Eval(expr);
		var date = result switch
		{
			DateTime dt => dt,
			DateOnly d => d.ToDateTime(TimeOnly.MinValue),
			_ => throw new InvalidOperationException($"Unexpected type: {result?.GetType()}")
		};
		date.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Date arithmetic chains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE_ADD(DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH), INTERVAL 1 DAY)", "2024-02-02")]
	[InlineData("DATE_SUB(DATE_ADD(DATE '2024-01-15', INTERVAL 10 DAY), INTERVAL 5 DAY)", "2024-01-20")]
	[InlineData("DATE_ADD(DATE_TRUNC(DATE '2024-06-15', MONTH), INTERVAL 14 DAY)", "2024-06-15")]
	public async Task DateArithmeticChains(string expr, string expected)
	{
		var result = await Eval(expr);
		var date = result switch
		{
			DateTime dt => dt,
			DateOnly d => d.ToDateTime(TimeOnly.MinValue),
			_ => throw new InvalidOperationException($"Unexpected type: {result?.GetType()}")
		};
		date.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// CURRENT_DATE / CURRENT_TIMESTAMP
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CurrentDate_ReturnsDate()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().NotBeNull();
	}

	[Fact]
	public async Task CurrentTimestamp_ReturnsTimestamp()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	public async Task CurrentDate_ExtractYear()
	{
		var result = (long)(await Eval("EXTRACT(YEAR FROM CURRENT_DATE())"))!;
		result.Should().BeGreaterOrEqualTo(2024);
	}

	[Fact]
	public async Task CurrentTimestamp_ExtractHour()
	{
		var result = (long)(await Eval("EXTRACT(HOUR FROM CURRENT_TIMESTAMP())"))!;
		result.Should().BeInRange(0, 23);
	}

	// ═══════════════════════════════════════════════════════════════
	// Date/Timestamp in conditionals
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(DATE '2024-01-15' > DATE '2024-01-14', 'yes', 'no')", "yes")]
	[InlineData("IF(DATE '2024-01-15' < DATE '2024-01-14', 'yes', 'no')", "no")]
	[InlineData("CASE WHEN DATE '2024-01-15' >= DATE '2024-01-01' THEN 'ok' ELSE 'fail' END", "ok")]
	public async Task DateInConditionals(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);
}
