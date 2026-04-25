using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for error conditions: invalid SQL, type mismatches, missing tables/columns,
/// division by zero, out-of-range values, and other expected error scenarios.
/// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#code
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ErrorConditionIntegrationTests : IntegrationTestBase
{
	public ErrorConditionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureErrorTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE ErrTest (Id INT64 NOT NULL, Name STRING(10) NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// Missing table / column references
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.rpc#code
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Query_NonExistentTable_Throws()
	{
		var act = () => QueryAsync("SELECT * FROM NonExistentTable_12345");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Query_NonExistentColumn_Throws()
	{
		await EnsureErrorTableAsync();
		var act = () => QueryAsync("SELECT NonExistentColumn FROM ErrTest");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Division by zero
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DivisionByZero_IntegerDivide_Throws()
	{
		var act = () => QueryAsync("SELECT DIV(1, 0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task DivisionByZero_Mod_Throws()
	{
		var act = () => QueryAsync("SELECT MOD(1, 0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// IEEE_DIVIDE by zero doesn't throw - returns Inf/NaN (tested in OperatorIntegrationTests)
	// SAFE_DIVIDE by zero returns NULL (tested in OperatorIntegrationTests)

	// ═══════════════════════════════════════════════════════════════
	// CAST errors
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST('abc' AS INT64)")]
	[InlineData("CAST('abc' AS FLOAT64)")]
	[InlineData("CAST('abc' AS BOOL)")]
	[InlineData("CAST('abc' AS DATE)")]
	[InlineData("CAST('abc' AS TIMESTAMP)")]
	[InlineData("CAST('999999999999999999999' AS INT64)")]
	public async Task Cast_InvalidValue_Throws(string expr)
	{
		var act = () => QueryAsync($"SELECT {expr}");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// SAFE_CAST should return NULL instead of throwing
	[Theory]
	[InlineData("SAFE_CAST('abc' AS INT64)")]
	[InlineData("SAFE_CAST('abc' AS FLOAT64)")]
	[InlineData("SAFE_CAST('abc' AS BOOL)")]
	public async Task SafeCast_InvalidValue_ReturnsNull(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// Math domain errors
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Sqrt_Negative_Throws()
	{
		var act = () => QueryAsync("SELECT SQRT(-1.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Ln_Zero_Throws()
	{
		var act = () => QueryAsync("SELECT LN(0.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Ln_Negative_Throws()
	{
		var act = () => QueryAsync("SELECT LN(-1.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Log_Zero_Throws()
	{
		var act = () => QueryAsync("SELECT LOG(0.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Log10_Zero_Throws()
	{
		var act = () => QueryAsync("SELECT LOG10(0.0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// NOT NULL constraint violations
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NotNull_OmitRequiredColumn_Throws()
	{
		await EnsureErrorTableAsync();
		var act = () => ExecuteDmlAsync("INSERT INTO ErrTest (Id) VALUES (1)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task NotNull_ExplicitNull_Throws()
	{
		await EnsureErrorTableAsync();
		var act = () => ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (2, NULL)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task NotNull_UpdateToNull_Throws()
	{
		await EnsureErrorTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (3, 'ok')"); } catch { }
		var act = () => ExecuteDmlAsync("UPDATE ErrTest SET Name = NULL WHERE Id = 3");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING(N) length overflow
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#string
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task StringLengthOverflow_Insert_Throws()
	{
		await EnsureErrorTableAsync();
		// Name is STRING(10), inserting 11 chars
		var act = () => ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (10, '12345678901')");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task StringLengthOverflow_Update_Throws()
	{
		await EnsureErrorTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (11, 'ok')"); } catch { }
		var act = () => ExecuteDmlAsync("UPDATE ErrTest SET Name = '12345678901' WHERE Id = 11");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Duplicate primary key
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#mutation
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DuplicateKey_Insert_Throws()
	{
		await EnsureErrorTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (20, 'first')"); } catch { }
		var act = () => ExecuteDmlAsync("INSERT INTO ErrTest (Id, Name) VALUES (20, 'second')");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task DuplicateKey_SdkInsert_Throws()
	{
		await EnsureErrorTableAsync();
		try { await InsertAsync("ErrTest", new Dictionary<string, object?> { ["Id"] = 30L, ["Name"] = "first" }); } catch { }
		var act = () => InsertAsync("ErrTest", new Dictionary<string, object?> { ["Id"] = 30L, ["Name"] = "second" });
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Array out of bounds
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#accessing_array_elements
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayOffset_OutOfBounds_Throws()
	{
		var act = () => QueryAsync("SELECT [1, 2, 3][OFFSET(5)]");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task ArrayOrdinal_OutOfBounds_Throws()
	{
		var act = () => QueryAsync("SELECT [1, 2, 3][ORDINAL(5)]");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task ArrayOffset_Negative_Throws()
	{
		var act = () => QueryAsync("SELECT [1, 2, 3][OFFSET(-1)]");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task ArrayOrdinal_Zero_Throws()
	{
		// ORDINAL is 1-based, 0 is invalid
		var act = () => QueryAsync("SELECT [1, 2, 3][ORDINAL(0)]");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// SAFE_OFFSET / SAFE_ORDINAL should return NULL instead (tested in ArrayFunctionIntegrationTests)

	// ═══════════════════════════════════════════════════════════════
	// DDL errors
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CreateTable_DuplicateName_Throws()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE ErrDupTable (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		var act = () => ExecuteDdlAsync("CREATE TABLE ErrDupTable (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task DropTable_NonExistent_Throws()
	{
		var act = () => ExecuteDdlAsync("DROP TABLE NonExistentErrTable_99999");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Empty query results (not errors, but boundary conditions)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Query_NoRows_ReturnsEmpty()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT * FROM ErrTest WHERE Id = -999999");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task Aggregate_NoRows_CountReturnsZero()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM ErrTest WHERE Id = -999999");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	public async Task Aggregate_NoRows_SumReturnsNull()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT SUM(Val) AS S FROM ErrTest WHERE Id = -999999");
		rows[0]["S"].Should().BeNull();
	}

	[Fact]
	public async Task Aggregate_NoRows_MinReturnsNull()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT MIN(Val) AS M FROM ErrTest WHERE Id = -999999");
		rows[0]["M"].Should().BeNull();
	}

	[Fact]
	public async Task Aggregate_NoRows_MaxReturnsNull()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT MAX(Val) AS M FROM ErrTest WHERE Id = -999999");
		rows[0]["M"].Should().BeNull();
	}

	[Fact]
	public async Task Aggregate_NoRows_AvgReturnsNull()
	{
		await EnsureErrorTableAsync();
		var rows = await QueryAsync("SELECT AVG(Val) AS A FROM ErrTest WHERE Id = -999999");
		rows[0]["A"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// UPDATE/DELETE with no matching rows
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Update_NoMatch_ReturnsZero()
	{
		await EnsureErrorTableAsync();
		var count = await ExecuteDmlAsync("UPDATE ErrTest SET Val = 0 WHERE Id = -999999");
		count.Should().Be(0);
	}

	[Fact]
	public async Task Delete_NoMatch_ReturnsZero()
	{
		await EnsureErrorTableAsync();
		var count = await ExecuteDmlAsync("DELETE FROM ErrTest WHERE Id = -999999");
		count.Should().Be(0);
	}
}
