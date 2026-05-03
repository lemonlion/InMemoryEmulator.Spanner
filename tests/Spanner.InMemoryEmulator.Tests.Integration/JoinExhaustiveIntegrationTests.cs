using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive JOIN tests across all join types and edge cases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JoinExhaustiveIntegrationTests : IntegrationTestBase
{
	private const string T1 = "JoExhT1";
	private const string T2 = "JoExhT2";
	private const string T3 = "JoExhT3";

	public JoinExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTables()
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE IF NOT EXISTS {T1} (Id INT64 NOT NULL, Name STRING(MAX), CatId INT64) PRIMARY KEY (Id)",
			$"CREATE TABLE IF NOT EXISTS {T2} (Id INT64 NOT NULL, Category STRING(MAX)) PRIMARY KEY (Id)",
			$"CREATE TABLE IF NOT EXISTS {T3} (Id INT64 NOT NULL, T1Id INT64, Val INT64) PRIMARY KEY (Id)");

		// T1: Items
		await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["CatId"] = 1L });
		await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["CatId"] = 2L });
		await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["CatId"] = 1L });
		await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["CatId"] = null });

		// T2: Categories
		await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "Engineering" });
		await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "Marketing" });
		await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "Sales" });

		// T3: Scores
		await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 1L, ["T1Id"] = 1L, ["Val"] = 90L });
		await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 2L, ["T1Id"] = 1L, ["Val"] = 85L });
		await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 3L, ["T1Id"] = 2L, ["Val"] = 75L });
		await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 4L, ["T1Id"] = 3L, ["Val"] = 95L });
	}

	// ─── INNER JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task InnerJoin_Basic()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a INNER JOIN {T2} b ON a.CatId = b.Id ORDER BY a.Id");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Category"].Should().Be("Engineering");
	}

	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task InnerJoin_NoMatch()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a INNER JOIN {T2} b ON a.CatId = b.Id AND b.Id = 999");
		rows.Should().BeEmpty();
	}

	// ─── LEFT JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task LeftJoin_Basic()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a LEFT JOIN {T2} b ON a.CatId = b.Id ORDER BY a.Id");
		rows.Should().HaveCount(4);
		rows[3]["Name"].Should().Be("Diana");
		rows[3]["Category"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task LeftJoin_NoMatchInRight()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a LEFT JOIN {T2} b ON a.CatId = b.Id WHERE b.Category IS NULL");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Diana");
	}

	// ─── RIGHT JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task RightJoin_Basic()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a RIGHT JOIN {T2} b ON a.CatId = b.Id ORDER BY b.Id");
		// Sales has no match
		rows.Should().HaveCount(4);
		var salesRow = rows.First(r => (string?)r["Category"] == "Sales");
		salesRow["Name"].Should().BeNull();
	}

	// ─── FULL OUTER JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task FullOuterJoin()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a FULL OUTER JOIN {T2} b ON a.CatId = b.Id ORDER BY a.Id, b.Id");
		// Diana has NULL CatId, Sales has no match
		rows.Should().HaveCount(5);
	}

	// ─── CROSS JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task CrossJoin()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a CROSS JOIN {T2} b");
		rows.Should().HaveCount(12); // 4 * 3
	}

	// ─── Self JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task SelfJoin()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name AS Name1, b.Name AS Name2 FROM {T1} a JOIN {T1} b ON a.CatId = b.CatId AND a.Id < b.Id ORDER BY a.Id");
		rows.Should().HaveCountGreaterOrEqualTo(1);
		rows[0]["Name1"].Should().Be("Alice");
		rows[0]["Name2"].Should().Be("Charlie");
	}

	// ─── Multi-table JOIN ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task ThreeTableJoin()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, b.Category, c.Val FROM {T1} a " +
			$"JOIN {T2} b ON a.CatId = b.Id " +
			$"JOIN {T3} c ON a.Id = c.T1Id " +
			$"ORDER BY a.Id, c.Id");
		rows.Should().HaveCount(4);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Val"].Should().Be(90L);
	}

	// ─── JOIN with WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_WithWhere()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, b.Category FROM {T1} a " +
			$"JOIN {T2} b ON a.CatId = b.Id WHERE b.Category = 'Engineering'");
		rows.Should().HaveCount(2);
	}

	// ─── JOIN with aggregates ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_WithAggregate()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, COUNT(c.Id) AS ScoreCount, SUM(c.Val) AS TotalScore FROM {T1} a " +
			$"LEFT JOIN {T3} c ON a.Id = c.T1Id GROUP BY a.Name ORDER BY a.Name");
		rows.Should().HaveCount(4);
		var alice = rows.First(r => (string)r["Name"]! == "Alice");
		alice["ScoreCount"].Should().Be(2L);
		alice["TotalScore"].Should().Be(175L);
	}

	// ─── JOIN with ORDER BY and LIMIT ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_OrderBy_Limit()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, c.Val FROM {T1} a " +
			$"JOIN {T3} c ON a.Id = c.T1Id ORDER BY c.Val DESC LIMIT 3");
		rows.Should().HaveCount(3);
		((long)rows[0]["Val"]!).Should().Be(95L);
	}

	// ─── JOIN with subquery ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_WithSubquery()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name FROM {T1} a " +
			$"WHERE a.CatId IN (SELECT Id FROM {T2} WHERE Category = 'Engineering')");
		rows.Should().HaveCount(2);
	}

	// ─── JOIN with DISTINCT ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_Distinct()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT DISTINCT b.Category FROM {T1} a JOIN {T2} b ON a.CatId = b.Id ORDER BY b.Category");
		rows.Should().HaveCount(2); // Engineering, Marketing
	}

	// ─── Comma join (implicit CROSS JOIN) ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task CommaJoin()
	{
		await EnsureTables();
		var rows = await QueryAsync($"SELECT a.Name, b.Category FROM {T1} a, {T2} b WHERE a.CatId = b.Id ORDER BY a.Id");
		rows.Should().HaveCount(3);
	}

	// ─── JOIN with CASE ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_WithCase()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, CASE WHEN b.Category IS NULL THEN 'None' ELSE b.Category END AS Cat " +
			$"FROM {T1} a LEFT JOIN {T2} b ON a.CatId = b.Id ORDER BY a.Id");
		rows[3]["Cat"].Should().Be("None");
	}

	// ─── JOIN + NULL handling ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task LeftJoin_Aggregate_NullHandling()
	{
		await EnsureTables();
		var rows = await QueryAsync(
			$"SELECT a.Name, COALESCE(SUM(c.Val), 0) AS Total FROM {T1} a " +
			$"LEFT JOIN {T3} c ON a.Id = c.T1Id GROUP BY a.Name ORDER BY a.Name");
		var diana = rows.First(r => (string)r["Name"]! == "Diana");
		diana["Total"].Should().Be(0L);
	}

	// ─── USING clause ───
	[Fact]
	[Trait(TestTraits.Category, "JoinExhaustive")]
	public async Task Join_Using()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS JuT1 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)",
			"CREATE TABLE IF NOT EXISTS JuT2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertOrUpdateAsync("JuT1", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a" });
		await InsertOrUpdateAsync("JuT2", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 42L });
		var rows = await QueryAsync("SELECT Name, Val FROM JuT1 JOIN JuT2 USING (Id)");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("a");
		rows[0]["Val"].Should().Be(42L);
	}
}
