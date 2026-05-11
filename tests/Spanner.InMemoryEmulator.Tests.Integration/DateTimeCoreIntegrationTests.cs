using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for date/time construction, extraction, arithmetic, and formatting.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateTimeCoreIntegrationTests : IntegrationTestBase
{
	public DateTimeCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── DATE constructor ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Date_FromString_ReturnsDate()
	{
		var result = await Eval("DATE('2024-01-15')");
		result.Should().BeOfType<DateTime>();
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 15));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Date_LeapYear()
	{
		var result = await Eval("DATE('2024-02-29')");
		((DateTime)result!).Should().Be(new DateTime(2024, 2, 29));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Date_EndOfYear()
	{
		var result = await Eval("DATE('2024-12-31')");
		((DateTime)result!).Should().Be(new DateTime(2024, 12, 31));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Date_StartOfYear()
	{
		var result = await Eval("DATE('2024-01-01')");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 1));
	}

	// ─── TIMESTAMP constructor ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Timestamp_FromString_ReturnsTimestamp()
	{
		var result = await Eval("TIMESTAMP('2024-01-15T10:30:00Z')");
		result.Should().BeOfType<DateTime>();
		var dt = (DateTime)result!;
		dt.Year.Should().Be(2024);
		dt.Month.Should().Be(1);
		dt.Day.Should().Be(15);
		dt.Hour.Should().Be(10);
		dt.Minute.Should().Be(30);
	}

	// ─── CURRENT_DATE / CURRENT_TIMESTAMP ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#current_date
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#current_timestamp

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task CurrentDate_ReturnsToday()
	{
		var result = await Eval("CURRENT_DATE()");
		result.Should().BeOfType<DateTime>();
		var dt = (DateTime)result!;
		// Allow ±1 day to account for timezone differences between emulator (UTC) and
		// Cloud Spanner (America/Los_Angeles default).
		(DateTime.UtcNow.Date - dt.Date).Duration().TotalDays.Should().BeLessOrEqualTo(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task CurrentTimestamp_ReturnsNow()
	{
		var result = await Eval("CURRENT_TIMESTAMP()");
		result.Should().BeOfType<DateTime>();
		var dt = (DateTime)result!;
		(DateTime.UtcNow - dt).TotalSeconds.Should().BeLessThan(10);
	}

	// ─── EXTRACT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE('2024-06-15'))", 2024L)]
	[InlineData("EXTRACT(MONTH FROM DATE('2024-06-15'))", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE('2024-06-15'))", 15L)]
	[InlineData("EXTRACT(DAYOFWEEK FROM DATE('2024-06-15'))", 7L)] // Saturday = 7 in Spanner
	[InlineData("EXTRACT(DAYOFYEAR FROM DATE('2024-06-15'))", 167L)]
	[InlineData("EXTRACT(QUARTER FROM DATE('2024-06-15'))", 2L)]
	[InlineData("EXTRACT(WEEK FROM DATE('2024-01-01'))", 0L)]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Extract_FromDate(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	//   EXTRACT on TIMESTAMP converts to default timezone (America/Los_Angeles) first.
	//   June 15 2024 10:30:45Z = June 15 2024 03:30:45 PDT (UTC-7)
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 2024L)]
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 6L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 15L)]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 3L)]  // 10 UTC = 3 PDT
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 30L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP('2024-06-15T10:30:45Z'))", 45L)]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Extract_FromTimestamp(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── DATE_ADD / DATE_SUB ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_Day()
	{
		var result = await Eval("DATE_ADD(DATE('2024-01-15'), INTERVAL 10 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 25));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_Month()
	{
		var result = await Eval("DATE_ADD(DATE('2024-01-31'), INTERVAL 1 MONTH)");
		((DateTime)result!).Should().Be(new DateTime(2024, 2, 29)); // Leap year
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_Year()
	{
		var result = await Eval("DATE_ADD(DATE('2024-02-29'), INTERVAL 1 YEAR)");
		((DateTime)result!).Should().Be(new DateTime(2025, 2, 28)); // Non-leap year
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateSub_Day()
	{
		var result = await Eval("DATE_SUB(DATE('2024-01-15'), INTERVAL 20 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2023, 12, 26));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateSub_Month()
	{
		var result = await Eval("DATE_SUB(DATE('2024-03-31'), INTERVAL 1 MONTH)");
		((DateTime)result!).Should().Be(new DateTime(2024, 2, 29)); // Leap year
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_NegativeInterval()
	{
		var result = await Eval("DATE_ADD(DATE('2024-01-15'), INTERVAL -5 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 10));
	}

	// ─── DATE_DIFF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff

	[Theory]
	[InlineData("DATE_DIFF(DATE('2024-01-15'), DATE('2024-01-10'), DAY)", 5L)]
	[InlineData("DATE_DIFF(DATE('2024-01-10'), DATE('2024-01-15'), DAY)", -5L)]
	[InlineData("DATE_DIFF(DATE('2024-06-15'), DATE('2024-01-15'), MONTH)", 5L)]
	[InlineData("DATE_DIFF(DATE('2025-01-15'), DATE('2024-01-15'), YEAR)", 1L)]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateDiff_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── DATE_TRUNC ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_trunc

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateTrunc_Month()
	{
		var result = await Eval("DATE_TRUNC(DATE('2024-06-15'), MONTH)");
		((DateTime)result!).Should().Be(new DateTime(2024, 6, 1));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateTrunc_Year()
	{
		var result = await Eval("DATE_TRUNC(DATE('2024-06-15'), YEAR)");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 1));
	}

	// ─── TIMESTAMP_ADD / TIMESTAMP_SUB ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampAdd_Hour()
	{
		var result = await Eval("TIMESTAMP_ADD(TIMESTAMP('2024-01-15T10:00:00Z'), INTERVAL 2 HOUR)");
		var dt = (DateTime)result!;
		dt.Hour.Should().Be(12);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampAdd_Minute()
	{
		var result = await Eval("TIMESTAMP_ADD(TIMESTAMP('2024-01-15T10:00:00Z'), INTERVAL 30 MINUTE)");
		var dt = (DateTime)result!;
		dt.Minute.Should().Be(30);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampAdd_Second()
	{
		var result = await Eval("TIMESTAMP_ADD(TIMESTAMP('2024-01-15T10:00:00Z'), INTERVAL 45 SECOND)");
		var dt = (DateTime)result!;
		dt.Second.Should().Be(45);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampSub_Day()
	{
		var result = await Eval("TIMESTAMP_SUB(TIMESTAMP('2024-01-15T10:00:00Z'), INTERVAL 5 DAY)");
		var dt = (DateTime)result!;
		dt.Day.Should().Be(10);
	}

	// ─── TIMESTAMP_DIFF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff

	[Theory]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP('2024-01-15T12:00:00Z'), TIMESTAMP('2024-01-15T10:00:00Z'), HOUR)", 2L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP('2024-01-15T10:30:00Z'), TIMESTAMP('2024-01-15T10:00:00Z'), MINUTE)", 30L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP('2024-01-16T00:00:00Z'), TIMESTAMP('2024-01-15T00:00:00Z'), DAY)", 1L)]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampDiff_Cases(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── TIMESTAMP_TRUNC ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampTrunc_Hour()
	{
		var result = await Eval("TIMESTAMP_TRUNC(TIMESTAMP('2024-01-15T10:30:45Z'), HOUR)");
		var dt = (DateTime)result!;
		dt.Minute.Should().Be(0);
		dt.Second.Should().Be(0);
		dt.Hour.Should().Be(10);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampTrunc_Day()
	{
		// Ref: TIMESTAMP_TRUNC converts to default timezone (America/Los_Angeles), truncates, converts back.
		// Jan 15 2024 10:30:45Z → Jan 15 02:30:45 PST → truncate to day → Jan 15 00:00:00 PST → Jan 15 08:00:00 UTC
		var result = await Eval("TIMESTAMP_TRUNC(TIMESTAMP('2024-01-15T10:30:45Z'), DAY)");
		var dt = (DateTime)result!;
		dt.Hour.Should().Be(8);  // UTC hour after PST→UTC conversion
		dt.Minute.Should().Be(0);
		dt.Second.Should().Be(0);
		dt.Day.Should().Be(15);
	}

	// ─── FORMAT_DATE / PARSE_DATE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#format_date

	[Theory]
	[InlineData("FORMAT_DATE('%Y-%m-%d', DATE('2024-06-15'))", "2024-06-15")]
	[InlineData("FORMAT_DATE('%Y', DATE('2024-06-15'))", "2024")]
	[InlineData("FORMAT_DATE('%m/%d/%Y', DATE('2024-06-15'))", "06/15/2024")]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task FormatDate_Cases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task ParseDate_BasicCase()
	{
		var result = await Eval("PARSE_DATE('%Y-%m-%d', '2024-06-15')");
		((DateTime)result!).Should().Be(new DateTime(2024, 6, 15));
	}

	// ─── FORMAT_TIMESTAMP / PARSE_TIMESTAMP ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	//   Default timezone is America/Los_Angeles when not specified.

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FormatTimestamp_BasicCase()
	{
		// 2024-06-15 is PDT (-07): 10:30:45 UTC = 03:30:45 PDT
		var result = await Eval("FORMAT_TIMESTAMP('%Y-%m-%d %H:%M:%S', TIMESTAMP('2024-06-15T10:30:45Z'))");
		result.Should().Be("2024-06-15 03:30:45");
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ParseTimestamp_BasicCase()
	{
		// Parses in America/Los_Angeles (PDT -07 in June): 10:30:45 PDT → 17:30:45 UTC
		var result = await Eval("PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', '2024-06-15 10:30:45')");
		var dt = (DateTime)result!;
		dt.ToUniversalTime().Year.Should().Be(2024);
		dt.ToUniversalTime().Month.Should().Be(6);
		dt.ToUniversalTime().Day.Should().Be(15);
		dt.ToUniversalTime().Hour.Should().Be(17);
		dt.ToUniversalTime().Minute.Should().Be(30);
		dt.ToUniversalTime().Second.Should().Be(45);
	}

	// ─── UNIX_SECONDS / UNIX_MILLIS / UNIX_MICROS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_seconds

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixSeconds_Epoch()
	{
		var result = await Eval("UNIX_SECONDS(TIMESTAMP('1970-01-01T00:00:00Z'))");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixSeconds_KnownValue()
	{
		var result = await Eval("UNIX_SECONDS(TIMESTAMP('2024-01-01T00:00:00Z'))");
		result.Should().Be(1704067200L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixMillis_Epoch()
	{
		var result = await Eval("UNIX_MILLIS(TIMESTAMP('1970-01-01T00:00:00Z'))");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixMicros_Epoch()
	{
		var result = await Eval("UNIX_MICROS(TIMESTAMP('1970-01-01T00:00:00Z'))");
		result.Should().Be(0L);
	}

	// ─── TIMESTAMP_SECONDS / TIMESTAMP_MILLIS / TIMESTAMP_MICROS ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_seconds

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampSeconds_Zero_IsEpoch()
	{
		var result = await Eval("TIMESTAMP_SECONDS(0)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampMillis_Zero_IsEpoch()
	{
		var result = await Eval("TIMESTAMP_MILLIS(0)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampMicros_Zero_IsEpoch()
	{
		var result = await Eval("TIMESTAMP_MICROS(0)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
	}

	// ─── UNIX_DATE / DATE_FROM_UNIX_DATE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#unix_date

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixDate_Epoch()
	{
		var result = await Eval("UNIX_DATE(DATE('1970-01-01'))");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixDate_PositiveValue()
	{
		var result = await Eval("UNIX_DATE(DATE('1970-01-02'))");
		result.Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateFromUnixDate_Zero_IsEpoch()
	{
		var result = await Eval("DATE_FROM_UNIX_DATE(0)");
		((DateTime)result!).Should().Be(new DateTime(1970, 1, 1));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateFromUnixDate_Positive()
	{
		var result = await Eval("DATE_FROM_UNIX_DATE(365)");
		((DateTime)result!).Should().Be(new DateTime(1971, 1, 1));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task UnixDate_RoundTrip()
	{
		var result = await Eval("DATE_FROM_UNIX_DATE(UNIX_DATE(DATE('2024-06-15')))");
		((DateTime)result!).Should().Be(new DateTime(2024, 6, 15));
	}

	// ─── GENERATE_DATE_ARRAY ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_date_array

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateDateArray_DayStep()
	{
		var rows = await QueryAsync("SELECT d FROM UNNEST(GENERATE_DATE_ARRAY(DATE('2024-01-01'), DATE('2024-01-05'), INTERVAL 1 DAY)) AS d");
		rows.Should().HaveCount(5);
		((DateTime)rows[0]["d"]!).Day.Should().Be(1);
		((DateTime)rows[4]["d"]!).Day.Should().Be(5);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task GenerateDateArray_MonthStep()
	{
		var rows = await QueryAsync("SELECT d FROM UNNEST(GENERATE_DATE_ARRAY(DATE('2024-01-01'), DATE('2024-06-01'), INTERVAL 1 MONTH)) AS d");
		rows.Should().HaveCount(6);
	}

	// ─── Date columns in table operations ───

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateColumn_InsertAndQuery()
	{
		var table = "DtTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, BirthDate DATE) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["BirthDate"] = new DateTime(1990, 5, 15) });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["BirthDate"] = new DateTime(2000, 12, 25) });

		var rows = await QueryAsync($"SELECT EXTRACT(YEAR FROM BirthDate) AS yr FROM {table} ORDER BY Id");
		rows[0]["yr"].Should().Be(1990L);
		rows[1]["yr"].Should().Be(2000L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateColumn_WhereFilter()
	{
		var table = "DtTbl2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, EventDate DATE) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["EventDate"] = new DateTime(2024, 1, 1) });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["EventDate"] = new DateTime(2024, 6, 15) });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["EventDate"] = new DateTime(2024, 12, 31) });

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE EventDate > DATE('2024-06-01') ORDER BY Id");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateColumn_OrderBy()
	{
		var table = "DtTbl3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, EventDate DATE) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["EventDate"] = new DateTime(2024, 12, 31) });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["EventDate"] = new DateTime(2024, 1, 1) });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["EventDate"] = new DateTime(2024, 6, 15) });

		var rows = await QueryAsync($"SELECT Id FROM {table} ORDER BY EventDate");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 3L, 1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task TimestampColumn_InsertAndQuery()
	{
		var table = "DtTbl4";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, CreatedAt TIMESTAMP) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["CreatedAt"] = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc) });

		// Ref: EXTRACT on TIMESTAMP uses America/Los_Angeles. Jan 15 10:30 UTC = 02:30 PST.
		var rows = await QueryAsync($"SELECT EXTRACT(HOUR FROM CreatedAt) AS hr FROM {table}");
		rows[0]["hr"].Should().Be(2L);
	}

	// ─── DateAdd crossing month boundary ───

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_CrossingMonthBoundary()
	{
		var result = await Eval("DATE_ADD(DATE('2024-01-30'), INTERVAL 5 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2024, 2, 4));
	}

	// ─── DateAdd crossing year boundary ───

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_CrossingYearBoundary()
	{
		var result = await Eval("DATE_ADD(DATE('2024-12-25'), INTERVAL 10 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2025, 1, 4));
	}

	// ─── Null date operations ───

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task DateAdd_Null_ReturnsNull()
	{
		var result = await Eval("DATE_ADD(CAST(NULL AS DATE), INTERVAL 1 DAY)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	public async Task Extract_FromNull_ReturnsNull()
	{
		var result = await Eval("EXTRACT(YEAR FROM CAST(NULL AS DATE))");
		result.Should().BeNull();
	}

	// ─── ADDDATE / SUBDATE aliases ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AddDate_Alias()
	{
		var result = await Eval("ADDDATE(DATE('2024-01-15'), INTERVAL 5 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 20));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateTime")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SubDate_Alias()
	{
		var result = await Eval("SUBDATE(DATE('2024-01-15'), INTERVAL 5 DAY)");
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 10));
	}
}
