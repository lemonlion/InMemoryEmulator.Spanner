using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for edge-case bugs discovered during audit.
/// Each test is written to surface a specific bug via TDD red-green-refactor.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class EdgeCaseBugIntegrationTests : IntegrationTestBase
{
	public EdgeCaseBugIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	private async Task<string> FreshTable(string prefix)
	{
		var t = $"{prefix}_{Guid.NewGuid().ToString("N")[..8]}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, Category STRING(MAX)) PRIMARY KEY (Id)");
		return t;
	}

	// ════════════════════════════════════════════════════════════════
	// 1. Mutation Update must preserve omitted columns
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
	//   "Update columns ... Only values for the listed columns will be updated."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task UpdateMutation_OmittedColumnsPreserved()
	{
		var t = await FreshTable("UpdOmit");
		// Insert a row with all columns populated
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 100L, ["Category"] = "A"
		});

		// Update only Name — Val and Category should be preserved
		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateUpdateCommand(t);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Bob");
		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name, Val, Category FROM {t} WHERE Id = 1");
		rows.Should().HaveCount(1);
		((string)rows[0]["Name"]!).Should().Be("Bob");
		((long)rows[0]["Val"]!).Should().Be(100L, "omitted column Val should be preserved");
		((string)rows[0]["Category"]!).Should().Be("A", "omitted column Category should be preserved");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task InsertOrUpdateMutation_UpdatePath_OmittedColumnsPreserved()
	{
		var t = await FreshTable("UpsOmit");
		// Insert a row with all columns populated
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 200L, ["Category"] = "B"
		});

		// InsertOrUpdate with only Id + Name — should update existing, preserving Val/Category
		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateInsertOrUpdateCommand(t);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Name", SpannerDbType.String, "Charlie");
		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name, Val, Category FROM {t} WHERE Id = 1");
		rows.Should().HaveCount(1);
		((string)rows[0]["Name"]!).Should().Be("Charlie");
		((long)rows[0]["Val"]!).Should().Be(200L, "omitted column Val should be preserved on upsert update path");
		((string)rows[0]["Category"]!).Should().Be("B", "omitted column Category should be preserved on upsert update path");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task UpdateMutation_ExplicitNullOverwritesExistingValue()
	{
		var t = await FreshTable("UpdExNull");
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 100L, ["Category"] = "A"
		});

		// Explicitly set Val to NULL — this SHOULD overwrite
		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateUpdateCommand(t);
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Val", SpannerDbType.Int64, DBNull.Value);
		await cmd.ExecuteNonQueryAsync();

		var rows = await QueryAsync($"SELECT Name, Val, Category FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().BeNull("explicitly setting Val to NULL should overwrite");
		((string)rows[0]["Name"]!).Should().Be("Alice", "Name should be preserved");
		((string)rows[0]["Category"]!).Should().Be("A", "Category should be preserved");
	}

	// ════════════════════════════════════════════════════════════════
	// 2. INT64 arithmetic overflow should error, not wrap
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	//   Overflow produces an error
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Int64_Addition_Overflow_ShouldError()
	{
		var act = async () => await QueryAsync("SELECT 9223372036854775807 + 1 AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Int64_Subtraction_Overflow_ShouldError()
	{
		var act = async () => await QueryAsync("SELECT CAST(-9223372036854775808 AS INT64) - 1 AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Int64_Multiplication_Overflow_ShouldError()
	{
		var act = async () => await QueryAsync("SELECT 9223372036854775807 * 2 AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 3. FLOAT64 division by zero should return an error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	//   "Divide by zero operations return an error. To return a different result,
	//    consider the IEEE_DIVIDE or SAFE_DIVIDE functions."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Float64_DivisionByZero_ReturnsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(1.0 AS FLOAT64) / CAST(0.0 AS FLOAT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Float64_NegDivisionByZero_ReturnsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(-1.0 AS FLOAT64) / CAST(0.0 AS FLOAT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Float64_ZeroDivZero_ReturnsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(0.0 AS FLOAT64) / CAST(0.0 AS FLOAT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Float64_ModByZero_ReturnsError()
	{
		var act = async () => await QueryAsync("SELECT MOD(CAST(5.0 AS FLOAT64), CAST(0.0 AS FLOAT64)) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Int64_DivisionByZero_ShouldError()
	{
		var act = async () => await QueryAsync("SELECT 10 / 0 AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Int64_ModByZero_ShouldError()
	{
		var act = async () => await QueryAsync("SELECT MOD(10, 0) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 4. LIKE pattern escape sequences
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   "The LIKE operator supports the following escape sequences:
	//    \% specifies a single percent sign, \_ specifies a single underscore"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Like_EscapedPercent_MatchesLiteralPercent()
	{
		var t = await FreshTable("LikeEsc");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "100%", ["Val"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "100x", ["Val"] = 2L });

		// \% should match literal % — only row 1 matches
		var rows = await QueryAsync($@"SELECT Id FROM {t} WHERE Name LIKE '100\%' ORDER BY Id");
		rows.Should().HaveCount(1);
		((long)rows[0]["Id"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Like_EscapedUnderscore_MatchesLiteralUnderscore()
	{
		var t = await FreshTable("LikeEsc");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a_b", ["Val"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "axb", ["Val"] = 2L });

		// \_ should match literal _ — only row 1 matches
		var rows = await QueryAsync($@"SELECT Id FROM {t} WHERE Name LIKE 'a\_b' ORDER BY Id");
		rows.Should().HaveCount(1);
		((long)rows[0]["Id"]!).Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Like_EscapedBackslash_MatchesLiteralBackslash()
	{
		var t = await FreshTable("LikeEsc");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = @"a\b", ["Val"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "axb", ["Val"] = 2L });

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
		//   String literal:  '\\\\' → unescape → '\\' (one backslash pair)
		//   LIKE pattern:    '\\' → matches literal backslash
		// So 'a\\\\b' matches string "a\b"
		var rows = await QueryAsync($@"SELECT Id FROM {t} WHERE Name LIKE 'a\\\\b' ORDER BY Id");
		rows.Should().HaveCount(1);
		((long)rows[0]["Id"]!).Should().Be(1L);
	}

	// ════════════════════════════════════════════════════════════════
	// 5. CAST TIMESTAMP to STRING should preserve sub-second precision
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task CastTimestamp_ToString_PreservesSubSecondPrecision()
	{
		var rows = await QueryAsync("SELECT CAST(TIMESTAMP '2024-01-15T12:34:56.123456Z' AS STRING) AS R");
		var r = (string)rows[0]["R"]!;
		r.Should().Contain(".123456", "sub-second precision should be preserved");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CastTimestamp_ToString_WholeSecondsOmitsFraction()
	{
		// 2024-01-15 12:34:56 UTC → 04:34:56-08 PST (no fractional seconds)
		var rows = await QueryAsync("SELECT CAST(TIMESTAMP '2024-01-15T12:34:56Z' AS STRING) AS R");
		var r = (string)rows[0]["R"]!;
		r.Should().Contain("04:34:56");
		r.Should().NotContain(".");
	}

	// ════════════════════════════════════════════════════════════════
	// 6. Negative LIMIT/OFFSET should error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#limit_and_offset_clause
	//   "LIMIT count ... count is a non-negative integer"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Negative_Limit_ShouldError()
	{
		var t = await FreshTable("NegLim");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 1L });

		var act = async () => await QueryAsync($"SELECT * FROM {t} LIMIT -1");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Negative_Offset_ShouldError()
	{
		var t = await FreshTable("NegOff");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 1L });

		var act = async () => await QueryAsync($"SELECT * FROM {t} LIMIT 10 OFFSET -1");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 7. NULL propagation in arithmetic
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	//   "Any operation with a NULL input returns NULL"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Arithmetic_NullPropagation_Add()
	{
		var rows = await QueryAsync("SELECT CAST(NULL AS INT64) + 1 AS R");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Arithmetic_NullPropagation_Multiply()
	{
		var rows = await QueryAsync("SELECT 5 * CAST(NULL AS INT64) AS R");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Arithmetic_NullPropagation_Divide()
	{
		var rows = await QueryAsync("SELECT CAST(NULL AS INT64) / 2 AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 8. Unary negate of MIN_INT64 should overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task UnaryNegate_MinInt64_ShouldError()
	{
		// -(-9223372036854775808) should overflow
		var act = async () => await QueryAsync(
			"SELECT -x AS R FROM UNNEST([CAST(-9223372036854775808 AS INT64)]) AS x");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 9. SAFE arithmetic functions should return NULL on overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_add
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeAdd_Overflow_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT SAFE_ADD(9223372036854775807, 1) AS R");
		rows[0]["R"].Should().BeNull("SAFE_ADD overflow should return NULL");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeSubtract_Overflow_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT SAFE_SUBTRACT(CAST(-9223372036854775808 AS INT64), 1) AS R");
		rows[0]["R"].Should().BeNull("SAFE_SUBTRACT overflow should return NULL");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeMultiply_Overflow_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT SAFE_MULTIPLY(9223372036854775807, 2) AS R");
		rows[0]["R"].Should().BeNull("SAFE_MULTIPLY overflow should return NULL");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT SAFE_DIVIDE(10, 0) AS R");
		rows[0]["R"].Should().BeNull("SAFE_DIVIDE by zero should return NULL");
	}

	// ════════════════════════════════════════════════════════════════
	// 10. BETWEEN expression
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#between
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_ReturnsCorrectResults()
	{
		var t = await FreshTable("Btwn");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "B", ["Val"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "C", ["Val"] = 30L });

		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Val BETWEEN 15 AND 25 ORDER BY Id");
		rows.Should().HaveCount(1);
		((long)rows[0]["Id"]!).Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_Inclusive()
	{
		var t = await FreshTable("BtwnInc");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "B", ["Val"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "C", ["Val"] = 30L });

		// BETWEEN is inclusive on both ends
		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Val BETWEEN 10 AND 30 ORDER BY Id");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NotBetween_ReturnsCorrectResults()
	{
		var t = await FreshTable("NotBtwn");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "B", ["Val"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "C", ["Val"] = 30L });

		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Val NOT BETWEEN 15 AND 25 ORDER BY Id");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_WithNull_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT CAST(NULL AS INT64) BETWEEN 1 AND 10 AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 6. NaN comparison semantics
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "All comparisons with NaN return FALSE, except for != and <>, which return TRUE."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_EqualToNaN_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) = IEEE_DIVIDE(0.0, 0.0) AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_NotEqualToNaN_ReturnsTrue()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) != IEEE_DIVIDE(0.0, 0.0) AS R");
		rows[0]["R"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_LessThanValue_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) < 5.0 AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_GreaterThanValue_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) > 5.0 AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_EqualToValue_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) = 5.0 AS R");
		rows[0]["R"].Should().Be(false);
	}

	// ════════════════════════════════════════════════════════════════
	// 7. BETWEEN three-valued logic
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "X [NOT] BETWEEN Y AND Z is equivalent to Y <= X AND X <= Z"
	//   Three-valued AND: FALSE AND NULL = FALSE
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_NullLow_ValueAboveHigh_ReturnsFalse()
	{
		// 100 BETWEEN NULL AND 10 → (NULL <= 100) AND (100 <= 10) → NULL AND FALSE → FALSE
		var rows = await QueryAsync("SELECT 100 BETWEEN CAST(NULL AS INT64) AND 10 AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_NullHigh_ValueBelowLow_ReturnsFalse()
	{
		// 1 BETWEEN 10 AND NULL → (10 <= 1) AND (1 <= NULL) → FALSE AND NULL → FALSE
		var rows = await QueryAsync("SELECT 1 BETWEEN 10 AND CAST(NULL AS INT64) AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Between_NullLow_ValueInRange_ReturnsNull()
	{
		// 5 BETWEEN NULL AND 10 → (NULL <= 5) AND (5 <= 10) → NULL AND TRUE → NULL
		var rows = await QueryAsync("SELECT 5 BETWEEN CAST(NULL AS INT64) AND 10 AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 8. FORMAT with NULL arguments
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	//   If any argument is NULL, the entire FORMAT result is NULL.
	//   Verified against real Cloud Spanner.
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Format_NullIntArg_ProducesNull()
	{
		var rows = await QueryAsync("SELECT FORMAT('%d', CAST(NULL AS INT64)) AS R");
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Format_NullFloatArg_ProducesNull()
	{
		var rows = await QueryAsync("SELECT FORMAT('%f', CAST(NULL AS FLOAT64)) AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 9. NaN equality semantics in BETWEEN, IN, CASE, NULLIF
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
	//   "All comparisons with NaN return FALSE, except for != and <>, which return TRUE."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_Between_ReturnsFalse()
	{
		// 100 BETWEEN NaN AND 200 → (NaN <= 100) AND (100 <= 200) → FALSE AND TRUE → FALSE
		var rows = await QueryAsync("SELECT 100 BETWEEN IEEE_DIVIDE(0.0, 0.0) AND 200 AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_InList_ReturnsFalse()
	{
		var rows = await QueryAsync("SELECT IEEE_DIVIDE(0.0, 0.0) IN (IEEE_DIVIDE(0.0, 0.0), 1.0) AS R");
		rows[0]["R"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_SimpleCase_NoMatch()
	{
		var rows = await QueryAsync("SELECT CASE IEEE_DIVIDE(0.0, 0.0) WHEN IEEE_DIVIDE(0.0, 0.0) THEN 'match' ELSE 'no' END AS R");
		rows[0]["R"].Should().Be("no");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task NaN_Nullif_ReturnsNaN()
	{
		var rows = await QueryAsync("SELECT NULLIF(IEEE_DIVIDE(0.0, 0.0), IEEE_DIVIDE(0.0, 0.0)) AS R");
		var r = rows[0]["R"];
		r.Should().NotBeNull();
		r.Should().BeOfType<double>();
		double.IsNaN((double)r!).Should().BeTrue();
	}

	// ════════════════════════════════════════════════════════════════
	// 10. PARSE_TIMESTAMP/PARSE_DATE with NULL format
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	//   NULL input → NULL output
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ParseTimestamp_NullFormat_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT PARSE_TIMESTAMP(CAST(NULL AS STRING), '2023-01-01') AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// 11. GENERATE_ARRAY with NaN arguments
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
	//   "Returns an error if any argument is a NaN."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task GenerateArray_NaNStep_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT GENERATE_ARRAY(1, 5, CAST('nan' AS FLOAT64)) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task GenerateArray_NaNStart_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT GENERATE_ARRAY(CAST('nan' AS FLOAT64), 5, 1) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 12. TIMESTAMP_DIFF should NOT support MONTH or YEAR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
	//   granularity only supports: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampDiff_Month_ThrowsError()
	{
		var act = async () => await QueryAsync(
			"SELECT TIMESTAMP_DIFF(TIMESTAMP '2025-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', MONTH) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampDiff_Year_ThrowsError()
	{
		var act = async () => await QueryAsync(
			"SELECT TIMESTAMP_DIFF(TIMESTAMP '2025-01-01T00:00:00Z', TIMESTAMP '2024-01-01T00:00:00Z', YEAR) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 13. DATE_DIFF boundary-counting for WEEK/ISOWEEK/QUARTER/ISOYEAR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
	//   WEEK counts Sunday boundaries; ISOWEEK counts Monday boundaries
	// ════════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	// WEEK: Sunday boundaries — Oct 14 (Sat) → Oct 15 (Sun) = 1 boundary
	[InlineData("DATE_DIFF(DATE '2017-10-15', DATE '2017-10-14', WEEK)", 1L)]
	// WEEK: Mon→Sat same week = 0
	[InlineData("DATE_DIFF(DATE '2017-12-18', DATE '2017-12-17', WEEK)", 0L)]
	// ISOWEEK: Monday boundaries — Sun→Mon = 1; Mon→Sun same week = 0
	[InlineData("DATE_DIFF(DATE '2017-12-18', DATE '2017-12-17', ISOWEEK)", 1L)]
	// QUARTER: Jan 1 to Apr 1 = 1 quarter boundary
	[InlineData("DATE_DIFF(DATE '2024-04-01', DATE '2024-01-01', QUARTER)", 1L)]
	// QUARTER: within same quarter = 0
	[InlineData("DATE_DIFF(DATE '2024-03-31', DATE '2024-01-01', QUARTER)", 0L)]
	// ISOYEAR: per docs 2017-12-30 to 2014-12-30 = 2 (not 3)
	[InlineData("DATE_DIFF(DATE '2017-12-30', DATE '2014-12-30', ISOYEAR)", 2L)]
	public async Task DateDiff_BoundaryCounting(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ════════════════════════════════════════════════════════════════
	// 14. CAST hex string to INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	//   "Hexadecimal values ... CAST('0x1A' AS INT64)"
	// ════════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[InlineData("CAST('0x1A' AS INT64)", 26L)]
	[InlineData("CAST('0xff' AS INT64)", 255L)]
	[InlineData("CAST('0x0' AS INT64)", 0L)]
	public async Task Cast_HexString_ToInt64(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ════════════════════════════════════════════════════════════════
	// 15. ORDER BY ... NULLS FIRST / NULLS LAST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
	//   "NULLS FIRST | NULLS LAST"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task OrderBy_NullsLast_AscPutsNullsAtEnd()
	{
		var t = await FreshTable("nullord");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });
		var rows = await QueryAsync($"SELECT Id FROM {t} ORDER BY Val ASC NULLS LAST");
		rows.Select(r => (long)r["Id"]!).Should().Equal(3L, 1L, 2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task OrderBy_NullsFirst_DescPutsNullsAtStart()
	{
		var t = await FreshTable("nullord");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });
		var rows = await QueryAsync($"SELECT Id FROM {t} ORDER BY Val DESC NULLS FIRST");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 1L, 3L);
	}

	// ════════════════════════════════════════════════════════════════
	// 16. SELECT * EXCEPT / SELECT * REPLACE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_except_clause
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SelectStar_Except_ExcludesColumns()
	{
		var t = await FreshTable("stex");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 42L });
		var rows = await QueryAsync($"SELECT * EXCEPT (Val, Category) FROM {t}");
		rows[0].Should().ContainKey("Id").And.ContainKey("Name");
		rows[0].Should().NotContainKey("Val").And.NotContainKey("Category");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SelectStar_Replace_SubstitutesExpression()
	{
		var t = await FreshTable("strp");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 10L });
		var rows = await QueryAsync($"SELECT * REPLACE (Val * 2 AS Val) FROM {t}");
		rows[0]["Val"].Should().Be(20L);
		rows[0]["Name"].Should().Be("Alice");
	}

	// ════════════════════════════════════════════════════════════════
	// v1.0.58 — SAFE_DIVIDE NaN, REPEAT negative, IGNORE NULLS, ARRAY_INCLUDES
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeDivide_NanInput_ReturnsNan()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
		//   "All mathematical functions return NaN if any of the arguments is NaN."
		var result = await Eval("SAFE_DIVIDE(CAST('nan' AS FLOAT64), 2.0)");
		result.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeDivide_NanDivisor_ReturnsNan()
	{
		var result = await Eval("SAFE_DIVIDE(10.0, CAST('nan' AS FLOAT64))");
		result.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeDivide_InfinityDividedByFinite_ReturnsInfinity()
	{
		// Infinity / 2 is valid IEEE — not an error
		var result = await Eval("SAFE_DIVIDE(CAST('+inf' AS FLOAT64), 2.0)");
		result.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeDivide_InfinityDividedByInfinity_ReturnsNaN()
	{
		// Inf/Inf is indeterminate — produces NaN (a valid IEEE result, not an error).
		// Verified against real Cloud Spanner.
		var result = await Eval("SAFE_DIVIDE(CAST('+inf' AS FLOAT64), CAST('+inf' AS FLOAT64))");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Repeat_NegativeCount_ThrowsError()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
		//   "This function returns an error if the repetitions value is negative."
		var act = async () => await QueryAsync("SELECT REPEAT('abc', -1) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Repeat_ZeroCount_ReturnsEmpty()
	{
		var result = await Eval("REPEAT('abc', 0)");
		result.Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task FirstValue_IgnoreNulls_SkipsNullRows()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions#first_value
		//   "IGNORE NULLS: ... all NULL values of expr are excluded from the calculation."
		var t = await FreshTable("fvin");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a", ["Val"] = (long?)null });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "b", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "c", ["Val"] = 20L });
		var rows = await QueryAsync($"SELECT Id, FIRST_VALUE(Val IGNORE NULLS) OVER (ORDER BY Id) AS FV FROM {t} ORDER BY Id");
		// First non-null Val is 10 (Id=2). Before seeing it (Id=1), FIRST_VALUE IGNORE NULLS returns NULL.
		rows[0]["FV"].Should().BeNull();
		((long)rows[1]["FV"]!).Should().Be(10L);
		((long)rows[2]["FV"]!).Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task LastValue_IgnoreNulls_SkipsNullRows()
	{
		var t = await FreshTable("lvin");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "b", ["Val"] = (long?)null });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "c", ["Val"] = 30L });
		var rows = await QueryAsync($"SELECT Id, LAST_VALUE(Val IGNORE NULLS) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS LV FROM {t} ORDER BY Id");
		// LAST_VALUE IGNORE NULLS with frame up to current row
		((long)rows[0]["LV"]!).Should().Be(10L);
		((long)rows[1]["LV"]!).Should().Be(10L); // skips null at Id=2
		((long)rows[2]["LV"]!).Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayIncludes_NumericTypeCoercion_MatchesCorrectly()
	{
		// ARRAY_INCLUDES should use value comparison, not Object.Equals
		var result = await Eval("ARRAY_INCLUDES([1, 2, 3], 2)");
		result.Should().Be(true);
	}

	// ════════════════════════════════════════════════════════════════
	// 10. SAFE_ADD/SAFE_SUBTRACT/SAFE_MULTIPLY NaN passthrough
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	//   "All mathematical functions return NaN if any of the arguments is NaN."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeAdd_NanInput_ReturnsNan()
	{
		var result = await Eval("SAFE_ADD(CAST('nan' AS FLOAT64), 1.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeSubtract_NanInput_ReturnsNan()
	{
		var result = await Eval("SAFE_SUBTRACT(10.0, CAST('nan' AS FLOAT64))");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeMultiply_NanInput_ReturnsNan()
	{
		var result = await Eval("SAFE_MULTIPLY(CAST('nan' AS FLOAT64), 5.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeAdd_InfinityPlusFinite_ReturnsInfinity()
	{
		// Inf + finite is not overflow — the input was already Inf
		var result = await Eval("SAFE_ADD(CAST('+inf' AS FLOAT64), 1.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeSubtract_InfinityMinusFinite_ReturnsInfinity()
	{
		var result = await Eval("SAFE_SUBTRACT(CAST('+inf' AS FLOAT64), 1.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeMultiply_InfinityTimesFinite_ReturnsInfinity()
	{
		var result = await Eval("SAFE_MULTIPLY(CAST('+inf' AS FLOAT64), 2.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeAdd_InfinityPlusNegInfinity_ReturnsNaN()
	{
		// Inf + (-Inf) = NaN (indeterminate form) — valid IEEE result.
		// Verified against real Cloud Spanner.
		var result = await Eval("SAFE_ADD(CAST('+inf' AS FLOAT64), CAST('-inf' AS FLOAT64))");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeMultiply_InfinityTimesZero_ReturnsNaN()
	{
		// Inf * 0 = NaN (indeterminate form) — valid IEEE result.
		// Verified against real Cloud Spanner.
		var result = await Eval("SAFE_MULTIPLY(CAST('+inf' AS FLOAT64), 0.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	// ════════════════════════════════════════════════════════════════
	// 11. SIGN(NaN) should return NaN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign
	//   "| NaN | NaN |"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Sign_NaN_ReturnsNan()
	{
		var result = await Eval("SIGN(CAST('nan' AS FLOAT64))");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	// ════════════════════════════════════════════════════════════════
	// 12. GREATEST/LEAST with NaN should return NaN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
	//   "in the case of floating-point arguments, if any argument is NaN, returns NaN"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Greatest_WithNaN_ReturnsNan()
	{
		var result = await Eval("GREATEST(1.0, CAST('nan' AS FLOAT64), 3.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Least_WithNaN_ReturnsNan()
	{
		var result = await Eval("LEAST(1.0, CAST('nan' AS FLOAT64), 3.0)");
		result.Should().BeOfType<double>().Which.Should().Be(double.NaN);
	}

	// ════════════════════════════════════════════════════════════════
	// 13. POW error cases
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#pow
	//   "| Finite value < 0 | Non-integer | Error |"
	//   "| 0 | Finite value < 0 | Error |"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Pow_NegativeBaseNonIntegerExponent_ThrowsError()
	{
		var act = () => Eval("POW(-2.0, 0.5)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Pow_ZeroBaseNegativeExponent_ThrowsError()
	{
		var act = () => Eval("POW(0.0, -1.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// 14. EXP overflow should throw error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#exp
	//   "Generates an error if the result overflows."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Exp_Overflow_ThrowsError()
	{
		// EXP(710) overflows to Infinity for FLOAT64
		var act = () => Eval("EXP(710.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Exp_NegativeInfinity_ReturnsZero()
	{
		// Ref: "| -inf | 0.0 |"
		var result = await Eval("EXP(CAST('-inf' AS FLOAT64))");
		result.Should().BeOfType<double>().Which.Should().Be(0.0);
	}

	// ════════════════════════════════════════════════════════════════
	// 15. REGEXP_REPLACE backreferences use \1 not $1
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
	//   "You can use backslashed-escaped digits (\1 to \9) within the replacement"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RegexpReplace_Backreference_WorksCorrectly()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
		//   "You can use backslashed-escaped digits (\1 to \9) within the replacement"
		//   RE2 supports \w in patterns.
		// Swap first and last word using backreferences
		var result = await Eval(@"REGEXP_REPLACE('hello world', '(\w+) (\w+)', '\\2 \\1')");
		result.Should().Be("world hello");
	}

	// ════════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD / TIMESTAMP_SUB reject WEEK and QUARTER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	//   "Supported date parts: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampAdd_Week_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 WEEK) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampAdd_Quarter_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT TIMESTAMP_ADD(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 QUARTER) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampSub_Week_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT TIMESTAMP_SUB(TIMESTAMP '2024-01-01T00:00:00Z', INTERVAL 1 WEEK) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// DATE_ADD / DATE_SUB reject sub-day parts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
	//   "Supported date parts: DAY, WEEK, MONTH, QUARTER, YEAR"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task DateAdd_Hour_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT DATE_ADD(DATE '2024-01-01', INTERVAL 1 HOUR) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task DateAdd_Second_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT DATE_ADD(DATE '2024-01-01', INTERVAL 1 SECOND) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task DateSub_Microsecond_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT DATE_SUB(DATE '2024-01-01', INTERVAL 1 MICROSECOND) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// JSON_VALUE returns NULL for non-scalar (objects and arrays)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
	//   "Extracts a JSON scalar value and converts it to a SQL STRING value.
	//    If json_string_expr is ... not a scalar value, then NULL is returned."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task JsonValue_Object_ReturnsNull()
	{
		var result = await Eval(@"JSON_VALUE('{""a"": {""b"": 1}}', '$.a')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task JsonValue_Array_ReturnsNull()
	{
		var result = await Eval(@"JSON_VALUE('{""a"": [1,2,3]}', '$.a')");
		result.Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// FORMAT_TIMESTAMP with timezone parameter
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
	//   "FORMAT_TIMESTAMP(format_string, timestamp[, time_zone])"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task FormatTimestamp_WithTimezone_ConvertsCorrectly()
	{
		// UTC midnight should be 7pm previous day in America/Los_Angeles (UTC-7 in summer, UTC-8 in winter)
		// January = UTC-8, so 2024-01-15 00:00:00 UTC = 2024-01-14 16:00:00 PST
		var result = await Eval(@"FORMAT_TIMESTAMP('%Y-%m-%d %H:%M:%S', TIMESTAMP '2024-01-15T00:00:00Z', 'America/Los_Angeles')");
		result.Should().Be("2024-01-14 16:00:00");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task FormatTimestamp_WithUtcTimezone_FormatsInUtc()
	{
		var result = await Eval(@"FORMAT_TIMESTAMP('%Y-%m-%d %H:%M:%S', TIMESTAMP '2024-01-15T12:30:00Z', 'UTC')");
		result.Should().Be("2024-01-15 12:30:00");
	}

	// ════════════════════════════════════════════════════════════════
	// STARTS_WITH / ENDS_WITH with BYTES type
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
	//   "Takes two STRING or BYTES values."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task StartsWith_Bytes_WorksCorrectly()
	{
		var result = await Eval(@"STARTS_WITH(b'\x01\x02\x03', b'\x01\x02')");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task StartsWith_Bytes_ReturnsFalse()
	{
		var result = await Eval(@"STARTS_WITH(b'\x01\x02\x03', b'\x02\x03')");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task EndsWith_Bytes_WorksCorrectly()
	{
		var result = await Eval(@"ENDS_WITH(b'\x01\x02\x03', b'\x02\x03')");
		result.Should().Be(true);
	}

	// ════════════════════════════════════════════════════════════════
	// PARSE_TIMESTAMP with timezone parameter
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
	//   "PARSE_TIMESTAMP(format_string, timestamp_string[, time_zone])"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ParseTimestamp_WithTimezone_ConvertsToUtc()
	{
		// 16:00 in America/Los_Angeles (PST = UTC-8 in January) should become 2024-01-15 00:00:00 UTC
		var result = await Eval(@"FORMAT_TIMESTAMP('%Y-%m-%d %H:%M:%S', PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', '2024-01-14 16:00:00', 'America/Los_Angeles'), 'UTC')");
		result.Should().Be("2024-01-15 00:00:00");
	}

	// ════════════════════════════════════════════════════════════════
	// CAST(NaN/Infinity AS INT64) should throw error
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Cast_NaN_AsInt64_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(CAST('nan' AS FLOAT64) AS INT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Cast_Infinity_AsInt64_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(CAST('inf' AS FLOAT64) AS INT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Cast_LargeDouble_AsInt64_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT CAST(1.0e19 AS INT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task SafeCast_NaN_AsInt64_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST(CAST('nan' AS FLOAT64) AS INT64)");
		result.Should().BeNull();
	}

	// ════════════════════════════════════════════════════════════════
	// Uncaught exceptions (FormatException etc.) should return proper errors
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#google.rpc.Code
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Cast_EmptyString_AsInt64_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT CAST('' AS INT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Cast_InvalidString_AsFloat64_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT CAST('abc' AS FLOAT64) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// Partitioned DML: INSERT is not supported
	// Ref: https://cloud.google.com/spanner/docs/dml-partitioned
	//   "INSERT is not supported."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task PartitionedDml_Insert_ThrowsError()
	{
		var t = await FreshTable("PdmlIns");
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		var cmd = connection.CreateDmlCommand($"INSERT INTO {t} (Id, Name) VALUES (1, 'Alice')");
		var act = async () => await cmd.ExecutePartitionedUpdateAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// FORMAT_TIMESTAMP with %E9S (nanosecond precision) should not crash
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/format-elements#format_elements_date_time
	//   "%E9S" — seconds with up to 9 fractional digits
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task FormatTimestamp_E9S_DoesNotCrash()
	{
		var result = await Eval("FORMAT_TIMESTAMP('%E9S', TIMESTAMP '2020-01-01 12:30:45.123456789Z')");
		result.Should().NotBeNull();
		var s = result!.ToString()!;
		// Should start with "45." (the seconds) and have fractional digits
		s.Should().StartWith("45.");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task FormatTimestamp_E4Y_DoesNotCrash()
	{
		var result = await Eval("FORMAT_TIMESTAMP('%E4Y', TIMESTAMP '2020-06-15 00:00:00Z')");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("2020");
	}

	// ════════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD overflow should give meaningful error, not crash
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	//   Adding extreme values should produce an error, not an unhandled exception
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampAdd_Overflow_ThrowsError()
	{
		var act = async () => await QueryAsync(
			"SELECT TIMESTAMP_ADD(TIMESTAMP '9999-12-31 23:59:59Z', INTERVAL 1000000000 DAY) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// BYTES support for string functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length
	//   "Returns the length of the STRING or BYTES value."
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
	//   "Concatenates one or more STRING or BYTES values into a single result."
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	//   "Gets a portion of a STRING or BYTES value."
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
	//   "|| is overloaded: STRING || STRING, BYTES || BYTES, ARRAY<T> || ARRAY<T>"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Length_Bytes_ReturnsByteCount()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#length
		//   "If value is BYTES, returns the number of bytes."
		var result = await Eval("LENGTH(b'hello')");
		result.Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Length_BytesFromCast_ReturnsByteCount()
	{
		var result = await Eval("LENGTH(CAST('abc' AS BYTES))");
		result.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Concat_Bytes_ConcatenatesByteValues()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#concat
		//   "Concatenates one or more values into a single result. All values must be BYTES or STRING."
		var result = await Eval("CONCAT(b'hello', b' world')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello world");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Substr_Bytes_ExtractsSubsequence()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
		//   "Gets a portion of a STRING or BYTES value."
		var result = await Eval("SUBSTR(b'hello world', 1, 5)");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Substr_Bytes_FromPositionToEnd()
	{
		var result = await Eval("SUBSTR(b'hello world', 7)");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("world");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ConcatOperator_Bytes_ConcatenatesCorrectly()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
		//   "BYTES || BYTES → BYTES"
		var result = await Eval("b'hello' || b' world'");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello world");
	}

	// ════════════════════════════════════════════════════════════════
	// REVERSE for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#reverse
	//   "Reverses a STRING or BYTES value."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Reverse_Bytes_ReversesCorrectly()
	{
		var result = await Eval("REVERSE(b'hello')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("olleh");
	}

	// ════════════════════════════════════════════════════════════════
	// REPLACE for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
	//   "Replaces all occurrences of from_value with to_value in original_value.
	//    If from_value is empty, returns original_value."
	//   Works on both STRING and BYTES.
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Replace_Bytes_ReplacesCorrectly()
	{
		var result = await Eval("REPLACE(b'hello world', b'world', b'there')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello there");
	}

	// ════════════════════════════════════════════════════════════════
	// ExecuteBatchDml error code mapping
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#executebatchdmlresponse
	//   "If a statement fails, the status in the response body identifies the cause."
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#google.rpc.Code
	//   Foreign key violation → FAILED_PRECONDITION (9)
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task BatchDml_ForeignKeyViolation_ReturnsFailedPrecondition()
	{
		// Arrange: parent + child tables with FK constraint
		await ExecuteDdlAsync(
			"CREATE TABLE ECB_FKParent (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync(
			"CREATE TABLE ECB_FKChild (ChildId INT64 NOT NULL, ParentId INT64, CONSTRAINT FK_ECB_Parent FOREIGN KEY (ParentId) REFERENCES ECB_FKParent (Id)) PRIMARY KEY (ChildId)");
		await InsertAsync("ECB_FKParent", new Dictionary<string, object?> { ["Id"] = 1L });

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		// Act: batch DML inserts a child row with non-existent parent → FK violation
		var act = async () =>
		{
			await connection.RunWithRetriableTransactionAsync(async transaction =>
			{
				var cmd = transaction.CreateBatchDmlCommand();
				cmd.Add("INSERT INTO ECB_FKChild (ChildId, ParentId) VALUES (100, 999)");
				await cmd.ExecuteNonQueryAsync();
			});
		};

		// Assert: should be FailedPrecondition, not InvalidArgument
		var ex = await act.Should().ThrowAsync<SpannerException>();
		ex.Which.ErrorCode.Should().Be(ErrorCode.FailedPrecondition);
	}

	// ════════════════════════════════════════════════════════════════
	// STRPOS for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
	//   "Returns the 1-based position of the first occurrence of search_value
	//    in value, or 0 if not found. Works on both STRING and BYTES."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task StrPos_Bytes_FindsSubsequence()
	{
		var result = await Eval("STRPOS(b'hello world', b'world')");
		result.Should().Be(7L); // 1-based position
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task StrPos_Bytes_NotFound_ReturnsZero()
	{
		var result = await Eval("STRPOS(b'hello', b'xyz')");
		result.Should().Be(0L);
	}

	// ════════════════════════════════════════════════════════════════
	// TRIM/LTRIM/RTRIM for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
	//   "Removes bytes from BYTES value that appear in bytes_to_trim."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Trim_Bytes_RemovesBytesFromBothEnds()
	{
		// TRIM(b'\x00\x01hello\x01\x00', b'\x00\x01') should remove \x00 and \x01 from both ends
		var result = await Eval(@"TRIM(b'\x00\x01hello\x01\x00', b'\x00\x01')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Ltrim_Bytes_RemovesBytesFromStart()
	{
		var result = await Eval(@"LTRIM(b'\x20\x20hello', b'\x20')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Rtrim_Bytes_RemovesBytesFromEnd()
	{
		var result = await Eval(@"RTRIM(b'hello\x20\x20', b'\x20')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello");
	}

	// ════════════════════════════════════════════════════════════════
	// LPAD/RPAD for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	//   "Pads a STRING or BYTES value."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Lpad_Bytes_PadsWithPattern()
	{
		// LPAD(b'hi', 5, b'\x00') should produce b'\x00\x00\x00hi' (3 null bytes + hi)
		var result = await Eval(@"LPAD(b'hi', 5, b'\x00')");
		result.Should().BeOfType<byte[]>();
		var bytes = (byte[])result!;
		bytes.Length.Should().Be(5);
		bytes[0].Should().Be(0x00);
		bytes[1].Should().Be(0x00);
		bytes[2].Should().Be(0x00);
		bytes[3].Should().Be((byte)'h');
		bytes[4].Should().Be((byte)'i');
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Rpad_Bytes_PadsWithPattern()
	{
		var result = await Eval(@"RPAD(b'hi', 5, b'\x00')");
		result.Should().BeOfType<byte[]>();
		var bytes = (byte[])result!;
		bytes.Length.Should().Be(5);
		bytes[0].Should().Be((byte)'h');
		bytes[1].Should().Be((byte)'i');
		bytes[2].Should().Be(0x00);
		bytes[3].Should().Be(0x00);
		bytes[4].Should().Be(0x00);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Lpad_Bytes_Truncates()
	{
		// LPAD(b'hello', 3, b'x') should truncate to first 3 bytes
		var result = await Eval("LPAD(b'hello', 3, b'x')");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hel");
	}

	// ════════════════════════════════════════════════════════════════
	// SPLIT for BYTES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
	//   "Splits a STRING or BYTES value, using the argument as the delimiter."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Split_Bytes_SplitsCorrectly()
	{
		// SPLIT(b'a,b,c', b',') should return array of 3 BYTES elements
		var rows = await QueryAsync("SELECT ARRAY_LENGTH(SPLIT(b'a,b,c', b',')) AS R");
		rows[0]["R"].Should().Be(3L);
	}

	// ════════════════════════════════════════════════════════════════
	// ARRAY_IS_DISTINCT with NULL handling
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_is_distinct
	//   "Returns TRUE if the array has no repeated elements, using the same
	//    equality comparison as DISTINCT."
	//   Two NULLs are NOT distinct (i.e., they're considered equal).
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayIsDistinct_WithDuplicateNulls_ReturnsFalse()
	{
		// Two NULL values should be considered duplicates
		var result = await Eval("ARRAY_IS_DISTINCT([1, NULL, 2, NULL])");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayIsDistinct_WithSingleNull_ReturnsTrue()
	{
		var result = await Eval("ARRAY_IS_DISTINCT([1, NULL, 2, 3])");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayIsDistinct_AllDistinct_ReturnsTrue()
	{
		var result = await Eval("ARRAY_IS_DISTINCT([1, 2, 3, 4])");
		result.Should().Be(true);
	}

	// ════════════════════════════════════════════════════════════════
	// MOD with NUMERIC type
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod
	//   "MOD(X, Y) returns the remainder when dividing X by Y."
	//   NUMERIC precision should be preserved.
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Mod_Numeric_PreservesPrecision()
	{
		// MOD(NUMERIC '10.5', NUMERIC '3') = 1.5
		var result = await Eval("MOD(NUMERIC '10.5', NUMERIC '3')");
		result.Should().BeOfType<SpannerNumeric>();
		((SpannerNumeric)result!).ToDecimal(LossOfPrecisionHandling.Truncate).Should().Be(1.5m);
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Mod_Numeric_DivideByZero_ThrowsError()
	{
		var act = async () => await QueryAsync("SELECT MOD(NUMERIC '10', NUMERIC '0') AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// TIMESTAMP_TRUNC NANOSECOND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	//   "NANOSECOND: Truncates to nanosecond precision (identity)."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampTrunc_Nanosecond_IsIdentity()
	{
		var result = await Eval("TIMESTAMP_TRUNC(TIMESTAMP '2025-03-15T10:30:45.123456789Z', NANOSECOND)");
		result.Should().BeOfType<DateTime>();
		// NANOSECOND truncation is an identity operation
		var ts = (DateTime)result!;
		ts.Year.Should().Be(2025);
		ts.Month.Should().Be(3);
		ts.Day.Should().Be(15);
		ts.Hour.Should().Be(10);
		ts.Minute.Should().Be(30);
		ts.Second.Should().Be(45);
	}

	// ════════════════════════════════════════════════════════════════
	// UPDATE/DELETE without WHERE clause
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax
	//   "The WHERE clause is required."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Update_WithoutWhere_RejectsStatement()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_NoWhere (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync("ECB_NoWhere", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });

		var act = async () => await ExecuteDmlAsync("UPDATE ECB_NoWhere SET Val = 99");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Delete_WithoutWhere_RejectsStatement()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_NoWhere2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync("ECB_NoWhere2", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });

		var act = async () => await ExecuteDmlAsync("DELETE FROM ECB_NoWhere2");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ════════════════════════════════════════════════════════════════
	// ARRAY_AGG / STRING_AGG with LIMIT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
	//   "LIMIT count: Specifies the maximum number of expression inputs in the result."
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayAgg_WithOrderByAndLimit_LimitsResults()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_AggLim (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_AggLim",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "c" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "b" },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = "d" });

		var rows = await QueryAsync("SELECT ARRAY_AGG(Val ORDER BY Val LIMIT 2) AS R FROM ECB_AggLim");
		rows.Should().HaveCount(1);
		var arr = (List<string>)rows[0]["R"]!;
		arr.Should().HaveCount(2);
		arr[0].Should().Be("a");
		arr[1].Should().Be("b");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task StringAgg_WithOrderByAndLimit_LimitsResults()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_StrAggLim (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_StrAggLim",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "c" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "b" });

		var rows = await QueryAsync("SELECT STRING_AGG(Val, ',' ORDER BY Val LIMIT 2) AS R FROM ECB_StrAggLim");
		rows.Should().HaveCount(1);
		rows[0]["R"].Should().Be("a,b");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task ArrayAgg_WithOrderByNullsFirst_NullsComesFirst()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_AggNulls (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_AggNulls",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "b" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "a" });

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
		//   "NULLS FIRST | NULLS LAST control sorting of NULL values within the ORDER BY clause."
		var rows = await QueryAsync("SELECT ARRAY_AGG(Val ORDER BY Val ASC NULLS FIRST) AS R FROM ECB_AggNulls");
		rows.Should().HaveCount(1);
		var arr = rows[0]["R"] as System.Collections.IList;
		arr.Should().NotBeNull();
		arr![0].Should().BeNull();
		arr[1].Should().Be("a");
		arr[2].Should().Be("b");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task StringAgg_WithOrderByNullsLast_NullsExcluded()
	{
		// STRING_AGG ignores NULLs by default, but NULLS LAST should still parse
		await ExecuteDdlAsync("CREATE TABLE ECB_StrNulls (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_StrNulls",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "b" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "a" });

		var rows = await QueryAsync("SELECT STRING_AGG(Val, ',' ORDER BY Val ASC NULLS LAST) AS R FROM ECB_StrNulls");
		rows.Should().HaveCount(1);
		// STRING_AGG ignores NULL values, so result is just "a,b"
		rows[0]["R"].Should().Be("a,b");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampDiff_WithMonth_ReturnsError()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
		//   Only NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY are valid parts.
		await ExecuteDdlAsync("CREATE TABLE ECB_TsDiffM (Id INT64 NOT NULL, Ts TIMESTAMP) PRIMARY KEY (Id)");
		await InsertAsync("ECB_TsDiffM",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Ts"] = DateTime.UtcNow });

		var act = () => QueryAsync("SELECT TIMESTAMP_DIFF(Ts, TIMESTAMP '2020-01-01T00:00:00Z', MONTH) FROM ECB_TsDiffM");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task TimestampDiff_WithYear_ReturnsError()
	{
		await ExecuteDdlAsync("CREATE TABLE ECB_TsDiffY (Id INT64 NOT NULL, Ts TIMESTAMP) PRIMARY KEY (Id)");
		await InsertAsync("ECB_TsDiffY",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Ts"] = DateTime.UtcNow });

		var act = () => QueryAsync("SELECT TIMESTAMP_DIFF(Ts, TIMESTAMP '2020-01-01T00:00:00Z', YEAR) FROM ECB_TsDiffY");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task ArrayAgg_IgnoreNulls_AllNullValues_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
		//   "If there are zero input rows, this function returns NULL."
		//   When IGNORE NULLS filters all rows, result should be NULL.
		await ExecuteDdlAsync("CREATE TABLE ECB_AggAllNull (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_AggAllNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = null });

		var rows = await QueryAsync("SELECT ARRAY_AGG(Val IGNORE NULLS) AS R FROM ECB_AggAllNull");
		rows.Should().HaveCount(1);
		rows[0]["R"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayAgg_RespectNulls_IncludesNullsInResult()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg
		//   "RESPECT NULLS ... include NULLs in the output array."
		await ExecuteDdlAsync("CREATE TABLE ECB_AggRespNull (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		await InsertAsync("ECB_AggRespNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "b" });

		var rows = await QueryAsync("SELECT ARRAY_AGG(Val RESPECT NULLS ORDER BY Val ASC NULLS LAST) AS R FROM ECB_AggRespNull");
		rows.Should().HaveCount(1);
		var arr = rows[0]["R"] as System.Collections.IList;
		arr.Should().NotBeNull();
		arr!.Count.Should().Be(3);
		arr[0].Should().Be("a");
		arr[1].Should().Be("b");
		arr[2].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayConcatAgg_WithOrderBy_SortsBeforeConcatenation()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_concat_agg
		//   "ORDER BY key [...] - Sorts input arrays before concatenation."
		await ExecuteDdlAsync("CREATE TABLE ECB_ArrConcOrd (Id INT64 NOT NULL, Arr ARRAY<STRING(10)>, Ord INT64) PRIMARY KEY (Id)");
		await InsertAsync("ECB_ArrConcOrd",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Arr"] = new[] { "c", "d" }, ["Ord"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Arr"] = new[] { "a", "b" }, ["Ord"] = 1L });

		var rows = await QueryAsync("SELECT ARRAY_CONCAT_AGG(Arr ORDER BY Ord) AS R FROM ECB_ArrConcOrd");
		rows.Should().HaveCount(1);
		var result = rows[0]["R"] as System.Collections.IList;
		result.Should().NotBeNull();
		result!.Count.Should().Be(4);
		result[0].Should().Be("a");
		result[1].Should().Be("b");
		result[2].Should().Be("c");
		result[3].Should().Be("d");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayConcatAgg_WithLimit_LimitsArraysBeforeConcatenation()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_concat_agg
		//   "LIMIT n - Limits the number of input arrays used."
		await ExecuteDdlAsync("CREATE TABLE ECB_ArrConcLim (Id INT64 NOT NULL, Arr ARRAY<STRING(10)>, Ord INT64) PRIMARY KEY (Id)");
		await InsertAsync("ECB_ArrConcLim",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Arr"] = new[] { "a", "b" }, ["Ord"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Arr"] = new[] { "c", "d" }, ["Ord"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Arr"] = new[] { "e", "f" }, ["Ord"] = 3L });

		var rows = await QueryAsync("SELECT ARRAY_CONCAT_AGG(Arr ORDER BY Ord LIMIT 2) AS R FROM ECB_ArrConcLim");
		rows.Should().HaveCount(1);
		var result = rows[0]["R"] as System.Collections.IList;
		result.Should().NotBeNull();
		result!.Count.Should().Be(4);
		result[0].Should().Be("a");
		result[1].Should().Be("b");
		result[2].Should().Be("c");
		result[3].Should().Be("d");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task InformationSchema_SpannerStatistics_ReturnsEmptyResult()
	{
		// Ref: https://cloud.google.com/spanner/docs/information-schema#spanner_statistics
		//   SPANNER_STATISTICS lists available query optimizer statistics packages.
		var rows = await QueryAsync("SELECT * FROM INFORMATION_SCHEMA.SPANNER_STATISTICS");
		rows.Should().NotBeNull();
		// The emulator has no statistics packages, so the result should be empty
		rows.Should().BeEmpty();
	}
}
