using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense tests for JOIN patterns, multi-table queries, and relational patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#join_types
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JoinDenseIntegrationTests : IntegrationTestBase
{
	public JoinDenseIntegrationTests(EmulatorSession session) : base(session) { }

	private string T() => $"T_{Guid.NewGuid():N}";

	private async Task SeedPair(string t1, string t2)
	{
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, T1Id INT64, Val STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 1L, ["T1Id"] = 1L, ["Val"] = "a1" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 2L, ["T1Id"] = 1L, ["Val"] = "a2" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 3L, ["T1Id"] = 2L, ["Val"] = "b1" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 4L, ["T1Id"] = 99L, ["Val"] = "orphan" });
	}

	// ═══════════════════════════════════════════════════════════════
	// INNER JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InnerJoin_MatchingRows()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a INNER JOIN {t2} b ON a.Id = b.T1Id ORDER BY b.Val");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Alice"); rows[0]["Val"].Should().Be("a1");
		rows[1]["Name"].Should().Be("Alice"); rows[1]["Val"].Should().Be("a2");
		rows[2]["Name"].Should().Be("Bob");   rows[2]["Val"].Should().Be("b1");
	}

	[Fact]
	public async Task InnerJoin_NoMatch_Empty()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Ref INT64) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 1L, ["Ref"] = 99L });
		var rows = await QueryAsync($"SELECT * FROM {t1} a JOIN {t2} b ON a.Id = b.Ref");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task InnerJoin_SelfJoin()
	{
		var t = T();
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Parent INT64) PRIMARY KEY (Id)");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Parent"] = DBNull.Value });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Parent"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Parent"] = 1L });
		var rows = await QueryAsync($"SELECT c.Id AS ChildId, p.Id AS ParentId FROM {t} c JOIN {t} p ON c.Parent = p.Id ORDER BY c.Id");
		rows.Should().HaveCount(2);
		rows[0]["ChildId"].Should().Be(2L);
		rows[1]["ChildId"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// LEFT JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LeftJoin_PreservesLeftRows()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a LEFT JOIN {t2} b ON a.Id = b.T1Id ORDER BY a.Name, b.Val");
		rows.Should().HaveCount(4); // Alice×2, Bob×1, Charlie×1(null)
		var charlie = rows.Single(r => (string)r["Name"]! == "Charlie");
		charlie["Val"].Should().BeNull();
	}

	[Fact]
	public async Task LeftJoin_AllNulls()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Ref INT64) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L });
		var rows = await QueryAsync($"SELECT a.Id, b.Ref FROM {t1} a LEFT JOIN {t2} b ON a.Id = b.Ref");
		rows.Should().ContainSingle();
		rows[0]["Ref"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// RIGHT JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task RightJoin_PreservesRightRows()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a RIGHT JOIN {t2} b ON a.Id = b.T1Id ORDER BY b.Val");
		rows.Should().HaveCount(4); // a1+Alice, a2+Alice, b1+Bob, orphan+null
		var orphan = rows.Single(r => (string)r["Val"]! == "orphan");
		orphan["Name"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// FULL OUTER JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task FullOuterJoin_AllRows()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a FULL OUTER JOIN {t2} b ON a.Id = b.T1Id ORDER BY b.Val");
		// Alice×2, Bob×1, Charlie(null), orphan(null name)
		rows.Should().HaveCount(5);
	}

	// ═══════════════════════════════════════════════════════════════
	// CROSS JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CrossJoin_CartesianProduct()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		for (int i = 1; i <= 3; i++) await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = (long)i });
		for (int i = 1; i <= 4; i++) await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = (long)i });
		var rows = await QueryAsync($"SELECT * FROM {t1} a CROSS JOIN {t2} b");
		rows.Should().HaveCount(12);
	}

	[Fact]
	public async Task CrossJoin_EmptyTable()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L });
		var rows = await QueryAsync($"SELECT * FROM {t1} a CROSS JOIN {t2} b");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Multi-table JOINs
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ThreeTableJoin()
	{
		var t1 = T(); var t2 = T(); var t3 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, T1Id INT64, Cat STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t3} (Id INT64 NOT NULL, T2Id INT64, Detail STRING(MAX)) PRIMARY KEY (Id)");

		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 1L, ["T1Id"] = 1L, ["Cat"] = "X" });
		await InsertAsync(t3, new Dictionary<string, object?> { ["Id"] = 1L, ["T2Id"] = 1L, ["Detail"] = "d1" });

		var rows = await QueryAsync($@"
			SELECT a.Name, b.Cat, c.Detail 
			FROM {t1} a 
			JOIN {t2} b ON a.Id = b.T1Id 
			JOIN {t3} c ON b.Id = c.T2Id");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Cat"].Should().Be("X");
		rows[0]["Detail"].Should().Be("d1");
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with WHERE, ORDER BY, LIMIT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithWhere()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id WHERE a.Name = 'Alice' ORDER BY b.Val");
		rows.Should().HaveCount(2);
		rows.All(r => (string)r["Name"]! == "Alice").Should().BeTrue();
	}

	[Fact]
	public async Task Join_WithOrderBy()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, b.Val FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id ORDER BY b.Val DESC");
		rows.Should().HaveCount(3);
		rows[0]["Val"].Should().Be("b1");
	}

	[Fact]
	public async Task Join_WithLimit()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id LIMIT 2");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with aggregates
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithCount()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, COUNT(*) AS Cnt FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id GROUP BY a.Name ORDER BY a.Name");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice"); rows[0]["Cnt"].Should().Be(2L);
		rows[1]["Name"].Should().Be("Bob");   rows[1]["Cnt"].Should().Be(1L);
	}

	[Fact]
	public async Task LeftJoin_WithCount_IncludingZeros()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT a.Name, COUNT(b.Id) AS Cnt FROM {t1} a LEFT JOIN {t2} b ON a.Id = b.T1Id GROUP BY a.Name ORDER BY a.Name");
		rows.Should().HaveCount(3);
		var charlie = rows.Single(r => (string)r["Name"]! == "Charlie");
		charlie["Cnt"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_Distinct()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT DISTINCT a.Name FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id ORDER BY a.Name");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[1]["Name"].Should().Be("Bob");
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithSubquery()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($@"
			SELECT a.Name 
			FROM {t1} a 
			WHERE a.Id IN (SELECT DISTINCT T1Id FROM {t2}) 
			ORDER BY a.Name");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[1]["Name"].Should().Be("Bob");
	}

	[Fact]
	public async Task Join_WithExists()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($@"
			SELECT a.Name 
			FROM {t1} a 
			WHERE EXISTS (SELECT 1 FROM {t2} b WHERE b.T1Id = a.Id)
			ORDER BY a.Name");
		rows.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN on multiple columns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_MultiColumnKey()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (A INT64 NOT NULL, B INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (A, B)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (K INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (K)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["A"] = 1L, ["B"] = 10L, ["V"] = "hit" });
		await InsertAsync(t1, new Dictionary<string, object?> { ["A"] = 1L, ["B"] = 20L, ["V"] = "miss" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = 1L, ["B"] = 10L });
		var rows = await QueryAsync($"SELECT t1.V FROM {t1} t1 JOIN {t2} t2 ON t1.A = t2.A AND t1.B = t2.B");
		rows.Should().ContainSingle().Which["V"].Should().Be("hit");
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with string functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithFunctionInSelect()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT UPPER(a.Name) AS UName FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id WHERE b.Val = 'a1'");
		rows.Should().ContainSingle().Which["UName"].Should().Be("ALICE");
	}

	[Fact]
	public async Task Join_WithConcatInSelect()
	{
		var t1 = T(); var t2 = T();
		await SeedPair(t1, t2);
		var rows = await QueryAsync($"SELECT CONCAT(a.Name, ':', b.Val) AS Combined FROM {t1} a JOIN {t2} b ON a.Id = b.T1Id ORDER BY b.Val LIMIT 1");
		rows[0]["Combined"].Should().Be("Alice:a1");
	}

	// ═══════════════════════════════════════════════════════════════
	// USING clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_UsingSyntax()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Detail STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync(t2, new Dictionary<string, object?> { ["Id"] = 1L, ["Detail"] = "stuff" });
		var rows = await QueryAsync($"SELECT a.Name, b.Detail FROM {t1} a JOIN {t2} b USING (Id)");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Detail"].Should().Be("stuff");
	}

	// ═══════════════════════════════════════════════════════════════
	// Empty table joins
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InnerJoin_BothEmpty()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Ref INT64) PRIMARY KEY (Id)");
		var rows = await QueryAsync($"SELECT * FROM {t1} a JOIN {t2} b ON a.Id = b.Ref");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task LeftJoin_RightEmpty()
	{
		var t1 = T(); var t2 = T();
		await ExecuteDdlAsync($"CREATE TABLE {t1} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (Id INT64 NOT NULL, Ref INT64) PRIMARY KEY (Id)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["Id"] = 1L });
		var rows = await QueryAsync($"SELECT a.Id, b.Ref FROM {t1} a LEFT JOIN {t2} b ON a.Id = b.Ref");
		rows.Should().ContainSingle();
		rows[0]["Ref"].Should().BeNull();
	}
}
