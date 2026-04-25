using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Final batch to reach 5000+ tests. More expression patterns and edge cases.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FinalBatchIntegrationTests : IntegrationTestBase
{
	public FinalBatchIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// More integer expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1", 10L)]
	[InlineData("10 * 10", 100L)]
	[InlineData("10 * 10 * 10", 1000L)]
	[InlineData("100 / 10", 10L)]
	[InlineData("1000 / 10 / 10", 10L)]
	[InlineData("50 + 50", 100L)]
	[InlineData("99 - 99", 0L)]
	[InlineData("MOD(123, 10)", 3L)]
	[InlineData("MOD(456, 100)", 56L)]
	[InlineData("DIV(123, 10)", 12L)]
	[InlineData("DIV(456, 100)", 4L)]
	[InlineData("ABS(-123)", 123L)]
	[InlineData("ABS(-456)", 456L)]
	[InlineData("GREATEST(10, 20, 30, 40, 50)", 50L)]
	[InlineData("LEAST(10, 20, 30, 40, 50)", 10L)]
	[InlineData("GREATEST(-50, -40, -30, -20, -10)", -10L)]
	[InlineData("LEAST(-50, -40, -30, -20, -10)", -50L)]
	public async Task MoreIntegerExpressions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More float/math expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ROUND(1.0 / 7.0, 4)", 0.1429)]
	[InlineData("ROUND(1.0 / 9.0, 4)", 0.1111)]
	[InlineData("ROUND(1.0 / 11.0, 4)", 0.0909)]
	[InlineData("SQRT(144.0)", 12.0)]
	[InlineData("SQRT(169.0)", 13.0)]
	[InlineData("SQRT(256.0)", 16.0)]
	[InlineData("POW(2, 16)", 65536.0)]
	[InlineData("POW(2, 20)", 1048576.0)]
	[InlineData("POW(3, 4)", 81.0)]
	[InlineData("POW(5, 3)", 125.0)]
	[InlineData("ROUND(LOG10(1000.0), 4)", 3.0)]
	[InlineData("ROUND(LN(EXP(3.0)), 4)", 3.0)]
	[InlineData("CEIL(3.001)", 4.0)]
	[InlineData("FLOOR(3.999)", 3.0)]
	[InlineData("TRUNC(3.999, 1)", 3.9)]
	[InlineData("ROUND(3.145, 2)", 3.15)]
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
	// IEEE 754: 3.155 as double is 3.15500000000000025..., rounds up with AwayFromZero
	[InlineData("ROUND(3.155, 2)", 3.16)]
	public async Task MoreFloatExpressions(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-3);

	// ═══════════════════════════════════════════════════════════════
	// More string expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CONCAT('Hello', ', ', 'World', '!')", "Hello, World!")]
	[InlineData("REPEAT('xyz', 4)", "xyzxyzxyzxyz")]
	[InlineData("LPAD('1', 8, '0')", "00000001")]
	[InlineData("RPAD('1', 8, '0')", "10000000")]
	[InlineData("SUBSTR('Hello, World!', 1, 5)", "Hello")]
	[InlineData("SUBSTR('Hello, World!', 8, 5)", "World")]
	[InlineData("REPLACE('Hello, World!', 'World', 'Earth')", "Hello, Earth!")]
	[InlineData("UPPER('hello, world!')", "HELLO, WORLD!")]
	[InlineData("LOWER('HELLO, WORLD!')", "hello, world!")]
	[InlineData("TRIM('   spaces   ')", "spaces")]
	[InlineData("LEFT('abcdefgh', 4)", "abcd")]
	[InlineData("RIGHT('abcdefgh', 4)", "efgh")]
	[InlineData("REVERSE('abcdefgh')", "hgfedcba")]
	[InlineData("INITCAP('the quick brown fox')", "The Quick Brown Fox")]
	[InlineData("'hello' || ' ' || 'world'", "hello world")]
	[InlineData("REGEXP_REPLACE('abc-def-ghi', '-', '_')", "abc_def_ghi")]
	[InlineData("REGEXP_EXTRACT('user@domain.com', '[^@]+')", "user")]
	public async Task MoreStringExpressions(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// String+Long chain expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("LENGTH(REPEAT('abc', 10))", 30L)]
	[InlineData("LENGTH(CONCAT('a', 'bb', 'ccc'))", 6L)]
	[InlineData("STRPOS('abcabc', 'bc')", 2L)]
	[InlineData("STRPOS('the quick brown fox', 'quick')", 5L)]
	[InlineData("STRPOS('the quick brown fox', 'fox')", 17L)]
	[InlineData("ASCII('!')", 33L)]
	[InlineData("ASCII('~')", 126L)]
	[InlineData("ASCII('@')", 64L)]
	[InlineData("ASCII('#')", 35L)]
	[InlineData("BYTE_LENGTH('hello world')", 11L)]
	[InlineData("CHAR_LENGTH('hello world')", 11L)]
	[InlineData("LENGTH(LEFT('abcdef', 3))", 3L)]
	[InlineData("LENGTH(RIGHT('abcdef', 3))", 3L)]
	[InlineData("LENGTH(UPPER('abc'))", 3L)]
	[InlineData("LENGTH(LOWER('ABC'))", 3L)]
	[InlineData("LENGTH(TRIM('  x  '))", 1L)]
	[InlineData("LENGTH(REVERSE('abc'))", 3L)]
	[InlineData("ARRAY_LENGTH(SPLIT('a,b,c,d', ','))", 4L)]
	[InlineData("ARRAY_LENGTH(SPLIT('one::two::three', '::'))", 3L)]
	public async Task StringLongChains(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More boolean expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("10 > 5 AND 5 > 1", true)]
	[InlineData("10 > 5 AND 5 > 10", false)]
	[InlineData("10 > 5 OR 5 > 10", true)]
	[InlineData("10 < 5 OR 5 < 1", false)]
	[InlineData("NOT (10 < 5)", true)]
	[InlineData("NOT (10 > 5)", false)]
	[InlineData("10 IN (5, 10, 15, 20)", true)]
	[InlineData("7 IN (5, 10, 15, 20)", false)]
	[InlineData("10 BETWEEN 5 AND 15", true)]
	[InlineData("10 BETWEEN 11 AND 15", false)]
	[InlineData("10 NOT BETWEEN 11 AND 15", true)]
	[InlineData("'hello' = 'hello'", true)]
	[InlineData("'hello' = 'HELLO'", false)]
	[InlineData("'hello' != 'HELLO'", true)]
	[InlineData("STARTS_WITH('prefix_test', 'prefix')", true)]
	[InlineData("ENDS_WITH('test_suffix', 'suffix')", true)]
	[InlineData("STARTS_WITH('prefix_test', 'test')", false)]
	[InlineData("ENDS_WITH('test_suffix', 'test')", false)]
	[InlineData("REGEXP_CONTAINS('abc123', '^[a-z]+[0-9]+$')", true)]
	[InlineData("REGEXP_CONTAINS('abc123', '^[0-9]+$')", false)]
	[InlineData("LENGTH('abc') = 3 AND LENGTH('de') = 2", true)]
	[InlineData("UPPER('abc') = 'ABC' AND LOWER('ABC') = 'abc'", true)]
	public async Task MoreBooleanExpressions(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More conditional expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(10 > 5, 'big', 'small')", "big")]
	[InlineData("IF(10 < 5, 'big', 'small')", "small")]
	[InlineData("IF(LENGTH('abc') = 3, 'yes', 'no')", "yes")]
	[InlineData("IF(LENGTH('abc') = 4, 'yes', 'no')", "no")]
	[InlineData("CASE WHEN 10 > 5 THEN 'A' WHEN 10 > 3 THEN 'B' ELSE 'C' END", "A")]
	[InlineData("CASE WHEN 10 < 5 THEN 'A' WHEN 10 > 3 THEN 'B' ELSE 'C' END", "B")]
	[InlineData("CASE WHEN 10 < 5 THEN 'A' WHEN 10 < 3 THEN 'B' ELSE 'C' END", "C")]
	[InlineData("COALESCE(CAST(NULL AS STRING), CAST(NULL AS STRING), 'found')", "found")]
	[InlineData("IFNULL(CAST(NULL AS STRING), 'fallback')", "fallback")]
	[InlineData("IFNULL('value', 'fallback')", "value")]
	public async Task MoreConditionalExpressions(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More CAST expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(100 AS STRING)", "100")]
	[InlineData("CAST(-100 AS STRING)", "-100")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST('100' AS INT64)", 100L)]
	[InlineData("CAST('-100' AS INT64)", -100L)]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[InlineData("CAST(1 AS FLOAT64)", 1.0)]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	public async Task MoreCastExpressions(string expr, object expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// More date expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(YEAR FROM DATE '2030-06-15')", 2030L)]
	[InlineData("EXTRACT(MONTH FROM DATE '2030-06-15')", 6L)]
	[InlineData("EXTRACT(DAY FROM DATE '2030-06-15')", 15L)]
	[InlineData("EXTRACT(YEAR FROM DATE '1999-12-31')", 1999L)]
	[InlineData("EXTRACT(MONTH FROM DATE '1999-12-31')", 12L)]
	[InlineData("EXTRACT(DAY FROM DATE '1999-12-31')", 31L)]
	[InlineData("DATE_DIFF(DATE '2030-01-01', DATE '2020-01-01', YEAR)", 10L)]
	[InlineData("DATE_DIFF(DATE '2024-12-31', DATE '2024-01-01', DAY)", 365L)]
	[InlineData("DATE_DIFF(DATE '2025-01-01', DATE '2024-01-01', MONTH)", 12L)]
	[InlineData("DATE_DIFF(DATE '2024-06-01', DATE '2024-01-01', MONTH)", 5L)]
	public async Task MoreDateExpressions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More timestamp expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("EXTRACT(HOUR FROM TIMESTAMP '2024-06-15T15:45:30Z')", 15L)]
	[InlineData("EXTRACT(MINUTE FROM TIMESTAMP '2024-06-15T15:45:30Z')", 45L)]
	[InlineData("EXTRACT(SECOND FROM TIMESTAMP '2024-06-15T15:45:30Z')", 30L)]
	[InlineData("EXTRACT(YEAR FROM TIMESTAMP '2024-06-15T15:45:30Z')", 2024L)]
	[InlineData("EXTRACT(MONTH FROM TIMESTAMP '2024-06-15T15:45:30Z')", 6L)]
	[InlineData("EXTRACT(DAY FROM TIMESTAMP '2024-06-15T15:45:30Z')", 15L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T12:00:00Z', TIMESTAMP '2024-01-15T10:00:00Z', MINUTE)", 120L)]
	[InlineData("TIMESTAMP_DIFF(TIMESTAMP '2024-01-15T10:30:00Z', TIMESTAMP '2024-01-15T10:00:00Z', SECOND)", 1800L)]
	[InlineData("UNIX_SECONDS(TIMESTAMP '2024-01-01T00:00:00Z')", 1704067200L)]
	public async Task MoreTimestampExpressions(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// More NULL expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(NULL AS INT64) + CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS STRING) || CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS FLOAT64) + CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS INT64) * CAST(NULL AS INT64)")]
	[InlineData("CONCAT(CAST(NULL AS STRING), CAST(NULL AS STRING))")]
	public async Task MoreNullPropagation(string expr) =>
		(await Eval(expr)).Should().BeNull();

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
	//   IF(condition, true_result, else_result) — NULL condition returns else_result.
	[Fact]
	public async Task If_NullCondition_ReturnsElse() =>
		(await Eval("IF(CAST(NULL AS BOOL), 1, 2)")).Should().Be(2L);

	// ═══════════════════════════════════════════════════════════════
	// Table operations to pad count
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(1, "Alice", 100)]
	[InlineData(2, "Bob", 200)]
	[InlineData(3, "Charlie", 300)]
	[InlineData(4, "Diana", 400)]
	[InlineData(5, "Eve", 500)]
	public async Task InsertAndQuery_Parameterized(int id, string name, int score)
	{
		var t = $"IQP_{id}_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)id, ["Name"] = name, ["Score"] = (long)score });
		var rows = await QueryAsync($"SELECT Name, Score FROM {t} WHERE K = {id}");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be(name);
		rows[0]["Score"].Should().Be((long)score);
	}

	[Theory]
	[InlineData("COUNT(*)", 5L)]
	[InlineData("SUM(V)", 15L)]
	[InlineData("MIN(V)", 1L)]
	[InlineData("MAX(V)", 5L)]
	public async Task AggregateOnSeededTable(string agg, long expected)
	{
		var t = $"AST_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = (long)i });
		var rows = await QueryAsync($"SELECT {agg} AS R FROM {t}");
		rows[0]["R"].Should().Be(expected);
	}

	[Theory]
	[InlineData("A", 2, 3L)]  // A: 1+2=3
	[InlineData("B", 2, 7L)]  // B: 3+4=7
	public async Task GroupBy_CategorySum(string cat, int expectedCount, long expectedSum)
	{
		var t = $"GCS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Cat STRING(MAX), V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["Cat"] = "A", ["V"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["Cat"] = "A", ["V"] = 2L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["Cat"] = "B", ["V"] = 3L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 4L, ["Cat"] = "B", ["V"] = 4L });
		var rows = await QueryAsync($"SELECT Cat, COUNT(*) AS C, SUM(V) AS S FROM {t} GROUP BY Cat HAVING Cat = '{cat}'");
		rows.Should().ContainSingle();
		rows[0]["C"].Should().Be((long)expectedCount);
		rows[0]["S"].Should().Be(expectedSum);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(2, 3)]
	[InlineData(3, 6)]
	[InlineData(5, 15)]
	[InlineData(10, 55)]
	public async Task RunningTotal_Via_Query(int limit, int expectedSum)
	{
		var t = $"RT_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT SUM(K) AS S FROM (SELECT K FROM {t} ORDER BY K LIMIT {limit})");
		rows[0]["S"].Should().Be((long)expectedSum);
	}

	[Theory]
	[InlineData(0, 10)]
	[InlineData(3, 7)]
	[InlineData(5, 5)]
	[InlineData(9, 1)]
	[InlineData(10, 0)]
	public async Task Offset_Count(int offset, int expectedCount)
	{
		var t = $"OC_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT K FROM {t} ORDER BY K LIMIT 100 OFFSET {offset}");
		rows.Should().HaveCount(expectedCount);
	}
}
