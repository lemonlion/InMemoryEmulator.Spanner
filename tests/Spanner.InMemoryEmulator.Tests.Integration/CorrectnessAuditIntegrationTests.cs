using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for correctness issues found during audit of EXTRACT, SAFE_ functions,
/// short-circuit evaluation, GENERATE_UUID, DATE_ADD/MONTH, CAST, DIV, and DATE().
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CorrectnessAuditIntegrationTests : IntegrationTestBase
{
	public CorrectnessAuditIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ════════════════════════════════════════════════════════════════
	// 1. DIV() with NUMERIC inputs — should truncate toward zero, not round
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
	//   "Returns the result of integer division of X by Y."
	//   "NUMERIC × NUMERIC → NUMERIC"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Div_Numeric_TruncatesTowardZero()
	{
		// DIV(7.5, 2.0) = truncate(3.75) = 3
		var result = await Eval("DIV(CAST(7.5 AS NUMERIC), CAST(2.0 AS NUMERIC))");
		// The result should be NUMERIC 3
		result!.ToString().Should().Be("3");
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Div_Numeric_NegativeTruncatesTowardZero()
	{
		// DIV(-7.5, 2.0) = truncate(-3.75) = -3 (toward zero, not floor)
		var result = await Eval("DIV(CAST(-7.5 AS NUMERIC), CAST(2.0 AS NUMERIC))");
		result!.ToString().Should().Be("-3");
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Div_Numeric_WholeNumbers()
	{
		// DIV(10.0, 3.0) = truncate(3.333...) = 3
		var result = await Eval("DIV(CAST(10.0 AS NUMERIC), CAST(3.0 AS NUMERIC))");
		result!.ToString().Should().Be("3");
	}

	// ════════════════════════════════════════════════════════════════
	// 2. DATE(timestamp) should convert to default timezone before extracting date
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
	//   "Extracts the DATE from a TIMESTAMP expression. It includes an optional
	//    parameter to specify a time zone. If no time zone is specified, the
	//    default time zone, America/Los_Angeles, is used."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Date_FromTimestamp_UsesDefaultTimezone()
	{
		// 2025-01-16T06:00:00Z in UTC = 2025-01-15T22:00:00 in America/Los_Angeles
		// So DATE(timestamp) should return 2025-01-15 (the LA date), not 2025-01-16
		var result = await Eval("DATE(TIMESTAMP '2025-01-16T06:00:00Z')");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(2025, 1, 15));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Date_FromTimestamp_SameDayInDefaultTimezone()
	{
		// 2025-01-15T20:00:00Z in UTC = 2025-01-15T12:00:00 in America/Los_Angeles
		// Should return 2025-01-15 (same day in LA)
		var result = await Eval("DATE(TIMESTAMP '2025-01-15T20:00:00Z')");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(2025, 1, 15));
	}

	// ════════════════════════════════════════════════════════════════
	// 3. Short-circuit evaluation — IF, CASE WHEN, COALESCE, IFNULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task If_ShortCircuits_DoesNotEvaluateDivByZero()
	{
		// IF(false, 1/0, 1) should return 1 without evaluating 1/0
		var result = await Eval("IF(false, 1/0, 1)");
		Convert.ToInt64(result).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task CaseWhen_ShortCircuits_DoesNotEvaluateLaterBranches()
	{
		// CASE WHEN 1=1 THEN 'a' WHEN 1/0=0 THEN 'b' END
		// First branch matches, so 1/0 should never be evaluated
		var result = await Eval("CASE WHEN 1=1 THEN 'a' WHEN 1/0=0 THEN 'b' END");
		result.Should().Be("a");
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Coalesce_ShortCircuits_DoesNotEvaluateAfterNonNull()
	{
		// COALESCE(1, 1/0) — first arg is non-null, should return 1 without evaluating 1/0
		var result = await Eval("COALESCE(1, 1/0)");
		Convert.ToInt64(result).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task IfNull_ShortCircuits_DoesNotEvaluateSecondArg()
	{
		// IFNULL(1, 1/0) — first arg is non-null, should return 1
		var result = await Eval("IFNULL(1, 1/0)");
		Convert.ToInt64(result).Should().Be(1L);
	}

	// ════════════════════════════════════════════════════════════════
	// 4. SAFE_ functions — return NULL on overflow/error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		var result = await Eval("SAFE_DIVIDE(1, 0)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeAdd_Int64Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_ADD(9223372036854775807, 1)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeNegate_Int64Min_ReturnsNull()
	{
		var result = await Eval("SAFE_NEGATE(-9223372036854775808)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeMultiply_Int64Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_MULTIPLY(9223372036854775807, 2)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeSubtract_Int64Overflow_ReturnsNull()
	{
		var result = await Eval("SAFE_SUBTRACT(-9223372036854775808, 1)");
		result.Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 5. GENERATE_UUID() returns valid UUIDv4
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#generate_uuid
	//   "Returns a random universally unique identifier (UUID) as a STRING."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task GenerateUuid_ReturnsValidUuidV4()
	{
		var result = await Eval("GENERATE_UUID()");
		result.Should().BeOfType<string>();
		var uuid = (string)result!;
		Guid.TryParse(uuid, out _).Should().BeTrue("should be a valid UUID");
		// UUID should be lowercase with hyphens
		uuid.Should().MatchRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
			"should be a valid UUIDv4 string (lowercase, version=4, variant=1)");
	}

	// ════════════════════════════════════════════════════════════════
	// 6. DATE_ADD with MONTH — end-of-month overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	//   "Special handling is needed due to differing number of days per month."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task DateAdd_Month_EndOfMonthOverflow_ReturnsFeb28()
	{
		// Jan 31 + 1 MONTH = Feb 28 (or 29 in leap year)
		var result = await Eval("DATE_ADD(DATE '2025-01-31', INTERVAL 1 MONTH)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(2025, 2, 28));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task DateAdd_Month_EndOfMonthOverflow_LeapYear()
	{
		// Jan 31 + 1 MONTH in leap year = Feb 29
		var result = await Eval("DATE_ADD(DATE '2024-01-31', INTERVAL 1 MONTH)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(2024, 2, 29));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task DateAdd_Month_March31_ReturnApril30()
	{
		// Mar 31 + 1 MONTH = Apr 30 (April has only 30 days)
		var result = await Eval("DATE_ADD(DATE '2025-03-31', INTERVAL 1 MONTH)");
		var dt = (DateTime)result!;
		dt.Should().Be(new DateTime(2025, 4, 30));
	}

	// ════════════════════════════════════════════════════════════════
	// 7. CAST correctness
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Cast_StringToNumeric()
	{
		var result = await Eval("CAST('123.456' AS NUMERIC)");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("123.456");
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Cast_StringToDate()
	{
		var result = await Eval("CAST('2025-03-15' AS DATE)");
		((DateTime)result!).Should().Be(new DateTime(2025, 3, 15));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Cast_StringToTimestamp()
	{
		var result = await Eval("CAST('2025-03-15T12:30:00Z' AS TIMESTAMP)");
		((DateTime)result!).Should().Be(new DateTime(2025, 3, 15, 12, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Cast_StringToBytes()
	{
		var result = await Eval("CAST('hello' AS BYTES)");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().BeEquivalentTo(System.Text.Encoding.UTF8.GetBytes("hello"));
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task SafeCast_InvalidConversion_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('notanumber' AS INT64)");
		result.Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 8. EXTRACT date parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_DayOfWeek_SundayIs1()
	{
		// 2025-05-04 is a Sunday → DAYOFWEEK = 1
		var result = await Eval("EXTRACT(DAYOFWEEK FROM DATE '2025-05-04')");
		Convert.ToInt64(result).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_DayOfWeek_SaturdayIs7()
	{
		// 2025-05-03 is a Saturday → DAYOFWEEK = 7
		var result = await Eval("EXTRACT(DAYOFWEEK FROM DATE '2025-05-03')");
		Convert.ToInt64(result).Should().Be(7L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_DayOfYear()
	{
		// Feb 1 = day 32
		var result = await Eval("EXTRACT(DAYOFYEAR FROM DATE '2025-02-01')");
		Convert.ToInt64(result).Should().Be(32L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_IsoWeek()
	{
		// 2025-01-06 is Monday of ISO week 2
		var result = await Eval("EXTRACT(ISOWEEK FROM DATE '2025-01-06')");
		Convert.ToInt64(result).Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_IsoYear_CrossYearBoundary()
	{
		// 2024-12-30 is in ISO year 2025 (ISO week 1 of 2025 starts Dec 30, 2024)
		var result = await Eval("EXTRACT(ISOYEAR FROM DATE '2024-12-30')");
		Convert.ToInt64(result).Should().Be(2025L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_Week_BeforeFirstSunday_ReturnsZero()
	{
		// 2025-01-01 is a Wednesday. First Sunday is Jan 5. So Jan 1 is week 0.
		var result = await Eval("EXTRACT(WEEK FROM DATE '2025-01-01')");
		Convert.ToInt64(result).Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task Extract_Week_FirstSunday_ReturnsOne()
	{
		// 2025-01-05 is the first Sunday → week 1
		var result = await Eval("EXTRACT(WEEK FROM DATE '2025-01-05')");
		Convert.ToInt64(result).Should().Be(1L);
	}

	// ════════════════════════════════════════════════════════════════
	// 9. PENDING_COMMIT_TIMESTAMP() in DML
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#pending_commit_timestamp
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "CorrectnessAudit")]
	public async Task PendingCommitTimestamp_InDmlInsert()
	{
		var t = $"PCT_{Guid.NewGuid().ToString("N")[..8]}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Ts TIMESTAMP OPTIONS(allow_commit_timestamp=true)) PRIMARY KEY (Id)");

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var cmd = connection.CreateDmlCommand(
			$"INSERT INTO {t} (Id, Ts) VALUES (1, PENDING_COMMIT_TIMESTAMP())");
		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Ts FROM {t} WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Ts"].Should().NotBeNull("PENDING_COMMIT_TIMESTAMP should produce a timestamp");
		var ts = (DateTime)rows[0]["Ts"]!;
		ts.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
	}
}
