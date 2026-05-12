using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Tests for JOIN operations: INNER, LEFT, RIGHT, FULL, CROSS, self-joins, multi-table joins.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#join_types
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JoinCoreIntegrationTests : IntegrationTestBase
{
	public JoinCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task SetupTables(string prefix)
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE {prefix}_A (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)",
			$"CREATE TABLE {prefix}_B (Id INT64 NOT NULL, AId INT64, Val STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync($"{prefix}_A",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a1" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "a2" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "a3" });
		await InsertAsync($"{prefix}_B",
			new Dictionary<string, object?> { ["Id"] = 1L, ["AId"] = 1L, ["Val"] = "b1" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["AId"] = 1L, ["Val"] = "b2" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["AId"] = 2L, ["Val"] = "b3" },
			new Dictionary<string, object?> { ["Id"] = 4L, ["AId"] = 99L, ["Val"] = "b4" });
	}

	// ─── INNER JOIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#inner_join

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task InnerJoin_MatchingRows()
	{
		var p = "J1";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a INNER JOIN {p}_B b ON a.Id = b.AId
			ORDER BY a.Val, b.Val");

		rows.Should().HaveCount(3);
		rows[0]["aVal"].Should().Be("a1");
		rows[0]["bVal"].Should().Be("b1");
		rows[1]["aVal"].Should().Be("a1");
		rows[1]["bVal"].Should().Be("b2");
		rows[2]["aVal"].Should().Be("a2");
		rows[2]["bVal"].Should().Be("b3");
	}

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task InnerJoin_ImplicitSyntax()
	{
		var p = "J2";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a JOIN {p}_B b ON a.Id = b.AId
			ORDER BY a.Val, b.Val");

		rows.Should().HaveCount(3);
	}

	// ─── LEFT JOIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#left_outer_join

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task LeftJoin_IncludesUnmatchedLeft()
	{
		var p = "J3";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a LEFT JOIN {p}_B b ON a.Id = b.AId
			ORDER BY a.Val, b.Val");

		rows.Should().HaveCount(4); // a1→b1,b2, a2→b3, a3→NULL
		var a3Row = rows.First(r => (string)r["aVal"]! == "a3");
		a3Row["bVal"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task LeftJoin_NoRightMatch_AllNull()
	{
		var p = "J4";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_L (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			$"CREATE TABLE {p}_R (Id INT64 NOT NULL, LId INT64) PRIMARY KEY (Id)");
		await InsertAsync($"{p}_L", new Dictionary<string, object?> { ["Id"] = 1L });
		// R has no rows

		var rows = await QueryAsync($@"
			SELECT l.Id, r.Id AS RId
			FROM {p}_L l LEFT JOIN {p}_R r ON l.Id = r.LId");

		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
		rows[0]["RId"].Should().BeNull();
	}

	// ─── RIGHT JOIN ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task RightJoin_IncludesUnmatchedRight()
	{
		var p = "J5";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a RIGHT JOIN {p}_B b ON a.Id = b.AId
			ORDER BY b.Val");

		rows.Should().HaveCount(4); // b1→a1, b2→a1, b3→a2, b4→NULL
		var b4Row = rows.First(r => (string)r["bVal"]! == "b4");
		b4Row["aVal"].Should().BeNull();
	}

	// ─── FULL OUTER JOIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#full_outer_join

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task FullOuterJoin_IncludesAll()
	{
		var p = "J6";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a FULL OUTER JOIN {p}_B b ON a.Id = b.AId
			ORDER BY COALESCE(a.Val, ''), COALESCE(b.Val, '')");

		rows.Should().HaveCount(5); // 3 matches + 1 unmatched left + 1 unmatched right
	}

	// ─── CROSS JOIN ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#cross_join

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task CrossJoin_CartesianProduct()
	{
		var p = "J7";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_X (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			$"CREATE TABLE {p}_Y (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync($"{p}_X", new Dictionary<string, object?> { ["Id"] = 1L }, new Dictionary<string, object?> { ["Id"] = 2L });
		await InsertAsync($"{p}_Y", new Dictionary<string, object?> { ["Id"] = 10L }, new Dictionary<string, object?> { ["Id"] = 20L }, new Dictionary<string, object?> { ["Id"] = 30L });

		var rows = await QueryAsync($"SELECT x.Id AS XId, y.Id AS YId FROM {p}_X x CROSS JOIN {p}_Y y ORDER BY x.Id, y.Id");
		rows.Should().HaveCount(6); // 2 * 3
	}

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task CrossJoin_CommaSyntax()
	{
		var p = "J8";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_X (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			$"CREATE TABLE {p}_Y (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync($"{p}_X", new Dictionary<string, object?> { ["Id"] = 1L }, new Dictionary<string, object?> { ["Id"] = 2L });
		await InsertAsync($"{p}_Y", new Dictionary<string, object?> { ["Id"] = 10L }, new Dictionary<string, object?> { ["Id"] = 20L });

		var rows = await QueryAsync($"SELECT x.Id AS XId, y.Id AS YId FROM {p}_X x, {p}_Y y ORDER BY x.Id, y.Id");
		rows.Should().HaveCount(4);
	}

	// ─── Self-join ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task SelfJoin_FindPairs()
	{
		var p = "J9";
		await ExecuteDdlAsync($"CREATE TABLE {p}_T (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync($"{p}_T",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 20L });

		var rows = await QueryAsync($@"
			SELECT a.Id AS AId, b.Id AS BId
			FROM {p}_T a JOIN {p}_T b ON a.Val = b.Val AND a.Id < b.Id
			ORDER BY a.Id, b.Id");

		rows.Should().HaveCount(1); // (1,2) share Val=10
		rows[0]["AId"].Should().Be(1L);
		rows[0]["BId"].Should().Be(2L);
	}

	// ─── Multi-table join ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task ThreeTableJoin()
	{
		var p = "J10";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_Orders (OrderId INT64 NOT NULL, CustomerId INT64) PRIMARY KEY (OrderId)",
			$"CREATE TABLE {p}_Customers (CustomerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (CustomerId)",
			$"CREATE TABLE {p}_Items (ItemId INT64 NOT NULL, OrderId INT64, Product STRING(MAX)) PRIMARY KEY (ItemId)");
		await InsertAsync($"{p}_Customers", new Dictionary<string, object?> { ["CustomerId"] = 1L, ["Name"] = "Alice" });
		await InsertAsync($"{p}_Orders", new Dictionary<string, object?> { ["OrderId"] = 100L, ["CustomerId"] = 1L });
		await InsertAsync($"{p}_Items",
			new Dictionary<string, object?> { ["ItemId"] = 1L, ["OrderId"] = 100L, ["Product"] = "Widget" },
			new Dictionary<string, object?> { ["ItemId"] = 2L, ["OrderId"] = 100L, ["Product"] = "Gadget" });

		var rows = await QueryAsync($@"
			SELECT c.Name, o.OrderId, i.Product
			FROM {p}_Customers c
			JOIN {p}_Orders o ON c.CustomerId = o.CustomerId
			JOIN {p}_Items i ON o.OrderId = i.OrderId
			ORDER BY i.Product");

		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Product"].Should().Be("Gadget");
	}

	// ─── JOIN with WHERE ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task JoinWithWhere_FiltersAfterJoin()
	{
		var p = "J11";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val AS aVal, b.Val AS bVal
			FROM {p}_A a JOIN {p}_B b ON a.Id = b.AId
			WHERE a.Id = 1
			ORDER BY b.Val");

		rows.Should().HaveCount(2);
		rows[0]["bVal"].Should().Be("b1");
		rows[1]["bVal"].Should().Be("b2");
	}

	// ─── JOIN with aggregation ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task JoinWithGroupBy()
	{
		var p = "J12";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT a.Val, COUNT(b.Id) AS BCount
			FROM {p}_A a LEFT JOIN {p}_B b ON a.Id = b.AId
			GROUP BY a.Val
			ORDER BY a.Val");

		rows.Should().HaveCount(3);
		rows[0]["Val"].Should().Be("a1");
		rows[0]["BCount"].Should().Be(2L);
		rows[1]["Val"].Should().Be("a2");
		rows[1]["BCount"].Should().Be(1L);
		rows[2]["Val"].Should().Be("a3");
		rows[2]["BCount"].Should().Be(0L);
	}

	// ─── JOIN empty table ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task InnerJoin_EmptyRight_ReturnsEmpty()
	{
		var p = "J13";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_L (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			$"CREATE TABLE {p}_R (Id INT64 NOT NULL, LId INT64) PRIMARY KEY (Id)");
		await InsertAsync($"{p}_L", new Dictionary<string, object?> { ["Id"] = 1L });

		var rows = await QueryAsync($"SELECT l.Id FROM {p}_L l JOIN {p}_R r ON l.Id = r.LId");
		rows.Should().BeEmpty();
	}

	// ─── Multiple conditions in ON ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task Join_MultipleOnConditions()
	{
		var p = "J14";
		await ExecuteDdlAsync(
			$"CREATE TABLE {p}_A (X INT64 NOT NULL, Y INT64 NOT NULL) PRIMARY KEY (X, Y)",
			$"CREATE TABLE {p}_B (X INT64 NOT NULL, Y INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (X, Y)");
		await InsertAsync($"{p}_A", new Dictionary<string, object?> { ["X"] = 1L, ["Y"] = 2L });
		await InsertAsync($"{p}_A", new Dictionary<string, object?> { ["X"] = 1L, ["Y"] = 3L });
		await InsertAsync($"{p}_B", new Dictionary<string, object?> { ["X"] = 1L, ["Y"] = 2L, ["Val"] = "match" });

		var rows = await QueryAsync($@"
			SELECT b.Val FROM {p}_A a JOIN {p}_B b ON a.X = b.X AND a.Y = b.Y");
		rows.Should().HaveCount(1);
		rows[0]["Val"].Should().Be("match");
	}

	// ─── JOIN with ORDER BY + LIMIT ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task Join_OrderByLimit()
	{
		var p = "J15";
		await SetupTables(p);

		var rows = await QueryAsync($@"
			SELECT b.Val AS bVal
			FROM {p}_A a JOIN {p}_B b ON a.Id = b.AId
			ORDER BY b.Val LIMIT 2");

		rows.Should().HaveCount(2);
		rows[0]["bVal"].Should().Be("b1");
		rows[1]["bVal"].Should().Be("b2");
	}
}
