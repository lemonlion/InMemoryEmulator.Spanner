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
}
