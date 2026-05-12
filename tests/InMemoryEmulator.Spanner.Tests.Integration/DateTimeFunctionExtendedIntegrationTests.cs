using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Extended date/time function tests with dense InlineData coverage.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeFunctionExtendedIntegrationTests : IntegrationTestBase
{
	public DateTimeFunctionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// 1. EXTRACT(part FROM date) — various parts and dates
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("EXTRACT(YEAR FROM DATE '2020-03-15')", 2020L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1970-01-01')", 1970L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2099-12-31')", 2099L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-06-15')", 6L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-31')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-02-29')", 29L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-31')", 31L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-04-30')", 30L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-01')", 2L)]  // Monday = 2 (Sunday=1)
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-07')", 1L)]  // Sunday = 1
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-02-29')", 60L)] // Leap year, 31+29=60
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2023-03-01')", 60L)] // Non-leap year, 31+28+1=60
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2023-12-31')", 365L)]
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-01')", 0L)]       // First partial week
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-07')", 1L)]       // First Sunday starts week 1
	public async Task Extract_FromDate_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 2. DATE_ADD — various parts and values
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 DAY)", "2024-01-02")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 30 DAY)", "2024-01-31")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 31 DAY)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 366 DAY)", "2025-01-01")]  // Leap year
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH)", "2024-02-29")]  // Clamp to end of month (leap)
	[InlineData("DATE_ADD(DATE '2023-01-31', INTERVAL 1 MONTH)", "2023-02-28")]  // Clamp to end of month (non-leap)
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 YEAR)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 YEAR)", "2025-02-28")]   // Leap to non-leap clamp
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL -1 DAY)", "2023-12-31")]
	[InlineData("DATE_ADD(DATE '2024-03-01', INTERVAL -1 DAY)", "2024-02-29")]   // Leap year
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL -1 MONTH)", "2023-12-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL -1 YEAR)", "2023-01-01")]
	public async Task DateAdd_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 3. DATE_SUB — symmetric patterns to DATE_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_sub
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_SUB(DATE '2024-01-02', INTERVAL 1 DAY)", "2024-01-01")]
	[InlineData("DATE_SUB(DATE '2024-02-01', INTERVAL 1 DAY)", "2024-01-31")]
	[InlineData("DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY)", "2024-02-29")]   // Leap year
	[InlineData("DATE_SUB(DATE '2023-03-01', INTERVAL 1 DAY)", "2023-02-28")]   // Non-leap year
	[InlineData("DATE_SUB(DATE '2024-02-01', INTERVAL 1 MONTH)", "2024-01-01")]
	[InlineData("DATE_SUB(DATE '2024-03-31', INTERVAL 1 MONTH)", "2024-02-29")]  // Clamp to end of Feb (leap)
	[InlineData("DATE_SUB(DATE '2023-03-31', INTERVAL 1 MONTH)", "2023-02-28")]  // Clamp to end of Feb (non-leap)
	[InlineData("DATE_SUB(DATE '2025-01-01', INTERVAL 1 YEAR)", "2024-01-01")]
	[InlineData("DATE_SUB(DATE '2025-02-28', INTERVAL 1 YEAR)", "2024-02-28")]
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL -1 DAY)", "2024-01-02")]   // Negative sub = add
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL -1 MONTH)", "2024-02-01")]
	public async Task DateSub_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 4. DATE_DIFF — difference in various units
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_DIFF(DATE '2024-03-01', DATE '2024-02-01', DAY)", 29L)]    // Leap year Feb
	[InlineData("DATE_DIFF(DATE '2023-03-01', DATE '2023-02-01', DAY)", 28L)]    // Non-leap year Feb
	[InlineData("DATE_DIFF(DATE '2024-12-31', DATE '2024-01-01', DAY)", 365L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-12-31', DAY)", -365L)]
	[InlineData("DATE_DIFF(DATE '2024-07-01', DATE '2024-01-01', MONTH)", 6L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-07-01', MONTH)", -6L)]
	[InlineData("DATE_DIFF(DATE '2024-01-31', DATE '2024-01-01', MONTH)", 0L)]   // Same month
	[InlineData("DATE_DIFF(DATE '2030-01-01', DATE '2020-01-01', YEAR)", 10L)]
	[InlineData("DATE_DIFF(DATE '2020-01-01', DATE '2030-01-01', YEAR)", -10L)]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE '2024-06-15', DAY)", 0L)]     // Same day
	public async Task DateDiff_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 5. DATE_TRUNC — truncate to various parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', DAY)", "2024-06-15")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', MONTH)", "2024-06-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-01-01', MONTH)", "2024-01-01")]          // Already first of month

	[InlineData("DATE_TRUNC(DATE '2024-01-01', YEAR)", "2024-01-01")]           // Already first of year
	[InlineData("DATE_TRUNC(DATE '2024-02-29', MONTH)", "2024-02-01")]          // Leap day
	public async Task DateTrunc_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 6. FORMAT_DATE — various format specifiers
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-06-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-01-15')", "01")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-12-15')", "12")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-06-01')", "01")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-06-30')", "30")]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-02-29')", "2024-02-29")]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '1999-12-31')", "1999-12-31")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-01')", "Monday")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-07')", "Sunday")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-01-15')", "January")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-06-15')", "June")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-12-15')", "December")]
	[InlineData("FORMAT_DATE('%Y/%m/%d', DATE '2024-06-15')", "2024/06/15")]
	public async Task FormatDate_ReturnsExpected(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 7. PARSE_DATE — various format strings
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#parse_date
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-06-15')", "2024-06-15")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-01-01')", "2024-01-01")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-12-31')", "2024-12-31")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-02-29')", "2024-02-29")]
	[InlineData("PARSE_DATE('%Y/%m/%d', '2024/06/15')", "2024-06-15")]
	[InlineData("PARSE_DATE('%m-%d-%Y', '06-15-2024')", "2024-06-15")]
	[InlineData("PARSE_DATE('%d/%m/%Y', '15/06/2024')", "2024-06-15")]
	[InlineData("PARSE_DATE('%Y%m%d', '20240615')", "2024-06-15")]
	public async Task ParseDate_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 8. CURRENT_DATE() — verify non-null and type
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#current_date
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	public async Task CurrentDate_ReturnsNonNullDate()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CurrentDate_ReturnsReasonableDate()
	{
		var result = (DateTime)(await Eval("CURRENT_DATE()"))!;
		result.Year.Should().BeGreaterOrEqualTo(2024);
	}

	// ═══════════════════════════════════════════════════════════════
	// 9. TIMESTAMP_ADD — add various interval parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 MICROSECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1000 MICROSECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1000000 MICROSECOND)", "2024-01-01T00:00:01Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 MILLISECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1000 MILLISECOND)", "2024-01-01T00:00:01Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 SECOND)", "2024-01-01T00:00:01Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 90 SECOND)", "2024-01-01T00:01:30Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 MINUTE)", "2024-01-01T00:01:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 90 MINUTE)", "2024-01-01T01:30:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 HOUR)", "2024-01-01T01:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 48 HOUR)", "2024-01-03T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY)", "2024-01-02T00:00:00Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 365 DAY)", "2024-12-31T00:00:00Z")]  // Leap year
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL -1 SECOND)", "2023-12-31T23:59:59Z")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL -1 DAY)", "2023-12-31T00:00:00Z")]
	public async Task TimestampAdd_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 10. TIMESTAMP_SUB — subtract various interval parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:01Z', INTERVAL 1000000 MICROSECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:01Z', INTERVAL 1000 MILLISECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:01Z', INTERVAL 1 SECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:01:30Z', INTERVAL 90 SECOND)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:01:00Z', INTERVAL 1 MINUTE)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:30:00Z', INTERVAL 90 MINUTE)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:00:00Z', INTERVAL 1 HOUR)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-03T00:00:00Z', INTERVAL 48 HOUR)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-02T00:00:00Z', INTERVAL 1 DAY)", "2024-01-01T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 SECOND)", "2023-12-31T23:59:59Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY)", "2023-12-31T00:00:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL -1 HOUR)", "2024-01-01T01:00:00Z")]  // Negative sub = add
	public async Task TimestampSub_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 11. TIMESTAMP_DIFF — difference in various units
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:01Z', SECOND)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 0L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:01:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MINUTE)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:01:00Z', MINUTE)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T01:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', HOUR)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T01:00:00Z', HOUR)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-02T00:00:00Z', DAY)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T12:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MINUTE)", 720L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-08T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', HOUR)", 168L)]  // 7 days
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', MILLISECOND)", 1000L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', MICROSECOND)", 1000000L)]
	public async Task TimestampDiff_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 12. TIMESTAMP_TRUNC — truncate to various parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	// Sub-hour truncations preserve UTC directly
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', SECOND)", "2024-06-15T12:34:56Z")]
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', MINUTE)", "2024-06-15T12:34:00Z")]
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', HOUR)", "2024-06-15T12:00:00Z")]
	// DAY/MONTH/YEAR truncation uses default timezone (America/Los_Angeles), then converts back to UTC
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', DAY)", "2024-06-15T07:00:00Z")]    // LA midnight Jun 15 → UTC+7
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T12:34:56Z', DAY)", "2024-01-15T08:00:00Z")]    // LA midnight Jan 15 → UTC+8
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', MONTH)", "2024-06-01T07:00:00Z")]  // LA midnight Jun 1
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-01-15T12:34:56Z', MONTH)", "2024-01-01T08:00:00Z")]  // LA midnight Jan 1
	[InlineData("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T12:34:56Z', YEAR)", "2024-01-01T08:00:00Z")]   // LA midnight Jan 1
	public async Task TimestampTrunc_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 13. FORMAT_TIMESTAMP — various format strings
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	//   Default timezone is America/Los_Angeles (UTC-7 in June PDT, UTC-8 in Jan PST).
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', TIMESTAMP '2024-06-15T12:30:45Z')", "2024-06-15T05:30:45Z")]
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%d', TIMESTAMP '2024-06-15T12:30:45Z')", "2024-06-15")]
	[InlineData("FORMAT_TIMESTAMP('%H:%M:%S', TIMESTAMP '2024-06-15T12:30:45Z')", "05:30:45")]
	[InlineData("FORMAT_TIMESTAMP('%Y', TIMESTAMP '2024-01-01T00:00:00Z')", "2023")]
	[InlineData("FORMAT_TIMESTAMP('%m', TIMESTAMP '2024-06-15T12:30:45Z')", "06")]
	[InlineData("FORMAT_TIMESTAMP('%d', TIMESTAMP '2024-06-15T12:30:45Z')", "15")]
	[InlineData("FORMAT_TIMESTAMP('%H', TIMESTAMP '2024-06-15T12:30:45Z')", "05")]
	[InlineData("FORMAT_TIMESTAMP('%M', TIMESTAMP '2024-06-15T12:30:45Z')", "30")]
	[InlineData("FORMAT_TIMESTAMP('%S', TIMESTAMP '2024-06-15T12:30:45Z')", "45")]
	public async Task FormatTimestamp_ReturnsExpected(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 14. PARSE_TIMESTAMP — various format strings
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	//   Default timezone is America/Los_Angeles. Literal Z in format is not a timezone indicator.
	//   June = PDT (UTC-7), January/December = PST (UTC-8).
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', '2024-06-15T12:30:45Z')", "2024-06-15T19:30:45Z")]
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', '2024-01-01T00:00:00Z')", "2024-01-01T08:00:00Z")]
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%dT%H:%M:%SZ', '2024-12-31T23:59:59Z')", "2025-01-01T07:59:59Z")]
	[InlineData("PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', '2024-06-15 12:30:45')", "2024-06-15T19:30:45Z")]
	public async Task ParseTimestamp_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 15. CURRENT_TIMESTAMP() — verify non-null
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#current_timestamp
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	public async Task CurrentTimestamp_ReturnsNonNull()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().NotBeNull();
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	public async Task CurrentTimestamp_IsRecent()
	{
		var result = (DateTime)(await Eval("CURRENT_TIMESTAMP()"))!;
		result.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
	}

	// ═══════════════════════════════════════════════════════════════
	// 16. EXTRACT from TIMESTAMP — various parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	// Default timezone: America/Los_Angeles
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	// June: LA=UTC-7; Jan: LA=UTC-8
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-06-15T12:30:45Z')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-06-15T12:30:45Z')", 6L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-06-15T12:30:45Z')", 15L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-06-15T12:30:45Z')", 5L)]       // 12-7=5
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-06-15T12:30:45Z')", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-06-15T12:30:45Z')", 45L)]
	[InlineData("EXTRACT(MILLISECOND FROM TIMESTAMP '2024-06-15T12:30:45Z')", 0L)]
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2000-01-01T00:00:00Z')", 1999L)]    // LA: 1999-12-31 16:00
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2000-01-01T00:00:00Z')", 12L)]     // LA: Dec
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2000-01-01T00:00:00Z')", 31L)]       // LA: Dec 31
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-01T08:00:00Z')", 0L)]       // LA: midnight Jan 1
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-01T20:30:00Z')", 12L)]      // LA: 12:30 PM Jan 1
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-01T00:45:00Z')", 45L)]    // LA: 4:45 PM Dec 31
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-01T00:00:30Z')", 30L)]    // LA: 4:00:30 PM Dec 31
	public async Task ExtractTimestamp_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 17. DATE from TIMESTAMP — CAST(TIMESTAMP AS DATE)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	// Note: CAST to DATE uses default timezone America/Los_Angeles
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("CAST(TIMESTAMP '2024-06-15T12:00:00Z' AS DATE)", "2024-06-15")]    // LA: Jun 15 05:00
	[InlineData("CAST(TIMESTAMP '2024-01-01T08:00:00Z' AS DATE)", "2024-01-01")]    // LA: Jan 1 00:00
	[InlineData("CAST(TIMESTAMP '2024-02-29T12:00:00Z' AS DATE)", "2024-02-29")]    // Leap day
	[InlineData("CAST(TIMESTAMP '2024-12-31T23:59:59Z' AS DATE)", "2024-12-31")]    // LA: Dec 31 15:59
	public async Task CastTimestampToDate_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// 18. Boundary dates — edge cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	// Leap year boundary
	[InlineData("DATE_ADD(DATE '2024-02-28', INTERVAL 1 DAY)", "2024-02-29")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 DAY)", "2024-03-01")]
	[InlineData("DATE_ADD(DATE '2023-02-28', INTERVAL 1 DAY)", "2023-03-01")]  // Non-leap year
	// Month boundary
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 DAY)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-04-30', INTERVAL 1 DAY)", "2024-05-01")]
	// Year boundary
	[InlineData("DATE_ADD(DATE '2024-12-31', INTERVAL 1 DAY)", "2025-01-01")]
	[InlineData("DATE_SUB(DATE '2025-01-01', INTERVAL 1 DAY)", "2024-12-31")]
	// Month-end clamping: adding 1 month from end-of-month
	[InlineData("DATE_ADD(DATE '2024-05-31', INTERVAL 1 MONTH)", "2024-06-30")]  // 31→30 clamp
	[InlineData("DATE_ADD(DATE '2024-08-31', INTERVAL 1 MONTH)", "2024-09-30")]  // 31→30 clamp
	public async Task BoundaryDates_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	// Leap year checks via EXTRACT
	[InlineData("EXTRACT(DAY FROM DATE '2024-02-29')", 29L)]    // Valid leap day
	[InlineData("EXTRACT(MONTH FROM DATE '2024-02-29')", 2L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-03-01')", 61L)]  // After leap day
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2023-03-01')", 60L)]  // No leap day
	public async Task BoundaryDates_Extract_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// 19. NULL propagation for date/time functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	// EXTRACT with NULL
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(DAY FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(DAYOFWEEK FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(DAYOFYEAR FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(DAY FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(HOUR FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(MINUTE FROM CAST(NULL AS TIMESTAMP))")]
	[InlineData("EXTRACT(SECOND FROM CAST(NULL AS TIMESTAMP))")]
	// DATE_ADD / DATE_SUB with NULL
	[InlineData("DATE_ADD(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("DATE_ADD(CAST(NULL AS DATE), INTERVAL 1 MONTH)")]
	[InlineData("DATE_SUB(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("DATE_SUB(CAST(NULL AS DATE), INTERVAL 1 YEAR)")]
	// DATE_DIFF with NULL
	[InlineData("DATE_DIFF(CAST(NULL AS DATE), DATE '2024-01-01', DAY)")]
	[InlineData("DATE_DIFF(DATE '2024-01-01', CAST(NULL AS DATE), DAY)")]
	[InlineData("DATE_DIFF(CAST(NULL AS DATE), CAST(NULL AS DATE), DAY)")]
	// DATE_TRUNC with NULL
	[InlineData("DATE_TRUNC(CAST(NULL AS DATE), MONTH)")]
	[InlineData("DATE_TRUNC(CAST(NULL AS DATE), YEAR)")]
	// TIMESTAMP functions with NULL
	[InlineData("TIMESTAMP_ADD(CAST(NULL AS TIMESTAMP), INTERVAL 1 SECOND)")]
	[InlineData("TIMESTAMP_ADD(CAST(NULL AS TIMESTAMP), INTERVAL 1 HOUR)")]
	[InlineData("TIMESTAMP_SUB(CAST(NULL AS TIMESTAMP), INTERVAL 1 SECOND)")]
	[InlineData("TIMESTAMP_SUB(CAST(NULL AS TIMESTAMP), INTERVAL 1 DAY)")]
	[InlineData("TIMESTAMP_DIFF(CAST(NULL AS TIMESTAMP), TIMESTAMP '2024-01-01T00:00:00Z', SECOND)")]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', CAST(NULL AS TIMESTAMP), SECOND)")]
	[InlineData("TIMESTAMP_TRUNC(CAST(NULL AS TIMESTAMP), HOUR)")]
	[InlineData("TIMESTAMP_TRUNC(CAST(NULL AS TIMESTAMP), DAY)")]
	// FORMAT / PARSE with NULL
	[InlineData("FORMAT_DATE('%Y-%m-%d', CAST(NULL AS DATE))")]
	public async Task NullPropagation_ReturnsNull(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Additional: TIMESTAMP_ADD/SUB with MICROSECOND/MILLISECOND precision
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 500 MILLISECOND), TIMESTAMP '2024-01-01T00:00:00Z', MILLISECOND)", 500L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 500000 MICROSECOND), TIMESTAMP '2024-01-01T00:00:00Z', MICROSECOND)", 500000L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:01Z', INTERVAL 500 MILLISECOND), TIMESTAMP '2024-01-01T00:00:00Z', MILLISECOND)", 500L)]
	public async Task TimestampAddSub_SubSecondPrecision_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Additional: EXTRACT(WEEK) and EXTRACT(ISOWEEK)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// WEEK: Sunday-based, 0 for dates before first Sunday
	// ISOWEEK: ISO 8601, Monday-based
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-06')", 0L)]     // Saturday before first Sunday
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-07')", 1L)]     // First Sunday
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-13')", 1L)]     // Saturday of week 1
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-14')", 2L)]     // Second Sunday
	[InlineData("EXTRACT(ISOWEEK FROM DATE '2024-01-01')", 1L)]  // ISO week 1
	[InlineData("EXTRACT(ISOWEEK FROM DATE '2024-12-30')", 1L)]  // ISO week 1 of 2025
	public async Task ExtractWeek_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Additional: DATE arithmetic chaining
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_ADD(DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH), INTERVAL 1 DAY)", "2024-02-02")]
	[InlineData("DATE_SUB(DATE_ADD(DATE '2024-06-15', INTERVAL 1 YEAR), INTERVAL 6 MONTH)", "2024-12-15")]
	public async Task DateArithmetic_Chaining_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional: TIMESTAMP arithmetic chaining
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("TIMESTAMP_ADD(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 HOUR), INTERVAL 30 MINUTE)", "2024-01-01T01:30:00Z")]
	[InlineData("TIMESTAMP_SUB(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T12:00:00Z', INTERVAL 6 HOUR), INTERVAL 3 HOUR)", "2024-01-01T15:00:00Z")]
	public async Task TimestampArithmetic_Chaining_ReturnsExpected(string expr, string expected)
	{
		var result = (DateTime)(await Eval(expr))!;
		result.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional: DATE_DIFF and DATE_TRUNC combined
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE_TRUNC(DATE '2024-06-15', MONTH), DAY)", 14L)]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE_TRUNC(DATE '2024-06-15', YEAR), DAY)", 166L)]  // Days since Jan 1 (leap year)
	[InlineData("DATE_DIFF(DATE_TRUNC(DATE '2024-06-15', MONTH), DATE_TRUNC(DATE '2024-01-15', MONTH), MONTH)", 5L)]
	public async Task DateDiffTrunc_Chaining_ReturnsExpected(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Additional: FORMAT_DATE with day/month names — more coverage
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "DateTimeFunctionExtended")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-02')", "Tuesday")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-03')", "Wednesday")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-04')", "Thursday")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-05')", "Friday")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-01-06')", "Saturday")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-02-15')", "February")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-03-15')", "March")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-04-15')", "April")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-05-15')", "May")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-07-15')", "July")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-08-15')", "August")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-09-15')", "September")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-10-15')", "October")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-11-15')", "November")]
	public async Task FormatDate_DayAndMonthNames_ReturnsExpected(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);
}
