using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive query pattern tests: SELECT, FROM, WHERE, ORDER BY, LIMIT, OFFSET,
/// DISTINCT, subqueries, EXISTS, aliases, and combinations thereof.
/// Also covers UNION, INTERSECT, EXCEPT, CTE (WITH).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class QueryPatternExhaustiveIntegrationTests : IntegrationTestBase
{
	private const string T = "QpExhT";

	public QueryPatternExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTable()
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE IF NOT EXISTS {T} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, Category STRING(MAX)) PRIMARY KEY (Id)");
		for (long i = 1; i <= 10; i++)
		{
			await InsertOrUpdateAsync(T, new Dictionary<string, object?>
			{
				["Id"] = i,
				["Name"] = $"item{i}",
				["Val"] = i * 10,
				["Category"] = i <= 5 ? "A" : "B"
			});
		}
	}

	// ─── Basic SELECT ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Select_AllColumns()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Id");
		rows.Should().HaveCount(10);
		rows[0]["Id"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Select_SpecificColumns()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Name FROM {T} ORDER BY Id LIMIT 1");
		rows[0]["Id"].Should().Be(1L);
		rows[0]["Name"].Should().Be("item1");
		rows[0].ContainsKey("Val").Should().BeFalse();
	}

	// ─── WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_Equals()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("item1");
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_NotEquals()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id != 1");
		rows.Should().HaveCount(9);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_GreaterThan()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id > 8");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_LessThan()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id < 3");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_And()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Category = 'A' AND Val > 30");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_Or()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id = 1 OR Id = 10");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_In()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id IN (1, 3, 5)");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_NotIn()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id NOT IN (1, 2, 3)");
		rows.Should().HaveCount(7);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_Between()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id BETWEEN 3 AND 7");
		rows.Should().HaveCount(5);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_Like()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Name LIKE 'item1%'");
		rows.Should().HaveCountGreaterOrEqualTo(2); // item1, item10
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_IsNull()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Name IS NOT NULL");
		rows.Should().HaveCount(10);
	}

	// ─── ORDER BY ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task OrderBy_Asc()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Val ASC");
		((long)rows[0]["Val"]!).Should().BeLessOrEqualTo((long)rows[1]["Val"]!);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task OrderBy_Desc()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Val DESC");
		((long)rows[0]["Val"]!).Should().BeGreaterOrEqualTo((long)rows[1]["Val"]!);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task OrderBy_MultipleColumns()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Category ASC, Val DESC");
		rows[0]["Category"].Should().Be("A");
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task OrderBy_Expression()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, Val * 2 AS DoubleVal FROM {T} ORDER BY Val * 2 DESC LIMIT 1");
		rows[0]["Id"].Should().Be(10L);
	}

	// ─── LIMIT / OFFSET ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Limit()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Id LIMIT 3");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Limit_Offset()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Id LIMIT 3 OFFSET 2");
		rows.Should().HaveCount(3);
		rows[0]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Limit_1()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Id LIMIT 1");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Limit_0()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} LIMIT 0");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Offset_BeyondRows()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} ORDER BY Id LIMIT 10 OFFSET 100");
		rows.Should().BeEmpty();
	}

	// ─── DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Distinct()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT DISTINCT Category FROM {T} ORDER BY Category");
		rows.Should().HaveCount(2);
		rows[0]["Category"].Should().Be("A");
		rows[1]["Category"].Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Distinct_Count()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT Category) AS C FROM {T}");
		rows[0]["C"].Should().Be(2L);
	}

	// ─── Column aliases ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task ColumnAlias()
	{
		var rows = await QueryAsync("SELECT 42 AS answer, 'hello' AS greeting");
		rows[0]["answer"].Should().Be(42L);
		rows[0]["greeting"].Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task ExpressionAlias()
	{
		var rows = await QueryAsync("SELECT 1 + 2 AS sum, 10 * 5 AS product");
		rows[0]["sum"].Should().Be(3L);
		rows[0]["product"].Should().Be(50L);
	}

	// ─── Subquery in SELECT ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Scalar_Subquery()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT (SELECT COUNT(*) FROM {T}) AS C");
		((long)rows[0]["C"]!).Should().Be(10L);
	}

	// ─── EXISTS ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Exists_True()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT EXISTS(SELECT 1 FROM {T} WHERE Id = 1) AS E");
		rows[0]["E"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Exists_False()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT EXISTS(SELECT 1 FROM {T} WHERE Id = 999) AS E");
		rows[0]["E"].Should().Be(false);
	}

	// ─── IN (subquery) ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task In_Subquery()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE Id IN (SELECT Id FROM {T} WHERE Category = 'A') ORDER BY Id");
		rows.Should().HaveCount(5);
	}

	// ─── UNION ALL ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task UnionAll()
	{
		var rows = await QueryAsync("SELECT 1 AS X UNION ALL SELECT 2 UNION ALL SELECT 3");
		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task UnionAll_Duplicates()
	{
		var rows = await QueryAsync("SELECT 1 AS X UNION ALL SELECT 1 UNION ALL SELECT 1");
		rows.Should().HaveCount(3);
	}

	// ─── UNION DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task UnionDistinct()
	{
		var rows = await QueryAsync("SELECT 1 AS X UNION DISTINCT SELECT 1 UNION DISTINCT SELECT 2");
		rows.Should().HaveCount(2);
	}

	// ─── CTE (WITH) ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task With_CTE_Aggregate()
	{
		var rows = await QueryAsync("WITH nums AS (SELECT x FROM UNNEST([1,2,3,4,5]) AS x) SELECT SUM(x) AS S FROM nums");
		rows[0]["S"].Should().Be(15L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task With_CTE_Multiple()
	{
		var rows = await QueryAsync(
			"WITH a AS (SELECT 1 AS x), b AS (SELECT 2 AS y) SELECT a.x, b.y FROM a, b");
		rows[0]["x"].Should().Be(1L);
		rows[0]["y"].Should().Be(2L);
	}

	// ─── UNNEST ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Unnest_Basic()
	{
		var rows = await QueryAsync("SELECT x FROM UNNEST([1,2,3]) AS x ORDER BY x");
		rows.Should().HaveCount(3);
		rows[0]["x"].Should().Be(1L);
		rows[1]["x"].Should().Be(2L);
		rows[2]["x"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Unnest_Strings()
	{
		var rows = await QueryAsync("SELECT x FROM UNNEST(['a','b','c']) AS x ORDER BY x");
		rows.Should().HaveCount(3);
		rows[0]["x"].Should().Be("a");
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Unnest_WithOrdinal()
	{
		var rows = await QueryAsync("SELECT x, off FROM UNNEST(['a','b','c']) AS x WITH OFFSET AS off ORDER BY off");
		rows.Should().HaveCount(3);
		rows[0]["off"].Should().Be(0L);
		rows[1]["off"].Should().Be(1L);
	}

	// ─── Literal types ───
	[Theory]
	[InlineData("SELECT 42 AS R", 42L)]
	[InlineData("SELECT -1 AS R", -1L)]
	[InlineData("SELECT 0 AS R", 0L)]
	[InlineData("SELECT 3.14 AS R", 3.14)]
	[InlineData("SELECT TRUE AS R", true)]
	[InlineData("SELECT FALSE AS R", false)]
	[InlineData("SELECT 'hello' AS R", "hello")]
	[InlineData("SELECT '' AS R", "")]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Literals(string sql, object expected)
	{
		var rows = await QueryAsync(sql);
		if (expected is double d)
			((double)rows[0]["R"]!).Should().BeApproximately(d, 1e-10);
		else
			rows[0]["R"].Should().Be(expected);
	}

	// ─── NULL literal ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task NullLiteral()
	{
		var rows = await QueryAsync("SELECT NULL AS R");
		rows[0]["R"].Should().BeNull();
	}

	// ─── Table alias ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task TableAlias()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT t.Id FROM {T} t WHERE t.Id = 1");
		rows.Should().HaveCount(1);
	}

	// ─── Multiple columns with expressions ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Computed_Columns()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Id, Val, Val * 2 AS DoubleVal, Val + 100 AS PlusHundred FROM {T} WHERE Id = 1");
		rows[0]["Val"].Should().Be(10L);
		rows[0]["DoubleVal"].Should().Be(20L);
		rows[0]["PlusHundred"].Should().Be(110L);
	}

	// ─── WHERE with functions ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Where_WithFunction()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT * FROM {T} WHERE LENGTH(Name) > 5 ORDER BY Id");
		rows.All(r => ((string)r["Name"]!).Length > 5).Should().BeTrue();
	}

	// ─── GROUP BY with WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task GroupBy_WithWhere()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT Category, COUNT(*) AS C FROM {T} WHERE Val > 20 GROUP BY Category ORDER BY Category");
		rows.Should().HaveCountGreaterOrEqualTo(1);
	}

	// ─── SELECT 1 (no table) ───
	[Theory]
	[InlineData("SELECT 1", 1L)]
	[InlineData("SELECT 1 + 1", 2L)]
	[InlineData("SELECT 'hello'", "hello")]
	[InlineData("SELECT TRUE", true)]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Select_NoTable(string sql, object expected)
	{
		var rows = await QueryAsync(sql + " AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── Correlated subquery ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Correlated_Subquery()
	{
		await EnsureTable();
		var rows = await QueryAsync($"SELECT t.Id, (SELECT COUNT(*) FROM {T} t2 WHERE t2.Category = t.Category) AS CategoryCount FROM {T} t WHERE t.Id <= 3 ORDER BY t.Id");
		rows.Should().HaveCount(3);
		// All first 3 items are in category A which has 5 items
		((long)rows[0]["CategoryCount"]!).Should().Be(5L);
	}

	// ─── ARRAY literal ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Array_Literal()
	{
		var rows = await QueryAsync("SELECT ARRAY_LENGTH([1,2,3]) AS L");
		rows[0]["L"].Should().Be(3L);
	}

	// ─── Nested queries ───
	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Subquery_InFrom()
	{
		var rows = await QueryAsync("SELECT * FROM (SELECT 1 AS a, 2 AS b)");
		rows[0]["a"].Should().Be(1L);
		rows[0]["b"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "QueryPatternExhaustive")]
	public async Task Subquery_InFrom_WithAlias()
	{
		var rows = await QueryAsync("SELECT sub.a, sub.b FROM (SELECT 1 AS a, 2 AS b) sub");
		rows[0]["a"].Should().Be(1L);
	}
}
