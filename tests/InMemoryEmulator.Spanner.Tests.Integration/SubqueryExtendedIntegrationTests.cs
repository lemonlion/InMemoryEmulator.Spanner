using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Comprehensive tests for subqueries, CTEs, set operations, and complex query patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SubqueryExtendedIntegrationTests : IntegrationTestBase
{
	public SubqueryExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTablesAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE SubqProducts (Id INT64 NOT NULL, Name STRING(100), Price INT64, Category STRING(50)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE SubqOrders (Id INT64 NOT NULL, ProductId INT64, Qty INT64) PRIMARY KEY (Id)");
		}
		catch { }

		try
		{
			await InsertAsync("SubqProducts", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Widget", ["Price"] = 10L, ["Category"] = "A" });
			await InsertAsync("SubqProducts", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Gadget", ["Price"] = 20L, ["Category"] = "A" });
			await InsertAsync("SubqProducts", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Sprocket", ["Price"] = 30L, ["Category"] = "B" });
			await InsertAsync("SubqProducts", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Gizmo", ["Price"] = 40L, ["Category"] = "B" });
			await InsertAsync("SubqProducts", new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Doohickey", ["Price"] = 50L, ["Category"] = "C" });

			await InsertAsync("SubqOrders", new Dictionary<string, object?> { ["Id"] = 1L, ["ProductId"] = 1L, ["Qty"] = 5L });
			await InsertAsync("SubqOrders", new Dictionary<string, object?> { ["Id"] = 2L, ["ProductId"] = 1L, ["Qty"] = 3L });
			await InsertAsync("SubqOrders", new Dictionary<string, object?> { ["Id"] = 3L, ["ProductId"] = 2L, ["Qty"] = 10L });
			await InsertAsync("SubqOrders", new Dictionary<string, object?> { ["Id"] = 4L, ["ProductId"] = 3L, ["Qty"] = 2L });
			await InsertAsync("SubqOrders", new Dictionary<string, object?> { ["Id"] = 5L, ["ProductId"] = 5L, ["Qty"] = 1L });
		}
		catch { }
	}

	private async Task EnsureSetTablesAsync()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE SetA (Val INT64 NOT NULL) PRIMARY KEY (Val)");
			await ExecuteDdlAsync("CREATE TABLE SetB (Val INT64 NOT NULL) PRIMARY KEY (Val)");
		}
		catch { }

		try
		{
			await InsertAsync("SetA", new Dictionary<string, object?> { ["Val"] = 1L });
			await InsertAsync("SetA", new Dictionary<string, object?> { ["Val"] = 2L });
			await InsertAsync("SetA", new Dictionary<string, object?> { ["Val"] = 3L });
			await InsertAsync("SetA", new Dictionary<string, object?> { ["Val"] = 4L });

			await InsertAsync("SetB", new Dictionary<string, object?> { ["Val"] = 3L });
			await InsertAsync("SetB", new Dictionary<string, object?> { ["Val"] = 4L });
			await InsertAsync("SetB", new Dictionary<string, object?> { ["Val"] = 5L });
			await InsertAsync("SetB", new Dictionary<string, object?> { ["Val"] = 6L });
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// Scalar subqueries
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ScalarSubquery_InSelect()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT (SELECT MAX(Price) FROM SubqProducts)");
		result.Should().Be(50L);
	}

	[Fact]
	public async Task ScalarSubquery_InSelect_Min()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT (SELECT MIN(Price) FROM SubqProducts)");
		result.Should().Be(10L);
	}

	[Fact]
	public async Task ScalarSubquery_InWhere()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts WHERE Price > (SELECT AVG(Price) FROM SubqProducts) ORDER BY Name");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Doohickey");
		rows[1]["Name"].Should().Be("Gizmo");
	}

	[Fact]
	public async Task ScalarSubquery_EmptyReturnsNull()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT (SELECT Price FROM SubqProducts WHERE Id = 999)");
		(result == null || result == DBNull.Value).Should().BeTrue();
	}

	[Fact]
	public async Task ScalarSubquery_Count()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT (SELECT COUNT(*) FROM SubqOrders)");
		result.Should().Be(5L);
	}

	// ═══════════════════════════════════════════════════════════════
	// EXISTS subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Exists_WithRows_ReturnsTrue()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT EXISTS(SELECT 1 FROM SubqProducts WHERE Category = 'A')");
		result.Should().Be(true);
	}

	[Fact]
	public async Task Exists_NoRows_ReturnsFalse()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT EXISTS(SELECT 1 FROM SubqProducts WHERE Category = 'Z')");
		result.Should().Be(false);
	}

	[Fact]
	public async Task Exists_InWhere()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts p WHERE EXISTS(SELECT 1 FROM SubqOrders o WHERE o.ProductId = p.Id) ORDER BY Name");
		rows.Should().HaveCount(4); // Products 1, 2, 3, 5 have orders
	}

	[Fact]
	public async Task NotExists_InWhere()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts p WHERE NOT EXISTS(SELECT 1 FROM SubqOrders o WHERE o.ProductId = p.Id) ORDER BY Name");
		rows.Should().HaveCount(1); // Gizmo (Id=4) has no orders
		rows[0]["Name"].Should().Be("Gizmo");
	}

	// ═══════════════════════════════════════════════════════════════
	// IN subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#in_subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task In_Subquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts WHERE Id IN (SELECT ProductId FROM SubqOrders) ORDER BY Name");
		// Products with orders: Widget(Id=1), Gadget(Id=2), Sprocket(Id=3), Doohickey(Id=5)
		rows.Should().HaveCount(4);
	}

	[Fact]
	public async Task NotIn_Subquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts WHERE Id NOT IN (SELECT ProductId FROM SubqOrders) ORDER BY Name");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Gizmo");
	}

	// ═══════════════════════════════════════════════════════════════
	// FROM subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#from_clause_subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SubqueryInFrom()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT sq.Name, sq.Price FROM (SELECT Name, Price FROM SubqProducts WHERE Price > 20) AS sq ORDER BY sq.Price");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Sprocket");
	}

	[Fact]
	public async Task SubqueryInFrom_WithAggregate()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT sq.Category, sq.Total FROM " +
			"(SELECT Category, SUM(Price) AS Total FROM SubqProducts GROUP BY Category) AS sq " +
			"ORDER BY sq.Total DESC");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task SubqueryInFrom_Nested()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT MAX(sq.Price) FROM (SELECT Price FROM SubqProducts WHERE Category = 'B') AS sq");
		result.Should().Be(40L);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Array_Subquery_Length()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT ARRAY_LENGTH(ARRAY(SELECT Id FROM SubqProducts WHERE Category = 'A'))");
		result.Should().Be(2L);
	}

	[Fact]
	public async Task Array_Subquery_EmptyArray()
	{
		await EnsureTablesAsync();
		var result = await QueryScalarAsync(
			"SELECT ARRAY_LENGTH(ARRAY(SELECT Id FROM SubqProducts WHERE Category = 'Z'))");
		result.Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// CTEs (Common Table Expressions)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#with_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Cte_Single()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"WITH Expensive AS (SELECT Name, Price FROM SubqProducts WHERE Price >= 30) " +
			"SELECT Name FROM Expensive ORDER BY Name");
		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Doohickey");
		rows[1]["Name"].Should().Be("Gizmo");
		rows[2]["Name"].Should().Be("Sprocket");
	}

	[Fact]
	public async Task Cte_Multiple()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"WITH CatA AS (SELECT Id, Name FROM SubqProducts WHERE Category = 'A'), " +
			"OrderedProducts AS (SELECT DISTINCT ProductId FROM SubqOrders) " +
			"SELECT a.Name FROM CatA a WHERE a.Id IN (SELECT ProductId FROM OrderedProducts) ORDER BY a.Name");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public async Task Cte_WithAggregate()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"WITH CategoryTotals AS (SELECT Category, SUM(Price) AS Total FROM SubqProducts GROUP BY Category) " +
			"SELECT Category, Total FROM CategoryTotals WHERE Total > 50 ORDER BY Total DESC");
		rows.Should().HaveCount(1); // Only B(70) > 50; A(30) and C(50) are not
	}

	[Fact]
	public async Task Cte_ReferencedMultipleTimes()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"WITH HighPrice AS (SELECT MAX(Price) AS MaxP FROM SubqProducts) " +
			"SELECT Name FROM SubqProducts WHERE Price = (SELECT MaxP FROM HighPrice)");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Doohickey");
	}

	// ═══════════════════════════════════════════════════════════════
	// UNION ALL / UNION DISTINCT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnionAll_CombinesAllRows()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA UNION ALL SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(8); // 4 + 4
	}

	[Fact]
	public async Task UnionDistinct_RemovesDuplicates()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA UNION DISTINCT SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(6); // 1,2,3,4,5,6
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L, 6L });
	}

	// ═══════════════════════════════════════════════════════════════
	// EXCEPT ALL / EXCEPT DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ExceptAll_RemovesMatchingRows()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA EXCEPT ALL SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(2); // 1, 2
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 1L, 2L });
	}

	[Fact]
	public async Task ExceptDistinct_RemovesMatching()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA EXCEPT DISTINCT SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(2);
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 1L, 2L });
	}

	// ═══════════════════════════════════════════════════════════════
	// INTERSECT ALL / INTERSECT DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IntersectAll_ReturnsCommonRows()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA INTERSECT ALL SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(2); // 3, 4
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 3L, 4L });
	}

	[Fact]
	public async Task IntersectDistinct_ReturnsCommon()
	{
		await EnsureSetTablesAsync();
		var rows = await QueryAsync("SELECT Val FROM SetA INTERSECT DISTINCT SELECT Val FROM SetB ORDER BY Val");
		rows.Should().HaveCount(2);
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 3L, 4L });
	}

	// ═══════════════════════════════════════════════════════════════
	// DISTINCT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Distinct_RemovesDuplicates()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync("SELECT DISTINCT Category FROM SubqProducts ORDER BY Category");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Distinct_MultipleColumns()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync("SELECT DISTINCT Category, Price FROM SubqProducts ORDER BY Category, Price");
		rows.Should().HaveCount(5); // Each product unique
	}

	// ═══════════════════════════════════════════════════════════════
	// Complex JOIN + subquery combinations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithSubquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT p.Name, o.TotalQty FROM SubqProducts p " +
			"INNER JOIN (SELECT ProductId, SUM(Qty) AS TotalQty FROM SubqOrders GROUP BY ProductId) o " +
			"ON p.Id = o.ProductId ORDER BY p.Name");
		// Products with orders: Doohickey, Gadget, Sprocket, Widget (4 products have orders)
		rows.Should().HaveCount(4);
	}

	[Fact]
	public async Task LeftJoin_WithSubquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT p.Name, o.TotalQty FROM SubqProducts p " +
			"LEFT JOIN (SELECT ProductId, SUM(Qty) AS TotalQty FROM SubqOrders GROUP BY ProductId) o " +
			"ON p.Id = o.ProductId ORDER BY p.Name");
		rows.Should().HaveCount(5); // All products, some with NULL TotalQty
		rows.Single(r => (string)r["Name"]! == "Gizmo")["TotalQty"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// LIMIT / OFFSET with subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Subquery_WithLimit()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM (SELECT Name, Price FROM SubqProducts ORDER BY Price DESC LIMIT 3) AS sq ORDER BY Name");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Subquery_WithOffset()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM (SELECT Name, Price FROM SubqProducts ORDER BY Price LIMIT 3 OFFSET 1) AS sq ORDER BY Name");
		rows.Should().HaveCount(3);
	}

	// ═══════════════════════════════════════════════════════════════
	// Correlated subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CorrelatedSubquery_InSelect()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT p.Name, (SELECT SUM(o.Qty) FROM SubqOrders o WHERE o.ProductId = p.Id) AS TotalQty " +
			"FROM SubqProducts p ORDER BY p.Name");
		rows.Should().HaveCount(5);
		rows.Single(r => (string)r["Name"]! == "Widget")["TotalQty"].Should().Be(8L);
	}

	[Fact]
	public async Task CorrelatedSubquery_InWhere()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(
			"SELECT Name FROM SubqProducts p " +
			"WHERE (SELECT COUNT(*) FROM SubqOrders o WHERE o.ProductId = p.Id) > 1 ORDER BY Name");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Widget"); // Has 2 orders
	}
}
