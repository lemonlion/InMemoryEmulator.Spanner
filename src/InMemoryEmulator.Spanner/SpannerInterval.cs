namespace InMemoryEmulator.Spanner;

/// <summary>
/// Represents a GoogleSQL INTERVAL value with three parts: months, days, and nanoseconds.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
///   INTERVAL stores a duration as:
///   - months (encoded as years*12 + months)
///   - days
///   - nanoseconds (encoded as hours*3600e9 + minutes*60e9 + seconds*1e9 + nanos)
/// Canonical format: [sign]Y-M [sign]D [sign]H:M:S[.F]
/// </summary>
internal sealed class SpannerInterval
{
	/// <summary>Total number of months (years * 12 + months).</summary>
	public int Months { get; }

	/// <summary>Number of days.</summary>
	public int Days { get; }

	/// <summary>Time component in nanoseconds.</summary>
	public long Nanos { get; }

	public SpannerInterval(int months, int days, long nanos)
	{
		Months = months;
		Days = days;
		Nanos = nanos;
	}

	// Convenience constructors from common parts
	public static SpannerInterval FromYears(long years) => new((int)(years * 12), 0, 0);
	public static SpannerInterval FromMonths(long months) => new((int)months, 0, 0);
	public static SpannerInterval FromDays(long days) => new(0, (int)days, 0);
	public static SpannerInterval FromHours(long hours) => new(0, 0, hours * 3_600_000_000_000L);
	public static SpannerInterval FromMinutes(long minutes) => new(0, 0, minutes * 60_000_000_000L);
	public static SpannerInterval FromSeconds(long seconds) => new(0, 0, seconds * 1_000_000_000L);
	public static SpannerInterval FromMicroseconds(long micros) => new(0, 0, micros * 1_000L);
	public static SpannerInterval FromMilliseconds(long millis) => new(0, 0, millis * 1_000_000L);
	public static SpannerInterval FromNanoseconds(long nanos) => new(0, 0, nanos);

	/// <summary>
	/// Creates an interval from individual components (like MAKE_INTERVAL).
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#make_interval
	/// </summary>
	public static SpannerInterval Make(long year = 0, long month = 0, long day = 0,
		long hour = 0, long minute = 0, long second = 0)
	{
		var totalMonths = (int)(year * 12 + month);
		var totalNanos = hour * 3_600_000_000_000L
						 + minute * 60_000_000_000L
						 + second * 1_000_000_000L;
		return new SpannerInterval(totalMonths, (int)day, totalNanos);
	}

	/// <summary>
	/// JUSTIFY_DAYS: normalizes days to range [-29..29] by carrying to months.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_days
	///   "Normalizes the day part of the interval to the range from -29 to 29
	///    by incrementing/decrementing the month or year part of the interval."
	/// </summary>
	public SpannerInterval JustifyDays()
	{
		// Each 30 days = 1 month
		var extraMonths = Days / 30;
		var remainingDays = Days % 30;
		return new SpannerInterval(Months + extraMonths, remainingDays, Nanos);
	}

	/// <summary>
	/// JUSTIFY_HOURS: normalizes time part to range [-(24h-1ns)..(24h-1ns)] by carrying to days.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_hours
	///   "Normalizes the time part of the interval to the range from -23:59:59.999999
	///    to 23:59:59.999999 by incrementing/decrementing the day part of the interval."
	/// </summary>
	public SpannerInterval JustifyHours()
	{
		const long nanosPerDay = 24L * 3_600_000_000_000L;
		var extraDays = (int)(Nanos / nanosPerDay);
		var remainingNanos = Nanos % nanosPerDay;
		return new SpannerInterval(Months, Days + extraDays, remainingNanos);
	}

	/// <summary>
	/// JUSTIFY_INTERVAL: normalizes both days and time parts.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_interval
	///   "Normalizes the days and time parts of the interval."
	/// </summary>
	public SpannerInterval JustifyInterval() => JustifyHours().JustifyDays();

	/// <summary>
	/// EXTRACT part from INTERVAL.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#extract
	/// </summary>
	public long Extract(string part)
	{
		return part.ToUpperInvariant() switch
		{
			"YEAR" => Months / 12,
			"MONTH" => Months % 12,
			"DAY" => Days,
			"HOUR" => Nanos / 3_600_000_000_000L,
			"MINUTE" => (Nanos % 3_600_000_000_000L) / 60_000_000_000L,
			"SECOND" => (Nanos % 60_000_000_000L) / 1_000_000_000L,
			"MILLISECOND" => (Nanos % 1_000_000_000L) / 1_000_000L,
			"MICROSECOND" => (Nanos % 1_000_000L) / 1_000L,
			_ => throw new InvalidOperationException($"Unsupported EXTRACT part for INTERVAL: {part}")
		};
	}

	/// <summary>
	/// Returns the ISO 8601 duration string representation (e.g., P1Y6M15DT10H20S).
	/// This is the format expected by the Google Cloud Spanner .NET SDK's Interval.Parse().
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
	/// </summary>
	public override string ToString()
	{
		var years = Months / 12;
		var months = Months % 12;
		var days = Days;

		var absNanos = Math.Abs(Nanos);
		var hours = absNanos / 3_600_000_000_000L;
		var minutes = (absNanos % 3_600_000_000_000L) / 60_000_000_000L;
		var seconds = (absNanos % 60_000_000_000L) / 1_000_000_000L;
		var fracNanos = absNanos % 1_000_000_000L;

		var sign = (Months < 0 || Days < 0 || Nanos < 0) && Months <= 0 && Days <= 0 && Nanos <= 0 ? "-" : "";

		var datePart = "";
		if (years != 0) datePart += $"{Math.Abs(years)}Y";
		if (months != 0) datePart += $"{Math.Abs(months)}M";
		if (days != 0) datePart += $"{Math.Abs(days)}D";

		var timePart = "";
		if (hours != 0) timePart += $"{hours}H";
		if (minutes != 0) timePart += $"{minutes}M";
		if (seconds != 0 || fracNanos != 0)
		{
			if (fracNanos > 0)
				timePart += $"{seconds}.{fracNanos:D9}".TrimEnd('0') + "S";
			else
				timePart += $"{seconds}S";
		}

		if (datePart == "" && timePart == "")
			return "P0Y";

		var result = sign + "P" + datePart;
		if (timePart != "")
			result += "T" + timePart;

		return result;
	}

	public override bool Equals(object? obj) =>
		obj is SpannerInterval other && Months == other.Months && Days == other.Days && Nanos == other.Nanos;

	public override int GetHashCode() => HashCode.Combine(Months, Days, Nanos);
}
