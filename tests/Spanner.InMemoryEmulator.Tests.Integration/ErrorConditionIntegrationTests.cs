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

	// ═══════════════════════════════════════════════════════════════
	// Non-GCP functions — must reject functions not in Cloud Spanner
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   Functions like LEFT, RIGHT, ASCII, CHR, INITCAP, INSTR, TRANSLATE,
	//   CONTAINS_SUBSTR, RANGE_BUCKET, RAND do not exist in GCP Spanner.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SELECT LEFT('abc', 2) AS R")]
	[InlineData("SELECT RIGHT('abc', 2) AS R")]
	[InlineData("SELECT INITCAP('hello world') AS R")]
	[InlineData("SELECT INSTR('hello', 'l') AS R")]
	[InlineData("SELECT TRANSLATE('abc', 'a', 'x') AS R")]
	[InlineData("SELECT CONTAINS_SUBSTR('hello', 'ell') AS R")]
	[InlineData("SELECT RANGE_BUCKET(5, [1, 10, 100]) AS R")]
	[InlineData("SELECT RAND() AS R")]
	[InlineData("SELECT ASCII('A') AS R")]
	[InlineData("SELECT CHR(65) AS R")]
	public async Task NonGcpFunction_Throws(string sql)
	{
		var act = () => QueryAsync(sql);
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Window / Analytic functions — not supported in Cloud Spanner
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   Cloud Spanner does not support LAG, LEAD, FIRST_VALUE, LAST_VALUE.
	//   ROW_NUMBER, RANK, DENSE_RANK, and aggregate OVER() are now implemented.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SELECT LAG(Val) OVER (ORDER BY Id) AS L FROM ErrTest")]
	[InlineData("SELECT LEAD(Val) OVER (ORDER BY Id) AS L FROM ErrTest")]
	[InlineData("SELECT FIRST_VALUE(Val) OVER (ORDER BY Id) AS FV FROM ErrTest")]
	[InlineData("SELECT LAST_VALUE(Val) OVER (ORDER BY Id) AS LV FROM ErrTest")]
	public async Task WindowFunction_Throws(string sql)
	{
		await EnsureErrorTableAsync();
		var act = () => QueryAsync(sql);
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// Literal NULL in operators — GCP Spanner rejects at parse time
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
	//   "Operands of <op> cannot be literal NULL"
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SELECT NULL + 1 AS R")]
	[InlineData("SELECT 1 + NULL AS R")]
	[InlineData("SELECT NULL - 1 AS R")]
	[InlineData("SELECT NULL * 1 AS R")]
	[InlineData("SELECT NULL / 1 AS R")]
	[InlineData("SELECT NULL = 1 AS R")]
	[InlineData("SELECT 1 = NULL AS R")]
	[InlineData("SELECT NULL != 1 AS R")]
	[InlineData("SELECT NULL < 1 AS R")]
	[InlineData("SELECT NULL > 1 AS R")]
	[InlineData("SELECT NULL <= 1 AS R")]
	[InlineData("SELECT NULL >= 1 AS R")]
	[InlineData("SELECT NULL || 'a' AS R")]
	[InlineData("SELECT 'a' || NULL AS R")]
	[InlineData("SELECT NOT NULL AS R")]
	public async Task LiteralNull_InOperator_Throws(string sql)
	{
		var act = () => QueryAsync(sql);
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task LiteralNull_InAndOr_StillWorks()
	{
		// AND/OR with NULL use three-valued logic and are allowed
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
		var rows = await QueryAsync("SELECT NULL AND FALSE AS R");
		rows[0]["R"].Should().Be(false);
	}

	// ═══════════════════════════════════════════════════════════════
	// TO_JSON_STRING on non-JSON types — GCP Spanner rejects
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   TO_JSON_STRING only accepts JSON-typed values.
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SELECT TO_JSON_STRING(1) AS R")]
	[InlineData("SELECT TO_JSON_STRING('hello') AS R")]
	[InlineData("SELECT TO_JSON_STRING(TRUE) AS R")]
	public async Task ToJsonString_NonJsonType_Throws(string sql)
	{
		var act = () => QueryAsync(sql);
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_TO_STRING on non-STRING arrays — GCP rejects
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	//   Signature: ARRAY_TO_STRING(ARRAY<STRING>, STRING, [STRING])
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayToString_NonStringArray_Throws()
	{
		var act = () => QueryAsync("SELECT ARRAY_TO_STRING([1, 2, 3], ',') AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// CAST ARRAY type incompatibility — GCP rejects cross-type array casts
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CastArray_IncompatibleTypes_Throws()
	{
		var act = () => QueryAsync("SELECT CAST(GENERATE_ARRAY(1, 3) AS ARRAY<STRING>) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP_ADD with MONTH — not supported for TIMESTAMP
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
	//   "TIMESTAMP_ADD does not support the MONTH date part"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimestampAdd_Month_Throws()
	{
		var act = () => QueryAsync("SELECT TIMESTAMP_ADD(TIMESTAMP '2024-01-15T00:00:00Z', INTERVAL 1 MONTH) AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// REGEXP_EXTRACT with >1 capturing group — GCP rejects
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
	//   "at most one capturing group"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task RegexpExtract_MultipleCaptures_Throws()
	{
		var act = () => QueryAsync("SELECT REGEXP_EXTRACT('abc123', '([a-z]+)([0-9]+)') AS R");
		await act.Should().ThrowAsync<SpannerException>();
	}
}
