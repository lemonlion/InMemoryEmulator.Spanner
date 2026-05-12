using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for DIV with NUMERIC and DATE(timestamp) timezone conversion bugs.
/// </summary>
public class DivAndDateCtorTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });
		return db;
	}

	private static object? Eval(string expr)
	{
		using var db = CreateDb();
		return db.ExecuteQuery($"SELECT {expr} AS R FROM T WHERE Id=1")[0]["R"];
	}

	// ─── DIV with NUMERIC ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
	//   "DIV(X, Y): Returns the result of integer division of X by Y."
	//   NUMERIC × NUMERIC → NUMERIC (truncates toward zero)

	[Fact]
	public void Div_Numeric_TruncatesTowardZero()
	{
		// DIV(7.5, 2.0) = truncate(3.75) = 3
		var result = Eval("DIV(CAST(7.5 AS NUMERIC), CAST(2.0 AS NUMERIC))");
		result.Should().Be(3m);
	}

	[Fact]
	public void Div_Numeric_NegativeTruncatesTowardZero()
	{
		// DIV(-7.5, 2.0) = truncate(-3.75) = -3 (toward zero, not floor)
		var result = Eval("DIV(CAST(-7.5 AS NUMERIC), CAST(2.0 AS NUMERIC))");
		result.Should().Be(-3m);
	}

	[Fact]
	public void Div_Numeric_WholeNumbers()
	{
		// DIV(10.0, 3.0) = truncate(3.333...) = 3
		var result = Eval("DIV(CAST(10.0 AS NUMERIC), CAST(3.0 AS NUMERIC))");
		result.Should().Be(3m);
	}

	[Fact]
	public void Div_Numeric_ExactDivision()
	{
		// DIV(6.0, 2.0) = 3 exact
		var result = Eval("DIV(CAST(6.0 AS NUMERIC), CAST(2.0 AS NUMERIC))");
		result.Should().Be(3m);
	}

	[Fact]
	public void Div_Numeric_DivisionByZero_Throws()
	{
		var act = () => Eval("DIV(CAST(1.0 AS NUMERIC), CAST(0.0 AS NUMERIC))");
		act.Should().Throw<InvalidOperationException>().WithMessage("*zero*");
	}

	[Fact]
	public void Div_Int64_StillWorks()
	{
		// Regression: DIV with INT64 should still work
		var result = Eval("DIV(7, 2)");
		result.Should().Be(3L);
	}

	[Fact]
	public void Div_Int64_NegativeTruncatesTowardZero()
	{
		// -7 / 2 = -3.5 → truncate toward zero → -3
		var result = Eval("DIV(-7, 2)");
		result.Should().Be(-3L);
	}

	// ─── DATE(timestamp) timezone conversion ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	//   "Extracts the DATE from a TIMESTAMP expression. If no time zone is specified,
	//    the default time zone, America/Los_Angeles, is used."

	[Fact]
	public void Date_FromTimestamp_ConvertsToDefaultTimezone()
	{
		// 2025-01-16T06:00:00Z UTC = 2025-01-15T22:00:00 Pacific
		// DATE should return 2025-01-15 (the LA date)
		var result = Eval("DATE(TIMESTAMP '2025-01-16T06:00:00Z')");
		result.Should().Be(new DateTime(2025, 1, 15));
	}

	[Fact]
	public void Date_FromTimestamp_SameDayInBothTimezones()
	{
		// 2025-01-15T20:00:00Z UTC = 2025-01-15T12:00:00 Pacific — same date
		var result = Eval("DATE(TIMESTAMP '2025-01-15T20:00:00Z')");
		result.Should().Be(new DateTime(2025, 1, 15));
	}

	[Fact]
	public void Date_FromTimestamp_NonUtc_NoConversion()
	{
		// DATE values (non-UTC kind) should not do timezone conversion
		var result = Eval("DATE(DATE '2025-03-15')");
		result.Should().Be(new DateTime(2025, 3, 15));
	}

	[Fact]
	public void Date_ThreeArgs_StillWorks()
	{
		// DATE(year, month, day) should still work
		var result = Eval("DATE(2025, 3, 15)");
		result.Should().Be(new DateTime(2025, 3, 15));
	}
}
