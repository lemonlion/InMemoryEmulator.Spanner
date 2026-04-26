using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive tests for date/time SQL functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeFunctionIntegrationTests : IntegrationTestBase
{
	public DateTimeFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CURRENT_TIMESTAMP / CURRENT_DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#current_timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task CurrentTimestamp_ReturnsDateTime()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	public async Task CurrentTimestamp_IsRecentTime()
	{
		var result = (DateTime)(await Eval("CURRENT_TIMESTAMP()"))!;
		result.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
	}

	[Fact]
	public async Task CurrentDate_ReturnsDateTime()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().BeOfType<DateTime>();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task CurrentDate_IsToday()
	{
		var result = (DateTime)(await Eval("CURRENT_DATE()"))!;
		result.Date.Should().Be(DateTime.UtcNow.Date);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE constructor
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Date_FromLiteral_ReturnsExpected()
	{
		var result = (DateTime)(await Eval("DATE '2024-01-15'"))!;
		result.Should().Be(new DateTime(2024, 1, 15));
	}

	[Fact]
	public async Task Date_FromLiteral_Jan1()
	{
		var result = (DateTime)(await Eval("DATE '2024-01-01'"))!;
		result.Should().Be(new DateTime(2024, 1, 1));
	}

	[Fact]
	public async Task Date_FromLiteral_Dec31()
	{
		var result = (DateTime)(await Eval("DATE '2024-12-31'"))!;
		result.Should().Be(new DateTime(2024, 12, 31));
	}

	[Fact]
	public async Task Date_FromLiteral_LeapDay()
	{
		var result = (DateTime)(await Eval("DATE '2024-02-29'"))!;
		result.Should().Be(new DateTime(2024, 2, 29));
	}

	[Fact]
	public async Task Date_FromComponents()
	{
		var result = (DateTime)(await Eval("DATE(2024, 6, 15)"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	[Fact]
	public async Task Date_FromComponents_Year2000()
	{
		var result = (DateTime)(await Eval("DATE(2000, 1, 1)"))!;
		result.Should().Be(new DateTime(2000, 1, 1));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP constructor
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Timestamp_FromLiteral()
	{
		var result = (DateTime)(await Eval("TIMESTAMP '2024-01-15T10:30:00Z'"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task Timestamp_FromLiteral_Midnight()
	{
		var result = (DateTime)(await Eval("TIMESTAMP '2024-01-01T00:00:00Z'"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task Timestamp_FromLiteral_EndOfDay()
	{
		var result = (DateTime)(await Eval("TIMESTAMP '2024-12-31T23:59:59Z'"))!;
		result.Should().Be(new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// EXTRACT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-06-15')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-06-15')", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-06-15')", 15L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2000-01-01')", 2000L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-12-31')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-12-31')", 31L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1999-01-01')", 1999L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(YEAR FROM DATE '2024-02-29')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2024-02-29')", 2L)]
	[InlineData("EXTRACT(DAY FROM DATE '2024-02-29')", 29L)]
	public async Task Extract_Date_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	//   "If no time zone is specified, the default time zone, America/Los_Angeles, is used."
	//   2024-06-15T14:30:45Z â†’ LA(UTC-7 DST) = 2024-06-15T07:30:45
	//   2024-01-01T00:00:00Z â†’ LA(UTC-8)     = 2023-12-31T16:00:00
	//   2024-12-31T23:59:59Z â†’ LA(UTC-8)     = 2024-12-31T15:59:59
	[Theory]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-06-15T14:30:45Z')", 7L)]
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-06-15T14:30:45Z')", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-06-15T14:30:45Z')", 45L)]
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-06-15T14:30:45Z')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-06-15T14:30:45Z')", 6L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-06-15T14:30:45Z')", 15L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-01-01T00:00:00Z')", 16L)]
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-01-01T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-01-01T00:00:00Z')", 0L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-12-31T23:59:59Z')", 15L)]
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-12-31T23:59:59Z')", 59L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-12-31T23:59:59Z')", 59L)]
	public async Task Extract_Timestamp_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-01')", 2L)]  // Monday=2 in Spanner (Sun=1)
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE '2024-01-07')", 1L)]  // Sunday=1
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-01-01')", 1L)]
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE '2024-12-31')", 366L)] // 2024 is leap year
	[InlineData("EXTRACT(QUARTER FROM DATE '2024-01-15')", 1L)]
	[InlineData("EXTRACT(QUARTER FROM DATE '2024-04-15')", 2L)]
	[InlineData("EXTRACT(QUARTER FROM DATE '2024-07-15')", 3L)]
	[InlineData("EXTRACT(QUARTER FROM DATE '2024-10-15')", 4L)]
	public async Task Extract_SpecialParts_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Theory]
	[InlineData("EXTRACT(WEEK FROM DATE '2024-01-01')", 0L)] // Before first Sunday â†’ week 0
	[InlineData("EXTRACT(ISOYEAR FROM DATE '2024-01-01')", 2024L)]
	public async Task Extract_Week_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// EXTRACT(DATE FROM TIMESTAMP)
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Extract_Date_FromTimestamp()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
		//   Default timezone: America/Los_Angeles.
		//   2024-06-15T14:30:45Z â†’ LA(UTC-7 DST) = 2024-06-15T07:30:45 â†’ DATE = 2024-06-15
		var result = (DateTime)(await Eval("EXTRACT(DATE FROM TIMESTAMP '2024-06-15T14:30:45Z')"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampAdd_Day()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 16, 10, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_Hour()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 3 HOUR)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 13, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_Minute()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 30 MINUTE)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_Second()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 45 SECOND)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 0, 45, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_MultipleDays()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T00:00:00Z', INTERVAL 10 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 25, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_CrossMonth()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-31T10:00:00Z', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 2, 1, 10, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_CrossYear()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-12-31T23:00:00Z', INTERVAL 2 HOUR)"))!;
		result.Should().Be(new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_Millisecond()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 500 MILLISECOND)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 0, 0, 500, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampAdd_Microsecond()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1000000 MICROSECOND)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 0, 1, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampSub_Day()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 14, 10, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampSub_Hour()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL 3 HOUR)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 7, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampSub_Minute()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-01-15T10:30:00Z', INTERVAL 15 MINUTE)"))!;
		result.Should().Be(new DateTime(2024, 1, 15, 10, 15, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampSub_CrossMonth()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-02-01T10:00:00Z', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 31, 10, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampSub_CrossYear()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SUB(TIMESTAMP '2024-01-01T01:00:00Z', INTERVAL 2 HOUR)"))!;
		result.Should().Be(new DateTime(2023, 12, 31, 23, 0, 0, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-16T10:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', DAY)", 1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T13:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', HOUR)", 3L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:30:00Z', TIMESTAMP '2024-01-15T10:00:00Z', MINUTE)", 30L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:45Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)", 45L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:00Z', TIMESTAMP '2024-01-16T10:00:00Z', DAY)", -1L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)", 0L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-02-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', DAY)", 31L)]
	public async Task TimestampDiff_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampTrunc_Day()
	{
		// LA: Jun 15 07:30:45 â†’ trunc DAY â†’ Jun 15 00:00 LA â†’ UTC Jun 15 07:00
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', DAY)"))!;
		result.Should().Be(new DateTime(2024, 6, 15, 7, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampTrunc_Hour()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', HOUR)"))!;
		result.Should().Be(new DateTime(2024, 6, 15, 14, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampTrunc_Minute()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', MINUTE)"))!;
		result.Should().Be(new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampTrunc_Second()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', SECOND)"))!;
		result.Should().Be(new DateTime(2024, 6, 15, 14, 30, 45, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampTrunc_Month()
	{
		// LA: Jun 15 07:30:45 â†’ trunc MONTH â†’ Jun 1 00:00 LA â†’ UTC Jun 1 07:00
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', MONTH)"))!;
		result.Should().Be(new DateTime(2024, 6, 1, 7, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampTrunc_Year()
	{
		// LA: Jun 15 07:30:45 â†’ trunc YEAR â†’ Jan 1 00:00 LA (UTC-8) â†’ UTC Jan 1 08:00
		var result = (DateTime)(await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', YEAR)"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_ADD
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task DateAdd_Day()
	{
		var result = (DateTime)(await Eval("DATE_ADD(DATE '2024-01-15', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 16));
	}

	[Fact]
	public async Task DateAdd_MultipleDays()
	{
		var result = (DateTime)(await Eval("DATE_ADD(DATE '2024-01-15', INTERVAL 30 DAY)"))!;
		result.Should().Be(new DateTime(2024, 2, 14));
	}

	[Fact]
	public async Task DateAdd_LeapYear()
	{
		var result = (DateTime)(await Eval("DATE_ADD(DATE '2024-02-28', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 2, 29));
	}

	[Fact]
	public async Task DateAdd_CrossYear()
	{
		var result = (DateTime)(await Eval("DATE_ADD(DATE '2024-12-31', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2025, 1, 1));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_sub
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task DateSub_Day()
	{
		var result = (DateTime)(await Eval("DATE_SUB(DATE '2024-01-15', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 14));
	}

	[Fact]
	public async Task DateSub_CrossYear()
	{
		var result = (DateTime)(await Eval("DATE_SUB(DATE '2024-01-01', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2023, 12, 31));
	}

	[Fact]
	public async Task DateSub_CrossMonth()
	{
		var result = (DateTime)(await Eval("DATE_SUB(DATE '2024-03-01', INTERVAL 1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 2, 29));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_DIFF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("DATE_DIFF(DATE '2024-01-16', DATE '2024-01-15', DAY)", 1L)]
	[InlineData("DATE_DIFF(DATE '2024-02-01', DATE '2024-01-01', DAY)", 31L)]
	[InlineData("DATE_DIFF(DATE '2024-01-15', DATE '2024-01-16', DAY)", -1L)]
	[InlineData("DATE_DIFF(DATE '2024-01-15', DATE '2024-01-15', DAY)", 0L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', DAY)", 366L)]
	[InlineData("DATE_DIFF(DATE '2024-06-15', DATE '2024-01-15', MONTH)", 5L)]
	[InlineData("DATE_DIFF(DATE '2025-01-15', DATE '2024-01-15', YEAR)", 1L)]
	public async Task DateDiff_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DATE_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task DateTrunc_Month()
	{
		var result = (DateTime)(await Eval("DATE_TRUNC(DATE '2024-06-15', MONTH)"))!;
		result.Should().Be(new DateTime(2024, 6, 1));
	}

	[Fact]
	public async Task DateTrunc_Year()
	{
		var result = (DateTime)(await Eval("DATE_TRUNC(DATE '2024-06-15', YEAR)"))!;
		result.Should().Be(new DateTime(2024, 1, 1));
	}

	[Fact]
	public async Task DateTrunc_Day()
	{
		var result = (DateTime)(await Eval("DATE_TRUNC(DATE '2024-06-15', DAY)"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UNIX_SECONDS / UNIX_MILLIS / UNIX_MICROS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_seconds
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task UnixSeconds_Epoch_ReturnsZero()
		=> (await Eval("UNIX_SECONDS(TIMESTAMP '1970-01-01T00:00:00Z')")).Should().Be(0L);

	[Fact]
	public async Task UnixSeconds_KnownTimestamp()
		=> (await Eval("UNIX_SECONDS(TIMESTAMP '2024-01-01T00:00:00Z')")).Should().Be(1704067200L);

	[Fact]
	public async Task UnixMillis_Epoch_ReturnsZero()
		=> (await Eval("UNIX_MILLIS(TIMESTAMP '1970-01-01T00:00:00Z')")).Should().Be(0L);

	[Fact]
	public async Task UnixMillis_KnownTimestamp()
		=> (await Eval("UNIX_MILLIS(TIMESTAMP '2024-01-01T00:00:00Z')")).Should().Be(1704067200000L);

	[Fact]
	public async Task UnixMicros_Epoch_ReturnsZero()
		=> (await Eval("UNIX_MICROS(TIMESTAMP '1970-01-01T00:00:00Z')")).Should().Be(0L);

	[Fact]
	public async Task UnixMicros_KnownTimestamp()
		=> (await Eval("UNIX_MICROS(TIMESTAMP '2024-01-01T00:00:00Z')")).Should().Be(1704067200000000L);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// TIMESTAMP_SECONDS / TIMESTAMP_MILLIS / TIMESTAMP_MICROS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_seconds
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampSeconds_Epoch()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SECONDS(0)"))!;
		result.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampSeconds_KnownValue()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SECONDS(1704067200)"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampMillis_Epoch()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_MILLIS(0)"))!;
		result.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampMillis_KnownValue()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_MILLIS(1704067200000)"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampMicros_Epoch()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_MICROS(0)"))!;
		result.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task TimestampMicros_KnownValue()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_MICROS(1704067200000000)"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT_TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("FORMAT_TIMESTAMP('%Y-%m-%d', TIMESTAMP '2024-06-15T10:30:00Z')", "2024-06-15")]
	[InlineData("FORMAT_TIMESTAMP('%H:%M:%S', TIMESTAMP '2024-06-15T10:30:45Z')", "10:30:45")]
	[InlineData("FORMAT_TIMESTAMP('%Y', TIMESTAMP '2024-06-15T10:30:00Z')", "2024")]
	[InlineData("FORMAT_TIMESTAMP('%m', TIMESTAMP '2024-06-15T10:30:00Z')", "06")]
	[InlineData("FORMAT_TIMESTAMP('%d', TIMESTAMP '2024-06-15T10:30:00Z')", "15")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task FormatTimestamp_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// PARSE_TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task ParseTimestamp_BasicFormat()
	{
		var result = (DateTime)(await Eval("PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', '2024-06-15 10:30:45')"))!;
		result.Year.Should().Be(2024);
		result.Month.Should().Be(6);
		result.Day.Should().Be(15);
		result.Hour.Should().Be(10);
		result.Minute.Should().Be(30);
		result.Second.Should().Be(45);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FORMAT_DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE '2024-06-15')", "2024-06-15")]
	[InlineData("FORMAT_DATE('%Y', DATE '2024-06-15')", "2024")]
	[InlineData("FORMAT_DATE('%m', DATE '2024-06-15')", "06")]
	[InlineData("FORMAT_DATE('%d', DATE '2024-06-15')", "15")]
	public async Task FormatDate_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// PARSE_DATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#parse_date
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task ParseDate_BasicFormat()
	{
		var result = (DateTime)(await Eval("PARSE_DATE('%Y-%m-%d', '2024-06-15')"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	[Fact]
	public async Task ParseDate_UsFormat()
	{
		var result = (DateTime)(await Eval("PARSE_DATE('%m/%d/%Y', '06/15/2024')"))!;
		result.Should().Be(new DateTime(2024, 6, 15));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Date/time arithmetic combinations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampAdd_ThenDiff_RoundTrips()
	{
		var result = await Eval(
			"TIMESTAMP_DIFF(TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 10 DAY), " +
			"TIMESTAMP '2024-01-01T00:00:00Z', DAY)");
		result.Should().Be(10L);
	}

	[Fact]
	public async Task DateAdd_ThenDiff_RoundTrips()
	{
		var result = await Eval(
			"DATE_DIFF(DATE_ADD(DATE '2024-01-01', INTERVAL 5 DAY), DATE '2024-01-01', DAY)");
		result.Should().Be(5L);
	}

	[Fact]
	public async Task Extract_FromDateAdd()
	{
		var result = await Eval("EXTRACT(DAY FROM DATE_ADD(DATE '2024-01-15', INTERVAL 10 DAY))");
		result.Should().Be(25L);
	}

	[Fact]
	public async Task TimestampAdd_NegativeInterval()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_ADD(TIMESTAMP '2024-01-15T10:00:00Z', INTERVAL -1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 14, 10, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task DateSub_NegativeInterval()
	{
		var result = (DateTime)(await Eval("DATE_SUB(DATE '2024-01-15', INTERVAL -1 DAY)"))!;
		result.Should().Be(new DateTime(2024, 1, 16));
	}

	[Fact]
	public async Task TimestampDiff_SameTimestamp_ReturnsZero()
	{
		var result = await Eval(
			"TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)");
		result.Should().Be(0L);
	}

	[Fact]
	public async Task DateDiff_SameDate_ReturnsZero()
		=> (await Eval("DATE_DIFF(DATE '2024-01-15', DATE '2024-01-15', DAY)")).Should().Be(0L);

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Nested date/time operations
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task TimestampTrunc_ThenExtract()
	{
		var result = await Eval(
			"EXTRACT(HOUR FROM TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15T14:30:45Z', DAY))");
		result.Should().Be(0L);
	}

	[Fact]
	public async Task UnixSeconds_ThenTimestampSeconds_RoundTrips()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_SECONDS(UNIX_SECONDS(TIMESTAMP '2024-01-01T00:00:00Z'))"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public async Task UnixMillis_ThenTimestampMillis_RoundTrips()
	{
		var result = (DateTime)(await Eval("TIMESTAMP_MILLIS(UNIX_MILLIS(TIMESTAMP '2024-01-01T00:00:00Z'))"))!;
		result.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NULL propagation for date functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData("EXTRACT(YEAR FROM CAST(NULL AS DATE))")]
	[InlineData("UNIX_SECONDS(CAST(NULL AS TIMESTAMP))")]
	[InlineData("UNIX_MILLIS(CAST(NULL AS TIMESTAMP))")]
	[InlineData("UNIX_MICROS(CAST(NULL AS TIMESTAMP))")]
	public async Task DateFunction_NullPropagation(string expr)
		=> (await Eval(expr)).Should().BeNull();
}
