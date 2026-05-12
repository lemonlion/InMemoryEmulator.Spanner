using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive date/time function tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeExhaustiveIntegrationTests : IntegrationTestBase
{
	public DateTimeExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── CURRENT_DATE / CURRENT_TIMESTAMP ───
	[Fact]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task CurrentDate_ReturnsDate()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task CurrentTimestamp_ReturnsTimestamp()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().BeOfType<DateTime>();
	}

	// ─── DATE literals ───
	[Theory]
	[InlineData("DATE '2024-01-01'", "2024-01-01")]
	[InlineData("DATE '2024-12-31'", "2024-12-31")]
	[InlineData("DATE '2000-02-29'", "2000-02-29")]
	[InlineData("DATE '1970-01-01'", "1970-01-01")]
	[InlineData("DATE '2024-06-15'", "2024-06-15")]
	[InlineData("DATE '2023-03-01'", "2023-03-01")]
	[InlineData("DATE '2020-02-29'", "2020-02-29")]
	[InlineData("DATE '2100-01-01'", "2100-01-01")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateLiterals(string expr, string expected)
	{
		var result = await Eval(expr);
		((DateTime)result!).ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ─── EXTRACT from DATE ───
	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-06-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-06-15')", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-06-15')", 15L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-06-15')", 7L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-06-15')", 167L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2000-01-01')", 2000L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-31')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-31')", 31L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1999-12-31')", 1999L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-01')", 2L)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task ExtractFromDate(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── DATE_ADD ───
	[Theory]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 DAY)", "2024-01-02")]
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 DAY)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-02-28', INTERVAL 1 DAY)", "2024-02-29")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 DAY)", "2024-03-01")]
	[InlineData("DATE_ADD(DATE '2024-12-31', INTERVAL 1 DAY)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 7 DAY)", "2024-01-08")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 30 DAY)", "2024-01-31")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 366 DAY)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 MONTH)", "2024-02-01")]
	[InlineData("DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH)", "2024-02-29")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 12 MONTH)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL 1 YEAR)", "2025-01-01")]
	[InlineData("DATE_ADD(DATE '2024-02-29', INTERVAL 1 YEAR)", "2025-02-28")]
	[InlineData("DATE_ADD(DATE '2024-01-15', INTERVAL -1 DAY)", "2024-01-14")]
	[InlineData("DATE_ADD(DATE '2024-01-01', INTERVAL -1 DAY)", "2023-12-31")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateAdd(string expr, string expected)
	{
		var result = await Eval(expr);
		((DateTime)result!).ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ─── DATE_SUB ───
	[Theory]
	[InlineData("DATE_SUB(DATE '2024-01-02', INTERVAL 1 DAY)", "2024-01-01")]
	[InlineData("DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY)", "2024-02-29")]
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 DAY)", "2023-12-31")]
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 MONTH)", "2023-12-01")]
	[InlineData("DATE_SUB(DATE '2024-03-31', INTERVAL 1 MONTH)", "2024-02-29")]
	[InlineData("DATE_SUB(DATE '2024-01-01', INTERVAL 1 YEAR)", "2023-01-01")]
	[InlineData("DATE_SUB(DATE '2024-02-29', INTERVAL 1 YEAR)", "2023-02-28")]
	[InlineData("DATE_SUB(DATE '2024-06-15', INTERVAL 7 DAY)", "2024-06-08")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateSub(string expr, string expected)
	{
		var result = await Eval(expr);
		((DateTime)result!).ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ─── DATE_DIFF ───
	[Theory]
	[InlineData("DATE_DIFF(DATE '2024-01-02', DATE '2024-01-01', DAY)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-01-02', DAY)", -1L)]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE '2024-01-01', DAY)", 166L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', DAY)", 366L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2024-01-01', DAY)", 0L)]
	[InlineData("DATE_DIFF(DATE '2024-06-01', DATE '2024-01-01', MONTH)", 5L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', YEAR)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-01', DATE '2025-01-01', YEAR)", -1L)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateDiff(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── DATE_TRUNC ───
	[Theory]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', MONTH)", "2024-06-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-06-15', DAY)", "2024-06-15")]
	[InlineData("DATE_TRUNC(DATE '2024-12-25', MONTH)", "2024-12-01")]
	[InlineData("DATE_TRUNC(DATE '2024-12-25', YEAR)", "2024-01-01")]
	[InlineData("DATE_TRUNC(DATE '2024-01-15', MONTH)", "2024-01-01")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateTrunc(string expr, string expected)
	{
		var result = await Eval(expr);
		((DateTime)result!).ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ─── TIMESTAMP literals ───
	[Theory]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z'")]
	[InlineData("TIMESTAMP '2024-06-15T12:30:45Z'")]
	[InlineData("TIMESTAMP '2024-12-31T23:59:59Z'")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task TimestampLiterals(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeOfType<DateTime>();
	}

	// ─── TIMESTAMP_ADD ───
	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	//   CAST(TIMESTAMP AS STRING) uses session default timezone America/Los_Angeles.
	//   January = PST (UTC-8). 2024-01-01T00:00:00Z = 2023-12-31 16:00:00-08 in LA.
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 HOUR) AS STRING)", "2023-12-31 17:00:00-08")]
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 MINUTE) AS STRING)", "2023-12-31 16:01:00-08")]
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 SECOND) AS STRING)", "2023-12-31 16:00:01-08")]
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T23:59:59Z', INTERVAL 1 SECOND) AS STRING)", "2024-01-01 16:00:00-08")]
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 24 HOUR) AS STRING)", "2024-01-01 16:00:00-08")]
	[InlineData("CAST(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 DAY) AS STRING)", "2024-01-01 16:00:00-08")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task TimestampAdd(string expr, string expected)
	{
		var result = await Eval(expr);
		((string)result!).Should().StartWith(expected);
	}

	// ─── TIMESTAMP_SUB ───
	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
	//   CAST(TIMESTAMP AS STRING) uses session default timezone America/Los_Angeles.
	//   January = PST (UTC-8). 2024-01-01T00:00:00Z = 2023-12-31 16:00:00-08 in LA.
	[InlineData("CAST(TIMESTAMP_SUB(TIMESTAMP '2024-01-02T00:00:00Z', INTERVAL 1 DAY) AS STRING)", "2023-12-31 16:00:00-08")]
	[InlineData("CAST(TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:00:00Z', INTERVAL 1 HOUR) AS STRING)", "2023-12-31 16:00:00-08")]
	[InlineData("CAST(TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:01:00Z', INTERVAL 1 MINUTE) AS STRING)", "2023-12-31 16:00:00-08")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task TimestampSub(string expr, string expected)
	{
		var result = await Eval(expr);
		((string)result!).Should().StartWith(expected);
	}

	// ─── TIMESTAMP_DIFF ───
	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-02T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T01:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', HOUR)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:01:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MINUTE)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:01Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', SECOND)", 0L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-01T00:00:00Z', TIMESTAMP '2024-01-02T00:00:00Z', DAY)", -1L)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task TimestampDiff(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── FORMAT_DATE ───
	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-06-15')", "2024-06-15")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-06-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-06-15')", "06")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-06-15')", "15")]
	[InlineData("FORMAT_DATE('%Y/%m/%d', DATE '2024-06-15')", "2024/06/15")]
	[InlineData("FORMAT_DATE('%A', DATE '2024-06-15')", "Saturday")]
	[InlineData("FORMAT_DATE('%B', DATE '2024-06-15')", "June")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task FormatDate(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── FORMAT_TIMESTAMP ───
	[Theory]
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%d', TIMESTAMP '2024-06-15T12:30:45Z')", "2024-06-15")]
	[InlineData("FORMAT_TIMESTAMP('%Y', TIMESTAMP '2024-06-15T12:30:45Z')", "2024")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task FormatTimestamp(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── PARSE_DATE ───
	[Theory]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-06-15')", "2024-06-15")]
	[InlineData("PARSE_DATE('%Y-%m-%d', '2024-01-01')", "2024-01-01")]
	[InlineData("PARSE_DATE('%m/%d/%Y', '06/15/2024')", "2024-06-15")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task ParseDate(string expr, string expected)
	{
		var result = await Eval(expr);
		((DateTime)result!).ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ─── GENERATE_DATE_ARRAY ───
	[Theory]
	[InlineData("ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-10', INTERVAL 1 DAY))", 10L)]
	[InlineData("ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-01', INTERVAL 1 DAY))", 1L)]
	[InlineData("ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-12-01', INTERVAL 1 MONTH))", 12L)]
	[InlineData("ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-07', INTERVAL 1 DAY))", 7L)]
	[InlineData("ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-05', INTERVAL 2 DAY))", 3L)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task GenerateDateArray(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NULL datetime operations ───
	[Theory]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("EXTRACT(MONTH FROM CAST(NULL AS DATE))")]
	[InlineData("DATE_ADD(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("DATE_SUB(CAST(NULL AS DATE), INTERVAL 1 DAY)")]
	[InlineData("FORMAT_DATE('%Y', CAST(NULL AS DATE))")]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateTime_Null_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── DATE comparisons ───
	[Theory]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' = DATE '2024-01-02'", false)]
	[InlineData("DATE '2024-01-01' < DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-02' > DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' <= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' >= DATE '2024-01-01'", true)]
	[InlineData("DATE '2024-01-01' != DATE '2024-01-02'", true)]
	[InlineData("DATE '2024-01-01' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", true)]
	[InlineData("DATE '2025-01-01' BETWEEN DATE '2024-01-01' AND DATE '2024-12-31'", false)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task DateComparisons(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── TIMESTAMP comparisons ───
	[Theory]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' = TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' < TIMESTAMP '2024-01-01T00:00:01Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:01Z' > TIMESTAMP '2024-01-01T00:00:00Z'", true)]
	[InlineData("TIMESTAMP '2024-01-01T00:00:00Z' != TIMESTAMP '2024-01-01T00:00:01Z'", true)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task TimestampComparisons(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── UNIX_SECONDS / UNIX_MILLIS / UNIX_MICROS ───
	[Theory]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:01Z')", 1L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '2024-01-01T00:00:00Z')", 1704067200L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:00Z')", 0L)]
	[InlineData("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:01Z')", 1000000L)]
	[Trait(TestTraits.Category, "DateTimeExhaustive")]
	public async Task UnixTimestampFunctions(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}
}
