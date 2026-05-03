using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive subquery tests: scalar, EXISTS, IN, correlated, derived tables, CTEs,
/// lateral, ARRAY subquery, and complex nesting.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SubqueryExhaustiveIntegrationTests : IntegrationTestBase
{
	public SubqueryExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<(string main, string detail)> SeedTables()
	{
		var m = $"SubM_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		var d = $"SubD_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {m} (Id INT64 NOT NULL, Name STRING(MAX), Category STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {d} (Id INT64 NOT NULL, MainId INT64, Amount INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {m} (Id, Name, Category) VALUES (1, 'Alice', 'A'), (2, 'Bob', 'B'), (3, 'Charlie', 'A')");
		await ExecuteDmlAsync($"INSERT INTO {d} (Id, MainId, Amount) VALUES (1, 1, 100), (2, 1, 200), (3, 2, 150), (4, 3, 50)");
		return (m, d);
	}

	// ─── Scalar subquery ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task ScalarSubquery_InSelect()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name, (SELECT SUM(Amount) FROM {d} WHERE MainId = {m}.Id) AS Total FROM {m} ORDER BY Name");
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Total"].Should().Be(300L);
		rows[1]["Name"].Should().Be("Bob");
		rows[1]["Total"].Should().Be(150L);
		rows[2]["Name"].Should().Be("Charlie");
		rows[2]["Total"].Should().Be(50L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task ScalarSubquery_InWhere()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE (SELECT SUM(Amount) FROM {d} WHERE MainId = {m}.Id) > 100 ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	// ─── EXISTS subquery ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Exists_Subquery()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE EXISTS (SELECT 1 FROM {d} WHERE MainId = {m}.Id AND Amount > 100) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task NotExists_Subquery()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE NOT EXISTS (SELECT 1 FROM {d} WHERE MainId = {m}.Id AND Amount > 100) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Charlie" });
	}

	// ─── IN subquery ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task In_Subquery()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE Id IN (SELECT MainId FROM {d} WHERE Amount >= 150) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task NotIn_Subquery()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE Id NOT IN (SELECT MainId FROM {d} WHERE Amount >= 150) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Charlie" });
	}

	// ─── Derived table (subquery in FROM) ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task DerivedTable_InFrom()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT sub.TotalAmt FROM (SELECT SUM(Amount) AS TotalAmt FROM {d}) AS sub");
		rows[0]["TotalAmt"].Should().Be(500L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task DerivedTable_WithGroupBy()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT sub.MainId, sub.Total FROM (SELECT MainId, SUM(Amount) AS Total FROM {d} GROUP BY MainId) AS sub ORDER BY sub.MainId");
		rows.Should().HaveCount(3);
		rows[0]["Total"].Should().Be(300L);
	}

	// ─── CTE (WITH) ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Cte_Simple()
	{
		var (m, _) = await SeedTables();
		var rows = await QueryAsync(
			$"WITH cte AS (SELECT * FROM {m} WHERE Category = 'A') SELECT Name FROM cte ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Charlie" });
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Cte_MultipleCtes()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$@"WITH 
				cat_a AS (SELECT Id, Name FROM {m} WHERE Category = 'A'),
				totals AS (SELECT MainId, SUM(Amount) AS Total FROM {d} GROUP BY MainId)
			SELECT cat_a.Name, totals.Total 
			FROM cat_a JOIN totals ON cat_a.Id = totals.MainId 
			ORDER BY cat_a.Name");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Total"].Should().Be(300L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Cte_ReferencedMultipleTimes()
	{
		var (m, _) = await SeedTables();
		var rows = await QueryAsync(
			$@"WITH cte AS (SELECT COUNT(*) AS Cnt FROM {m})
			SELECT c1.Cnt + c2.Cnt AS DoubleCount
			FROM cte c1 CROSS JOIN cte c2");
		rows[0]["DoubleCount"].Should().Be(6L);
	}

	// ─── Correlated subquery ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task CorrelatedSubquery_Count()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name, (SELECT COUNT(*) FROM {d} WHERE MainId = {m}.Id) AS DetailCount FROM {m} ORDER BY Name");
		rows[0]["DetailCount"].Should().Be(2L); // Alice
		rows[1]["DetailCount"].Should().Be(1L); // Bob
		rows[2]["DetailCount"].Should().Be(1L); // Charlie
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task CorrelatedSubquery_Max()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name, (SELECT MAX(Amount) FROM {d} WHERE MainId = {m}.Id) AS MaxAmt FROM {m} ORDER BY Name");
		rows[0]["MaxAmt"].Should().Be(200L); // Alice
		rows[1]["MaxAmt"].Should().Be(150L); // Bob
		rows[2]["MaxAmt"].Should().Be(50L);  // Charlie
	}

	// ─── ARRAY subquery ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task ArraySubquery()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT v FROM UNNEST(ARRAY(SELECT Amount FROM {d} WHERE MainId = 1 ORDER BY Amount)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(100L, 200L);
	}

	// ─── Subquery with LIMIT ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Subquery_WithLimit()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Amount FROM (SELECT Amount FROM {d} ORDER BY Amount DESC LIMIT 2) AS sub ORDER BY Amount DESC");
		rows.Should().HaveCount(2);
		rows[0]["Amount"].Should().Be(200L);
		rows[1]["Amount"].Should().Be(150L);
	}

	// ─── Subquery with DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Subquery_WithDistinct()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT MainId FROM (SELECT DISTINCT MainId FROM {d}) AS sub ORDER BY MainId");
		rows.Should().HaveCount(3);
	}

	// ─── Deep nesting ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task DeepNesting_ThreeLevels()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Amount FROM (SELECT Amount FROM (SELECT Amount FROM {d} WHERE Amount > 50) AS inner1) AS outer1 ORDER BY Amount");
		rows.Should().HaveCount(3);
	}

	// ─── Subquery returns no rows ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task ScalarSubquery_NoRows_ReturnsNull()
	{
		var (m, d) = await SeedTables();
		// MainId = 999 has no detail rows, SUM returns NULL
		var rows = await QueryAsync(
			$"SELECT (SELECT SUM(Amount) FROM {d} WHERE MainId = 999) AS Total");
		rows[0]["Total"].Should().BeNull();
	}

	// ─── In subquery with literals ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task In_LiteralSubquery()
	{
		var (m, _) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT Name FROM {m} WHERE Id IN (SELECT x FROM UNNEST([1, 3]) AS x) ORDER BY Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Charlie" });
	}

	// ─── WITH RECURSIVE not supported, but CTE + JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Cte_JoinWithMainTable()
	{
		var (m, d) = await SeedTables();
		var rows = await QueryAsync(
			$@"WITH totals AS (SELECT MainId, SUM(Amount) AS Total FROM {d} GROUP BY MainId)
			SELECT m.Name, t.Total FROM {m} m JOIN totals t ON m.Id = t.MainId WHERE t.Total > 100 ORDER BY m.Name");
		rows.Select(r => (string)r["Name"]!).Should().BeEquivalentTo(new[] { "Alice", "Bob" });
	}

	// ─── Subquery in HAVING ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Subquery_InHaving()
	{
		var (_, d) = await SeedTables();
		var rows = await QueryAsync(
			$"SELECT MainId, SUM(Amount) AS Total FROM {d} GROUP BY MainId HAVING SUM(Amount) > (SELECT AVG(Amount) FROM {d}) ORDER BY MainId");
		// AVG = 500/4 = 125. MainId=1 has 300, MainId=2 has 150
		rows.Should().HaveCount(2);
	}

	// ─── Subquery with ORDER BY and NULL ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task Subquery_OrderByNull()
	{
		var t = $"SubNull_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, NULL), (2, 10), (3, 20)");
		var rows = await QueryAsync($"SELECT Val FROM (SELECT Val FROM {t} ORDER BY Val) sub");
		rows.Should().HaveCount(3);
	}

	// ─── UNION ALL in subquery FROM ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task UnionAll_InSubqueryFrom_ReturnsAllRows()
	{
		var rows = await QueryAsync(
			"SELECT * FROM (SELECT 1 AS x UNION ALL SELECT 2 AS x UNION ALL SELECT 3 AS x) AS sub ORDER BY x");
		rows.Should().HaveCount(3);
		rows.Select(r => (long)r["x"]!).Should().Equal(1L, 2L, 3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task UnionAll_InSubqueryFrom_Aggregate()
	{
		var rows = await QueryAsync(
			"SELECT SUM(x) AS s FROM (SELECT 1 AS x UNION ALL SELECT 2 AS x) AS sub");
		rows[0]["s"].Should().Be(3L);
	}

	// ─── UNION ALL in CTE body ───
	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task UnionAll_InCteBody_Sum()
	{
		var rows = await QueryAsync(
			"WITH nums AS (SELECT 1 AS n UNION ALL SELECT 2 AS n UNION ALL SELECT 3 AS n) SELECT SUM(n) AS s FROM nums");
		rows[0]["s"].Should().Be(6L);
	}

	[Fact]
	[Trait(TestTraits.Category, "SubqueryExhaustive")]
	public async Task UnionAll_InCteBody_Count()
	{
		var rows = await QueryAsync(
			"WITH nums AS (SELECT 1 AS n UNION ALL SELECT 2 AS n) SELECT COUNT(*) AS c FROM nums");
		rows[0]["c"].Should().Be(2L);
	}
}
