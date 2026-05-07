using FluentAssertions;
using Google.Cloud.Spanner.Data;
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
	public async Task CastTimestamp_ToString_WholeSecondsOmitsFraction()
	{
		var rows = await QueryAsync("SELECT CAST(TIMESTAMP '2024-01-15T12:34:56Z' AS STRING) AS R");
		var r = (string)rows[0]["R"]!;
		r.Should().Contain("12:34:56");
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
	//   "NULL values are formatted as the string 'NULL'"
	// ════════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Format_NullIntArg_ProducesNullString()
	{
		var rows = await QueryAsync("SELECT FORMAT('%d', CAST(NULL AS INT64)) AS R");
		rows[0]["R"].Should().Be("NULL");
	}

	[Fact]
	[Trait(TestTraits.Category, "EdgeCaseBugs")]
	public async Task Format_NullFloatArg_ProducesNullString()
	{
		var rows = await QueryAsync("SELECT FORMAT('%f', CAST(NULL AS FLOAT64)) AS R");
		rows[0]["R"].Should().Be("NULL");
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
	public async Task SafeDivide_InfinityDividedByInfinity_ReturnsNull()
	{
		// Inf/Inf is indeterminate (would produce NaN) — that's an error
		var result = await Eval("SAFE_DIVIDE(CAST('+inf' AS FLOAT64), CAST('+inf' AS FLOAT64))");
		result.Should().BeNull();
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
}
