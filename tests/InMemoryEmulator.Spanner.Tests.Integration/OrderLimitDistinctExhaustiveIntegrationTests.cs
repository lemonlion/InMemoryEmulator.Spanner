using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive ORDER BY, LIMIT, OFFSET, DISTINCT, and result set formatting tests.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class OrderLimitDistinctExhaustiveIntegrationTests : IntegrationTestBase
{
	public OrderLimitDistinctExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> SeedTable()
	{
		var t = $"OLD_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, Cat STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync(
			$@"INSERT INTO {t} (Id, Name, Val, Cat) VALUES
				(1, 'Charlie', 30, 'A'),
				(2, 'Alice',   10, 'B'),
				(3, 'Bob',     20, 'A'),
				(4, 'Diana',   10, 'B'),
				(5, 'Eve',     40, 'C'),
				(6, 'Frank',   30, 'A')");
		return t;
	}

	// ─── ORDER BY ASC ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Asc()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name ASC");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Alice", "Bob", "Charlie", "Diana", "Eve", "Frank");
	}

	// ─── ORDER BY DESC ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Desc()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name DESC");
		rows.Select(r => (string)r["Name"]!).Should().Equal("Frank", "Eve", "Diana", "Charlie", "Bob", "Alice");
	}

	// ─── ORDER BY multiple columns ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_MultipleColumns()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} ORDER BY Val ASC, Name ASC");
		rows[0]["Name"].Should().Be("Alice");
		rows[1]["Name"].Should().Be("Diana");
		rows[2]["Name"].Should().Be("Bob");
	}

	// ─── ORDER BY mixed ASC/DESC ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_MixedAscDesc()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} ORDER BY Val DESC, Name ASC");
		rows[0]["Name"].Should().Be("Eve");     // Val=40
		rows[1]["Name"].Should().Be("Charlie"); // Val=30
		rows[2]["Name"].Should().Be("Frank");   // Val=30
	}

	// ─── ORDER BY expression ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Expression()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} ORDER BY Val * -1");
		rows[0]["Name"].Should().Be("Eve"); // -40 is smallest
	}

	// ─── ORDER BY alias ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Alias()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, Val AS V FROM {t} ORDER BY V");
		rows[0]["Name"].Should().BeOneOf("Alice", "Diana");
	}

	// ─── ORDER BY ordinal ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Ordinal()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} ORDER BY 2, 1");
		// Ordered by Val then Name
		rows[0]["Name"].Should().Be("Alice");
	}

	// ─── ORDER BY with NULL ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_WithNulls()
	{
		var t = $"OLDNull_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, NULL), (2, 10), (3, 20), (4, NULL)");
		var rows = await QueryAsync($"SELECT Id, Val FROM {t} ORDER BY Val ASC, Id ASC");
		// NULLs sort first in Spanner
		rows[0]["Val"].Should().BeNull();
		rows[1]["Val"].Should().BeNull();
		rows[2]["Val"].Should().Be(10L);
		rows[3]["Val"].Should().Be(20L);
	}

	// ─── LIMIT ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Limit_Basic()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name LIMIT 3");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Limit_1()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Val ASC LIMIT 1");
		rows.Should().HaveCount(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Limit_LargerThanRowCount()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} LIMIT 100");
		rows.Should().HaveCount(6);
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Limit_0()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} LIMIT 0");
		rows.Should().BeEmpty();
	}

	// ─── OFFSET ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Offset_Basic()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name LIMIT 2 OFFSET 2");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Charlie");
		rows[1]["Name"].Should().Be("Diana");
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Offset_BeyondRows()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name LIMIT 10 OFFSET 100");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Offset_ExactBoundary()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name LIMIT 10 OFFSET 5");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Frank");
	}

	// ─── LIMIT with parameterized value ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Limit_WithParam()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY Name LIMIT @lim",
			("lim", SpannerDbType.Int64, (object?)2L));
		rows.Should().HaveCount(2);
	}

	// ─── DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Distinct_Basic()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT DISTINCT Val FROM {t} ORDER BY Val");
		rows.Should().HaveCount(4); // 10, 20, 30, 40
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Distinct_MultipleColumns()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT DISTINCT Cat, Val FROM {t} ORDER BY Cat, Val");
		rows.Should().HaveCountGreaterOrEqualTo(4);
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Distinct_WithNull()
	{
		var t = $"OLDDistN_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, NULL), (2, NULL), (3, 10)");
		var rows = await QueryAsync($"SELECT DISTINCT Val FROM {t}");
		rows.Should().HaveCount(2); // NULL and 10
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Distinct_AllSame()
	{
		var t = $"OLDDistS_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 42), (2, 42), (3, 42)");
		var rows = await QueryAsync($"SELECT DISTINCT Val FROM {t}");
		rows.Should().HaveCount(1);
		rows[0]["Val"].Should().Be(42L);
	}

	// ─── DISTINCT with LIMIT ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task Distinct_WithLimit()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT DISTINCT Val FROM {t} ORDER BY Val LIMIT 2");
		rows.Should().HaveCount(2);
		rows[0]["Val"].Should().Be(10L);
		rows[1]["Val"].Should().Be(20L);
	}

	// ─── ORDER BY + GROUP BY ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task GroupBy_OrderBy_Limit()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Cat, SUM(Val) AS Total FROM {t} GROUP BY Cat ORDER BY Total DESC LIMIT 2");
		rows.Should().HaveCount(2);
		// Cat A: 30+20+30=80, Cat C: 40, Cat B: 10+10=20
		rows[0]["Cat"].Should().Be("A");
		rows[0]["Total"].Should().Be(80L);
	}

	// ─── ORDER BY function result ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task OrderBy_Function()
	{
		var t = await SeedTable();
		var rows = await QueryAsync($"SELECT Name FROM {t} ORDER BY LENGTH(Name) ASC, Name ASC");
		// Bob(3), Eve(3), Alice(5), Diana(5), Frank(5), Charlie(7)
		rows[0]["Name"].Should().Be("Bob");
	}

	// ─── UNION ALL with ORDER BY ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task UnionAll_OrderBy()
	{
		var t = await SeedTable();
		var rows = await QueryAsync(
			$"SELECT Name FROM {t} WHERE Cat = 'A' UNION ALL SELECT Name FROM {t} WHERE Cat = 'B' ORDER BY Name");
		rows.Should().HaveCount(5); // A: Charlie, Bob, Frank; B: Alice, Diana
	}

	// ─── UNION DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	public async Task UnionDistinct()
	{
		var rows = await QueryAsync(
			"SELECT 1 AS V UNION DISTINCT SELECT 1 UNION DISTINCT SELECT 2 ORDER BY V");
		rows.Should().HaveCount(2);
	}

	// ─── DISTINCT on ARRAY values ───

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Distinct_ArrayColumn_ThrowsInvalidArgument()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_distinct
		//   ARRAY columns cannot be used in SELECT DISTINCT.
		//   Real Spanner returns: "Column arr of type ARRAY cannot be used in SELECT DISTINCT"
		var act = () => QueryAsync(
			"SELECT DISTINCT arr FROM (SELECT [1,2,3] AS arr UNION ALL SELECT [1,2,3] AS arr)");
		await act.Should().ThrowAsync<SpannerException>()
			.Where(e => e.ToString().Contains("ARRAY") && e.ToString().Contains("SELECT DISTINCT"));
	}

	[Fact]
	[Trait(TestTraits.Category, "OrderLimitDistinctExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Distinct_ArrayColumn_DifferentArrays_ThrowsInvalidArgument()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_distinct
		//   ARRAY columns cannot be used in SELECT DISTINCT.
		var act = () => QueryAsync(
			"SELECT DISTINCT arr FROM (SELECT [1,2,3] AS arr UNION ALL SELECT [4,5,6] AS arr)");
		await act.Should().ThrowAsync<SpannerException>()
			.Where(e => e.ToString().Contains("ARRAY") && e.ToString().Contains("SELECT DISTINCT"));
	}
}
