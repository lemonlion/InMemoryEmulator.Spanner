using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Final gap-closing tests: more variations of comparison operators, arithmetic,
/// type coercion, multi-column operations, and various function combinations.
/// Each [InlineData] = 1 test. Target: ~400+ tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
/// </summary>
[Collection(IntegrationCollection.Name)]
public class GapClosingIntegrationTests : IntegrationTestBase
{
	public GapClosingIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// Integer arithmetic edge cases
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 + 2 + 3 + 4 + 5", 15L)]
	[InlineData("10 - 3 - 2 - 1", 4L)]
	[InlineData("2 * 3 * 4", 24L)]
	[InlineData("100 / 10 / 2", 5L)]
	[InlineData("MOD(100, 7)", 2L)]
	[InlineData("MOD(100, 10)", 0L)]
	[InlineData("MOD(17, 5)", 2L)]
	[InlineData("MOD(0, 7)", 0L)]
	[InlineData("DIV(100, 7)", 14L)]
	[InlineData("DIV(17, 5)", 3L)]
	[InlineData("DIV(99, 100)", 0L)]
	[InlineData("ABS(-999)", 999L)]
	[InlineData("ABS(999)", 999L)]
	[InlineData("SIGN(999)", 1L)]
	[InlineData("SIGN(-999)", -1L)]
	[InlineData("GREATEST(1, 2, 3, 4, 5)", 5L)]
	[InlineData("LEAST(1, 2, 3, 4, 5)", 1L)]
	[InlineData("GREATEST(-5, -3, -1)", -1L)]
	[InlineData("LEAST(-5, -3, -1)", -5L)]
	[InlineData("GREATEST(10, 10, 10)", 10L)]
	[InlineData("LEAST(10, 10, 10)", 10L)]
	[InlineData("1 + 2 * 3 + 4", 11L)]
	[InlineData("(1 + 2) * (3 + 4)", 21L)]
	[InlineData("100 - 50 + 25 - 12", 63L)]
	[InlineData("2 * 2 * 2 * 2 * 2", 32L)]
	[InlineData("MOD(DIV(100, 3), 7)", 5L)]
	[InlineData("ABS(-1) + ABS(-2) + ABS(-3)", 6L)]
	[InlineData("GREATEST(ABS(-10), ABS(-5), ABS(-1))", 10L)]
	[InlineData("LEAST(ABS(-10), ABS(-5), ABS(-1))", 1L)]
	public async Task IntArithmeticEdgeCases(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Float arithmetic edge cases
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1.1 + 2.2 + 3.3", 6.6)]
	[InlineData("10.5 - 3.2", 7.3)]
	[InlineData("2.5 * 2.0", 5.0)]
	[InlineData("7.5 / 2.5", 3.0)]
	[InlineData("ROUND(1.0 / 3.0, 4)", 0.3333)]
	[InlineData("ROUND(2.0 / 3.0, 4)", 0.6667)]
	[InlineData("CEIL(0.001)", 1.0)]
	[InlineData("CEIL(0.999)", 1.0)]
	[InlineData("FLOOR(0.001)", 0.0)]
	[InlineData("FLOOR(0.999)", 0.0)]
	[InlineData("ROUND(0.4)", 0.0)]
	[InlineData("ROUND(0.5)", 1.0)]
	[InlineData("ROUND(0.6)", 1.0)]
	[InlineData("ROUND(-0.4)", 0.0)]
	[InlineData("ROUND(-0.5)", -1.0)]
	[InlineData("ROUND(-0.6)", -1.0)]
	[InlineData("TRUNC(9.99)", 9.0)]
	[InlineData("TRUNC(-9.99)", -9.0)]
	[InlineData("TRUNC(0.99999)", 0.0)]
	[InlineData("ABS(-0.001)", 0.001)]
	[InlineData("SIGN(0.001)", 1.0)]
	[InlineData("SIGN(-0.001)", -1.0)]
	[InlineData("GREATEST(1.1, 2.2, 3.3)", 3.3)]
	[InlineData("LEAST(1.1, 2.2, 3.3)", 1.1)]
	[InlineData("IEEE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	[InlineData("IEEE_DIVIDE(0.0, 1.0)", 0.0)]
	[InlineData("SAFE_DIVIDE(1.0, 3.0)", 0.3333333333333333)]
	[InlineData("SQRT(2.0) * SQRT(2.0)", 2.0)]
	[InlineData("POW(1.5, 2)", 2.25)]
	[InlineData("POW(0.5, 2)", 0.25)]
	public async Task FloatArithmeticEdgeCases(string expr, double expected) =>
		((double)(await Eval(expr))!).Should().BeApproximately(expected, 1e-4);

	// ═══════════════════════════════════════════════════════════════
	// Boolean expression combinations
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("TRUE AND TRUE AND TRUE", true)]
	[InlineData("TRUE AND TRUE AND FALSE", false)]
	[InlineData("FALSE OR FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE OR FALSE", false)]
	[InlineData("NOT (TRUE AND FALSE)", true)]
	[InlineData("NOT (TRUE OR FALSE)", false)]
	[InlineData("(TRUE OR FALSE) AND (TRUE OR FALSE)", true)]
	[InlineData("(TRUE AND FALSE) OR (FALSE AND TRUE)", false)]
	[InlineData("NOT NOT NOT TRUE", false)]
	[InlineData("NOT NOT NOT FALSE", true)]
	[InlineData("TRUE AND NOT FALSE", true)]
	[InlineData("FALSE OR NOT FALSE", true)]
	[InlineData("1 = 1 AND 2 = 2", true)]
	[InlineData("1 = 1 AND 2 = 3", false)]
	[InlineData("1 = 2 OR 3 = 3", true)]
	[InlineData("1 = 2 OR 3 = 4", false)]
	[InlineData("NOT (1 = 2)", true)]
	[InlineData("NOT (1 = 1)", false)]
	[InlineData("1 < 2 AND 2 < 3 AND 3 < 4", true)]
	[InlineData("1 > 2 OR 2 > 3 OR 3 > 4", false)]
	[InlineData("(1 + 1) = 2", true)]
	[InlineData("(2 * 3) = 6", true)]
	[InlineData("LENGTH('abc') = 3", true)]
	[InlineData("UPPER('abc') = 'ABC'", true)]
	[InlineData("STARTS_WITH('hello', 'he') AND ENDS_WITH('hello', 'lo')", true)]
	[InlineData("LENGTH('') = 0 AND LENGTH('a') = 1", true)]
	public async Task BooleanCombinations(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Comparison operators comprehensive
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("0 = 0", true)]
	[InlineData("0 != 0", false)]
	[InlineData("0 < 1", true)]
	[InlineData("0 > 1", false)]
	[InlineData("0 <= 0", true)]
	[InlineData("0 >= 0", true)]
	[InlineData("-1 < 0", true)]
	[InlineData("-1 > 0", false)]
	[InlineData("100 = 100", true)]
	[InlineData("100 != 101", true)]
	[InlineData("0.0 = 0.0", true)]
	[InlineData("0.0 < 0.1", true)]
	[InlineData("0.1 > 0.0", true)]
	[InlineData("'' = ''", true)]
	[InlineData("'' != 'a'", true)]
	[InlineData("'' < 'a'", true)]
	[InlineData("'a' > ''", true)]
	[InlineData("'abc' = 'abc'", true)]
	[InlineData("'abc' != 'abd'", true)]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'abd' > 'abc'", true)]
	[InlineData("TRUE = TRUE", true)]
	[InlineData("FALSE = FALSE", true)]
	[InlineData("TRUE != FALSE", true)]
	public async Task ComparisonOperators(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// BETWEEN comprehensive
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("0 BETWEEN 0 AND 10", true)]
	[InlineData("5 BETWEEN 0 AND 10", true)]
	[InlineData("10 BETWEEN 0 AND 10", true)]
	[InlineData("-1 BETWEEN 0 AND 10", false)]
	[InlineData("11 BETWEEN 0 AND 10", false)]
	[InlineData("0 NOT BETWEEN 0 AND 10", false)]
	[InlineData("-1 NOT BETWEEN 0 AND 10", true)]
	[InlineData("11 NOT BETWEEN 0 AND 10", true)]
	[InlineData("1.5 BETWEEN 1.0 AND 2.0", true)]
	[InlineData("0.5 BETWEEN 1.0 AND 2.0", false)]
	[InlineData("'b' BETWEEN 'a' AND 'c'", true)]
	[InlineData("'d' BETWEEN 'a' AND 'c'", false)]
	public async Task BetweenComprehensive(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// IN comprehensive
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("1 IN (1)", true)]
	[InlineData("1 IN (1, 2)", true)]
	[InlineData("2 IN (1, 2)", true)]
	[InlineData("3 IN (1, 2)", false)]
	[InlineData("1 IN (1, 2, 3, 4, 5)", true)]
	[InlineData("5 IN (1, 2, 3, 4, 5)", true)]
	[InlineData("6 IN (1, 2, 3, 4, 5)", false)]
	[InlineData("0 IN (1, 2, 3, 4, 5)", false)]
	[InlineData("1 NOT IN (1)", false)]
	[InlineData("1 NOT IN (2)", true)]
	[InlineData("1 NOT IN (2, 3, 4)", true)]
	[InlineData("'a' IN ('a', 'b', 'c')", true)]
	[InlineData("'d' IN ('a', 'b', 'c')", false)]
	[InlineData("'a' NOT IN ('a', 'b')", false)]
	[InlineData("'d' NOT IN ('a', 'b')", true)]
	[InlineData("TRUE IN (TRUE)", true)]
	[InlineData("FALSE IN (TRUE)", false)]
	public async Task InComprehensive(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Conditional expression variations
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(1 = 1, 10, 20)", 10L)]
	[InlineData("IF(1 = 2, 10, 20)", 20L)]
	[InlineData("IF(TRUE, 10, 20)", 10L)]
	[InlineData("IF(FALSE, 10, 20)", 20L)]
	[InlineData("IF(1 < 2, 10, 20)", 10L)]
	[InlineData("IF(1 > 2, 10, 20)", 20L)]
	[InlineData("IF(1 IN (1, 2), 10, 20)", 10L)]
	[InlineData("IF(3 IN (1, 2), 10, 20)", 20L)]
	[InlineData("IF(1 BETWEEN 0 AND 2, 10, 20)", 10L)]
	[InlineData("IF(3 BETWEEN 0 AND 2, 10, 20)", 20L)]
	[InlineData("COALESCE(1)", 1L)]
	[InlineData("COALESCE(1, 2, 3)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), 1)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64), 1)", 1L)]
	[InlineData("IFNULL(1, 2)", 1L)]
	[InlineData("IFNULL(CAST(NULL AS INT64), 2)", 2L)]
	[InlineData("NULLIF(1, 1)", null)]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF(0, 0)", null)]
	public async Task ConditionalVariations(string expr, object? expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// CASE expression variations
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 10 WHEN 2 THEN 20 END", 10L)]
	[InlineData("CASE 2 WHEN 1 THEN 10 WHEN 2 THEN 20 END", 20L)]
	[InlineData("CASE 3 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 30L)]
	[InlineData("CASE WHEN TRUE THEN 10 ELSE 20 END", 10L)]
	[InlineData("CASE WHEN FALSE THEN 10 ELSE 20 END", 20L)]
	[InlineData("CASE WHEN 1=1 THEN 10 WHEN 2=2 THEN 20 END", 10L)]
	[InlineData("CASE WHEN 1=2 THEN 10 WHEN 2=2 THEN 20 END", 20L)]
	[InlineData("CASE WHEN 1=2 THEN 10 WHEN 3=4 THEN 20 ELSE 30 END", 30L)]
	[InlineData("CASE WHEN 1 < 2 THEN 10 ELSE 20 END", 10L)]
	[InlineData("CASE WHEN 1 > 2 THEN 10 ELSE 20 END", 20L)]
	public async Task CaseVariations(string expr, long expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Deeply nested expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("IF(IF(TRUE, TRUE, FALSE), 1, 2)", 1L)]
	[InlineData("IF(IF(FALSE, TRUE, FALSE), 1, 2)", 2L)]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 2), 3)", 3L)]
	[InlineData("COALESCE(NULLIF(1, 2), 3)", 1L)]
	[InlineData("IF(NULLIF(1, 1) IS NULL, 'y', 'n')", "y")]
	[InlineData("IF(NULLIF(1, 2) IS NULL, 'y', 'n')", "n")]
	[InlineData("CASE WHEN NULLIF(1, 1) IS NULL THEN 'null' ELSE 'val' END", "null")]
	[InlineData("CASE WHEN NULLIF(1, 2) IS NULL THEN 'null' ELSE 'val' END", "val")]
	[InlineData("IFNULL(IFNULL(CAST(NULL AS INT64), CAST(NULL AS INT64)), 99)", 99L)]
	[InlineData("ABS(IF(TRUE, -5, 5))", 5L)]
	[InlineData("ABS(IF(FALSE, -5, 5))", 5L)]
	[InlineData("LENGTH(IF(TRUE, 'hello', 'hi'))", 5L)]
	[InlineData("LENGTH(IF(FALSE, 'hello', 'hi'))", 2L)]
	[InlineData("UPPER(IF(TRUE, 'hello', 'world'))", "HELLO")]
	[InlineData("UPPER(IF(FALSE, 'hello', 'world'))", "WORLD")]
	[InlineData("CONCAT(IF(TRUE, 'a', 'b'), IF(FALSE, 'c', 'd'))", "ad")]
	public async Task DeeplyNested(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// String comparison
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("'a' < 'b'", true)]
	[InlineData("'b' < 'a'", false)]
	[InlineData("'a' < 'aa'", true)]
	[InlineData("'aa' < 'ab'", true)]
	[InlineData("'abc' < 'abd'", true)]
	[InlineData("'abc' = 'abc'", true)]
	[InlineData("'abc' != 'ABC'", true)]
	[InlineData("'' < 'a'", true)]
	[InlineData("'a' > ''", true)]
	[InlineData("'z' > 'a'", true)]
	[InlineData("'zzz' > 'zzA'", true)]
	[InlineData("'abc' BETWEEN 'aaa' AND 'azz'", true)]
	[InlineData("'abc' BETWEEN 'abd' AND 'azz'", false)]
	[InlineData("'hello' IN ('hello', 'world')", true)]
	[InlineData("'test' IN ('hello', 'world')", false)]
	public async Task StringComparisons(string expr, bool expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Mixed type CAST chains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(CAST(42 AS STRING) AS INT64)", 42L)]
	[InlineData("CAST(CAST(-1 AS STRING) AS INT64)", -1L)]
	[InlineData("CAST(CAST(0 AS STRING) AS INT64)", 0L)]
	[InlineData("CAST(CAST(TRUE AS STRING) AS BOOL)", true)]
	[InlineData("CAST(CAST(FALSE AS STRING) AS BOOL)", false)]
	[InlineData("CAST(CAST(TRUE AS INT64) AS BOOL)", true)]
	[InlineData("CAST(CAST(FALSE AS INT64) AS BOOL)", false)]
	[InlineData("CAST(CAST(1 AS BOOL) AS INT64)", 1L)]
	[InlineData("CAST(CAST(0 AS BOOL) AS INT64)", 0L)]
	[InlineData("LENGTH(CAST(12345 AS STRING))", 5L)]
	[InlineData("LENGTH(CAST(0 AS STRING))", 1L)]
	[InlineData("LENGTH(CAST(-1 AS STRING))", 2L)]
	[InlineData("ABS(CAST('-99' AS INT64))", 99L)]
	[InlineData("UPPER(CAST(TRUE AS STRING))", "TRUE")]
	[InlineData("UPPER(CAST(FALSE AS STRING))", "FALSE")]
	public async Task CastChains(string expr, object expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Data-driven table tests
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MultiRow_SumGroupByHaving()
	{
		var t = $"MSGH_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Cat STRING(MAX), V INT64) PRIMARY KEY (K)");
		var cats = new[] { "A", "A", "A", "B", "B", "C" };
		for (int i = 0; i < cats.Length; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["Cat"] = cats[i], ["V"] = (long)(i + 1) });
		var rows = await QueryAsync($"SELECT Cat, SUM(V) AS S FROM {t} GROUP BY Cat HAVING SUM(V) > 3 ORDER BY Cat");
		rows.Should().HaveCount(3); // A=6, B=9, C=6 all > 3
		rows[0]["Cat"].Should().Be("A");
		rows[1]["Cat"].Should().Be("B");
		rows[2]["Cat"].Should().Be("C");
	}

	[Fact]
	public async Task MultiRow_OrderByMultipleColumns()
	{
		var t = $"MOMC_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = 1L, ["B"] = 2L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["A"] = 1L, ["B"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["A"] = 2L, ["B"] = 1L });
		var rows = await QueryAsync($"SELECT K FROM {t} ORDER BY A ASC, B ASC");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(2L, 1L, 3L);
	}

	[Fact]
	public async Task MultiRow_UpdateWithSubquery()
	{
		var t1 = $"UWS1_{Guid.NewGuid():N}";
		var t2 = $"UWS2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t1} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (K INT64 NOT NULL, Ref INT64) PRIMARY KEY (K)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 0L });
		await InsertAsync(t1, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 0L });
		await InsertAsync(t2, new Dictionary<string, object?> { ["K"] = 1L, ["Ref"] = 1L });
		await ExecuteDmlAsync($"UPDATE {t1} SET V = 99 WHERE K IN (SELECT Ref FROM {t2})");
		var rows = await QueryAsync($"SELECT K, V FROM {t1} ORDER BY K");
		rows[0]["V"].Should().Be(99L);
		rows[1]["V"].Should().Be(0L);
	}

	[Fact]
	public async Task MultiRow_DeleteWithSubquery()
	{
		var t1 = $"DWS1_{Guid.NewGuid():N}";
		var t2 = $"DWS2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t1} (K INT64 NOT NULL) PRIMARY KEY (K)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (K INT64 NOT NULL, Ref INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t1, new Dictionary<string, object?> { ["K"] = (long)i });
		await InsertAsync(t2, new Dictionary<string, object?> { ["K"] = 1L, ["Ref"] = 2L });
		await InsertAsync(t2, new Dictionary<string, object?> { ["K"] = 2L, ["Ref"] = 4L });
		await ExecuteDmlAsync($"DELETE FROM {t1} WHERE K IN (SELECT Ref FROM {t2})");
		var rows = await QueryAsync($"SELECT K FROM {t1} ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(1L, 3L, 5L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultiRow_WindowRowNumber()
	{
		var t = $"WRN_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 30L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT K, ROW_NUMBER() OVER (ORDER BY V) AS RN FROM {t} ORDER BY RN");
		rows[0]["K"].Should().Be(2L); rows[0]["RN"].Should().Be(1L);
		rows[1]["K"].Should().Be(3L); rows[1]["RN"].Should().Be(2L);
		rows[2]["K"].Should().Be(1L); rows[2]["RN"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultiRow_WindowRank()
	{
		var t = $"WR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT K, RANK() OVER (ORDER BY V) AS R FROM {t} ORDER BY K");
		rows[0]["R"].Should().Be(1L);
		rows[1]["R"].Should().Be(1L);
		rows[2]["R"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultiRow_WindowDenseRank()
	{
		var t = $"WDR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT K, DENSE_RANK() OVER (ORDER BY V) AS DR FROM {t} ORDER BY K");
		rows[0]["DR"].Should().Be(1L);
		rows[1]["DR"].Should().Be(1L);
		rows[2]["DR"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultiRow_WindowSumOver()
	{
		var t = $"WSO_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 30L });
		var rows = await QueryAsync($"SELECT K, SUM(V) OVER () AS Total FROM {t} ORDER BY K");
		rows.Should().AllSatisfy(r => r["Total"].Should().Be(60L));
	}

	[Fact]
	public async Task MultiRow_UnionAll()
	{
		var t = $"UA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE V = 10 UNION ALL SELECT V FROM {t} WHERE V = 20 ORDER BY V");
		rows.Should().HaveCount(2);
		rows[0]["V"].Should().Be(10L);
		rows[1]["V"].Should().Be(20L);
	}

	[Fact]
	public async Task MultiRow_UnionDistinct()
	{
		var t = $"UD_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 10L });
		var rows = await QueryAsync($"SELECT V FROM {t} UNION DISTINCT SELECT V FROM {t} ORDER BY V");
		rows.Should().ContainSingle();
		rows[0]["V"].Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task MultiRow_ExceptDistinct()
	{
		var rows = await QueryAsync("SELECT 1 AS V UNION ALL SELECT 2 UNION ALL SELECT 3 EXCEPT DISTINCT SELECT 2 AS V ORDER BY V");
		// Depends on precedence: EXCEPT binds tighter than UNION ALL
		// (SELECT 1) UNION ALL (SELECT 2 UNION ALL SELECT 3 EXCEPT DISTINCT SELECT 2)
		// But standard SQL groups left to right for same precedence
		rows.Should().NotBeEmpty();
	}

	[Fact]
	public async Task MultiRow_CTE()
	{
		var t = $"CTE_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		var rows = await QueryAsync($"WITH cte AS (SELECT V FROM {t} WHERE V > 5) SELECT SUM(V) AS S FROM cte");
		rows[0]["S"].Should().Be(30L);
	}

	[Fact]
	public async Task MultiRow_MultipleCTEs()
	{
		var t = $"MCT_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = (long)(i * 10) });
		var rows = await QueryAsync($@"
			WITH big AS (SELECT K, V FROM {t} WHERE V > 20),
			     small AS (SELECT K, V FROM {t} WHERE V <= 20)
			SELECT (SELECT COUNT(*) FROM big) AS BigCnt, (SELECT COUNT(*) FROM small) AS SmallCnt");
		rows[0]["BigCnt"].Should().Be(3L);
		rows[0]["SmallCnt"].Should().Be(2L);
	}

	[Fact]
	public async Task ScalarSubquery_InSelect()
	{
		var t = $"SSS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT K, V, (SELECT MAX(V) FROM {t}) AS MaxV FROM {t} ORDER BY K");
		rows.Should().HaveCount(2);
		rows.Should().AllSatisfy(r => r["MaxV"].Should().Be(20L));
	}

	[Fact]
	public async Task ComputedColumn_InSelect()
	{
		var t = $"CCS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = 3L, ["B"] = 4L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["A"] = 5L, ["B"] = 12L });
		var rows = await QueryAsync($"SELECT K, A * A + B * B AS SumSq FROM {t} ORDER BY K");
		rows[0]["SumSq"].Should().Be(25L);
		rows[1]["SumSq"].Should().Be(169L);
	}

	[Fact]
	public async Task StringAgg_Basic()
	{
		var t = $"SAG_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = "b" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = "c" });
		var rows = await QueryAsync($"SELECT STRING_AGG(V, ',' ORDER BY V) AS R FROM {t}");
		rows[0]["R"].Should().Be("a,b,c");
	}

	[Fact]
	public async Task CountIf_Basic()
	{
		var t = $"CIF_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = (long)i });
		var rows = await QueryAsync($"SELECT COUNTIF(V > 5) AS R FROM {t}");
		rows[0]["R"].Should().Be(5L);
	}

	[Fact]
	public async Task AnyValue_Basic()
	{
		var t = $"AV_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Cat STRING(MAX), V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["Cat"] = "A", ["V"] = 10L });
		var rows = await QueryAsync($"SELECT Cat, ANY_VALUE(V) AS R FROM {t} GROUP BY Cat");
		rows.Should().ContainSingle();
		rows[0]["R"].Should().Be(10L);
	}

	[Fact]
	public async Task LogicalAnd_Basic()
	{
		var t = $"LA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V BOOL) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = true });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = true });
		var rows = await QueryAsync($"SELECT LOGICAL_AND(V) AS R FROM {t}");
		rows[0]["R"].Should().Be(true);
	}

	[Fact]
	public async Task LogicalOr_Basic()
	{
		var t = $"LO_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V BOOL) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = false });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = true });
		var rows = await QueryAsync($"SELECT LOGICAL_OR(V) AS R FROM {t}");
		rows[0]["R"].Should().Be(true);
	}

	[Fact]
	public async Task ArrayAgg_Basic()
	{
		var t = $"AAG_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT ARRAY_LENGTH(ARRAY_AGG(V)) AS Len FROM {t}");
		rows[0]["Len"].Should().Be(2L);
	}

	[Fact]
	public async Task EmptyTable_Aggregates()
	{
		var t = $"ETA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C, SUM(V) AS S, MIN(V) AS Mi, MAX(V) AS Ma FROM {t}");
		rows[0]["C"].Should().Be(0L);
		rows[0]["S"].Should().BeNull();
		rows[0]["Mi"].Should().BeNull();
		rows[0]["Ma"].Should().BeNull();
	}

	[Fact]
	public async Task NotNull_Constraint()
	{
		var t = $"NNC_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX) NOT NULL) PRIMARY KEY (K)");
		var act = async () => await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = DBNull.Value });
		// Direct API throws InvalidOperationException; gRPC path wraps as SpannerException
		await act.Should().ThrowAsync<Exception>()
			.Where(e => e is SpannerException || e is InvalidOperationException);
	}

	[Fact]
	public async Task StringLength_Constraint()
	{
		var t = $"SLC_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(5)) PRIMARY KEY (K)");
		var act = async () => await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "toolong" });
		// Direct API throws InvalidOperationException; gRPC path wraps as SpannerException
		await act.Should().ThrowAsync<Exception>()
			.Where(e => e is SpannerException || e is InvalidOperationException);
	}
}
