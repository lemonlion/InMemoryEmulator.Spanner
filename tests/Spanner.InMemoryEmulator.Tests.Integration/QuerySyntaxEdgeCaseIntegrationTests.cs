using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Additional query syntax edge cases: UNION ALL/DISTINCT, INTERSECT, EXCEPT,
/// nested subqueries, correlated subqueries, aliasing, expression evaluation order.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class QuerySyntaxEdgeCaseIntegrationTests : IntegrationTestBase
{
	public QuerySyntaxEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task<List<object?>> EvalMultiRow(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<object?>();
		while (await reader.ReadAsync())
		{
			results.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
		}
		return results;
	}

	private async Task<long> RowCount(string sql)
	{
		var results = await EvalMultiRow(sql);
		return results.Count;
	}

	// ═══════════════════════════════════════════════════════════════
	// UNION ALL
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnionAll_PreservesDuplicates()
	{
		var count = await RowCount("SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 2");
		count.Should().Be(3);
	}

	[Fact]
	public async Task UnionAll_MaintainsOrder()
	{
		var results = await EvalMultiRow("SELECT 3 AS x UNION ALL SELECT 1 UNION ALL SELECT 2");
		results.Should().BeEquivalentTo(new object[] { 3L, 1L, 2L });
	}

	// ═══════════════════════════════════════════════════════════════
	// UNION DISTINCT (bare UNION defaults to DISTINCT)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnionDistinct_RemovesDuplicates()
	{
		var count = await RowCount("SELECT 1 AS x UNION DISTINCT SELECT 1 UNION DISTINCT SELECT 2");
		count.Should().Be(2);
	}

	[Fact]
	public async Task Union_Bare_DefaultsToDistinct()
	{
		var count = await RowCount("SELECT 1 AS x UNION SELECT 1 UNION SELECT 2");
		count.Should().Be(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// INTERSECT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IntersectDistinct_ReturnsCommon()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) " +
			"INTERSECT DISTINCT " +
			"SELECT x FROM (SELECT 2 AS x UNION ALL SELECT 3 UNION ALL SELECT 4)");
		results.Should().HaveCount(2);
		results.Select(r => (long)r!).Should().BeEquivalentTo(new long[] { 2, 3 });
	}

	[Fact]
	public async Task Intersect_Bare_DefaultsToDistinct()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2) " +
			"INTERSECT " +
			"SELECT x FROM (SELECT 2 AS x UNION ALL SELECT 3)");
		results.Should().HaveCount(1);
		results[0].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// EXCEPT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ExceptDistinct_RemovesMatches()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) " +
			"EXCEPT DISTINCT " +
			"SELECT x FROM (SELECT 2 AS x)");
		results.Should().HaveCount(2);
		results.Select(r => (long)r!).Should().BeEquivalentTo(new long[] { 1, 3 });
	}

	[Fact]
	public async Task Except_Bare_DefaultsToDistinct()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2) " +
			"EXCEPT " +
			"SELECT x FROM (SELECT 2 AS x)");
		results.Should().HaveCount(1);
		results[0].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NestedSubquery_InFrom()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2))");
		results.Should().HaveCount(2);
	}

	[Fact]
	public async Task NestedSubquery_InWhere()
	{
		var results = await EvalMultiRow(
			"SELECT x FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) " +
			"WHERE x > (SELECT 1)");
		results.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// SELECT with aliases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SelectAlias_CanBeUsedInOrderBy()
	{
		var results = await EvalMultiRow(
			"SELECT x AS y FROM (SELECT 3 AS x UNION ALL SELECT 1 UNION ALL SELECT 2) ORDER BY y");
		results[0].Should().Be(1L);
		results[1].Should().Be(2L);
		results[2].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SELECT with expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("SELECT 1 + 2", 3L)]
	[InlineData("SELECT 1 + 2 * 3", 7L)]  // precedence
	[InlineData("SELECT (1 + 2) * 3", 9L)] // parentheses
	[InlineData("SELECT -1 + 2", 1L)]
	[InlineData("SELECT 10 / 3", 3L)]
	public async Task Select_ArithmeticExpressions(string sql, long expected)
	{
		(await Eval(sql)).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// WITH (CTE)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task With_SimpleCte()
	{
		var results = await EvalMultiRow(
			"WITH t AS (SELECT 1 AS x UNION ALL SELECT 2) SELECT x FROM t ORDER BY x");
		results.Should().HaveCount(2);
		results[0].Should().Be(1L);
		results[1].Should().Be(2L);
	}

	[Fact]
	public async Task With_MultipleCtes()
	{
		var result = await Eval(
			"WITH t1 AS (SELECT 10 AS x), t2 AS (SELECT 20 AS x) SELECT t1.x + t2.x FROM t1 CROSS JOIN t2");
		result.Should().Be(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple columns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Select_MultipleColumns()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT 1 AS a, 'hello' AS b, true AS c");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(1L);
		reader.GetString(1).Should().Be("hello");
		reader.GetBoolean(2).Should().BeTrue();
	}

	[Fact]
	public async Task Select_StarFromSubquery()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT * FROM (SELECT 1 AS a, 2 AS b)");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.FieldCount.Should().Be(2);
		reader.GetInt64(0).Should().Be(1L);
		reader.GetInt64(1).Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FOR UPDATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#for_update_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ForUpdate_ParsesAndReturnsResults()
	{
		var result = await Eval("SELECT 1 AS x FOR UPDATE");
		result.Should().Be(1L);
	}

	[Fact]
	public async Task ForUpdate_WithWhereClause()
	{
		var result = await Eval("SELECT 42 AS x WHERE true FOR UPDATE");
		result.Should().Be(42L);
	}

	[Fact]
	public async Task ForUpdate_WithLimitOffset()
	{
		var results = await EvalMultiRow("SELECT x FROM UNNEST([1, 2, 3]) AS x LIMIT 2 OFFSET 1 FOR UPDATE");
		results.Should().HaveCount(2);
	}
}
