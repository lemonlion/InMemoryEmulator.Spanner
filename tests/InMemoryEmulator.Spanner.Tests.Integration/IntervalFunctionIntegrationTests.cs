using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for INTERVAL type, INTERVAL literal expressions, and interval functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class IntervalFunctionIntegrationTests : IntegrationTestBase
{
	public IntervalFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// MAKE_INTERVAL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#make_interval
	//   "Constructs an INTERVAL object using INT64 values representing the
	//    year, month, day, hour, minute, and second."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MakeInterval_FullArgs_ReturnsCanonicalFormat()
	{
		var rows = await QueryAsync("SELECT MAKE_INTERVAL(1, 6, 15, 0, 0, 0) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P1Y6M15D");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MakeInterval_HoursAndSeconds_ReturnsCorrectFormat()
	{
		var rows = await QueryAsync("SELECT MAKE_INTERVAL(0, 0, 0, 10, 0, 20) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("PT10H20S");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MakeInterval_ZeroArgs_ReturnsZeroInterval()
	{
		var rows = await QueryAsync("SELECT MAKE_INTERVAL(0, 0, 0, 0, 0, 0) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P0Y");
	}

	// ═══════════════════════════════════════════════════════════════
	// JUSTIFY_DAYS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_days
	//   "Normalizes the day part of the interval to the range from -29 to 29
	//    by incrementing/decrementing the month or year part of the interval."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyDays_31Days_CarriesToMonth()
	{
		var rows = await QueryAsync("SELECT JUSTIFY_DAYS(INTERVAL 31 DAY) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P1M1D");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyDays_370Days_CarriesToYear()
	{
		var rows = await QueryAsync("SELECT JUSTIFY_DAYS(INTERVAL 370 DAY) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P1Y10D");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyDays_29Days_NoChange()
	{
		var rows = await QueryAsync("SELECT JUSTIFY_DAYS(INTERVAL 29 DAY) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P29D");
	}

	// ═══════════════════════════════════════════════════════════════
	// JUSTIFY_HOURS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_hours
	//   "Normalizes the time part of the interval to the range from
	//    -23:59:59.999999 to 23:59:59.999999 by incrementing/decrementing
	//    the day part of the interval."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyHours_47Hours_CarriesToDay()
	{
		var rows = await QueryAsync("SELECT JUSTIFY_HOURS(INTERVAL 47 HOUR) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P1DT23H");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyHours_23Hours_NoChange()
	{
		var rows = await QueryAsync("SELECT JUSTIFY_HOURS(INTERVAL 23 HOUR) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("PT23H");
	}

	// ═══════════════════════════════════════════════════════════════
	// JUSTIFY_INTERVAL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_interval
	//   "Normalizes the days and time parts of the interval."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JustifyInterval_NormalizesHoursAndDays()
	{
		// 29 days + 49 hours = 29 days + 2 days + 1 hour = 31 days + 1h = 1 month 1 day 1:0:0
		var rows = await QueryAsync("SELECT JUSTIFY_INTERVAL(MAKE_INTERVAL(0, 0, 29, 49, 0, 0)) AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P1M1DT1H");
	}

	// ═══════════════════════════════════════════════════════════════
	// INTERVAL literal
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
	//   "INTERVAL int64_expression datetime_part"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task IntervalLiteral_Year_ReturnsCanonicalFormat()
	{
		var rows = await QueryAsync("SELECT INTERVAL 2 YEAR AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P2Y");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task IntervalLiteral_Day_ReturnsCanonicalFormat()
	{
		var rows = await QueryAsync("SELECT INTERVAL 42 DAY AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("P42D");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task IntervalLiteral_Hour_ReturnsCanonicalFormat()
	{
		var rows = await QueryAsync("SELECT INTERVAL 25 HOUR AS I");
		rows.Should().HaveCount(1);
		rows[0]["I"]!.ToString().Should().Be("PT25H");
	}
}
