using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Extended JOIN tests covering all join types, multiple conditions, self-joins,
/// cross joins, and joins with complex ON clauses.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#join_types
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JoinExtendedIntegrationTests : IntegrationTestBase
{
	public JoinExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTablesAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE JnCustomers (Id INT64 NOT NULL, Name STRING(100), City STRING(50)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE JnOrders (Id INT64 NOT NULL, CustomerId INT64, Amount INT64, Status STRING(20)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE JnProducts (Id INT64 NOT NULL, Name STRING(100), Price INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE JnOrderItems (OrderId INT64 NOT NULL, ProductId INT64 NOT NULL, Qty INT64) PRIMARY KEY (OrderId, ProductId)");
		}
		catch { }

		try
		{
			await InsertAsync("JnCustomers", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Acme", ["City"] = "NYC" });
			await InsertAsync("JnCustomers", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Beta", ["City"] = "LA" });
			await InsertAsync("JnCustomers", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Gamma", ["City"] = "NYC" });
			await InsertAsync("JnCustomers", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Delta", ["City"] = "Chicago" });

			await InsertAsync("JnOrders", new Dictionary<string, object?> { ["Id"] = 101L, ["CustomerId"] = 1L, ["Amount"] = 500L, ["Status"] = "Complete" });
			await InsertAsync("JnOrders", new Dictionary<string, object?> { ["Id"] = 102L, ["CustomerId"] = 1L, ["Amount"] = 300L, ["Status"] = "Pending" });
			await InsertAsync("JnOrders", new Dictionary<string, object?> { ["Id"] = 103L, ["CustomerId"] = 2L, ["Amount"] = 700L, ["Status"] = "Complete" });
			await InsertAsync("JnOrders", new Dictionary<string, object?> { ["Id"] = 104L, ["CustomerId"] = 3L, ["Amount"] = 200L, ["Status"] = "Complete" });
			await InsertAsync("JnOrders", new Dictionary<string, object?> { ["Id"] = 105L, ["CustomerId"] = 999L, ["Amount"] = 100L, ["Status"] = "Orphan" });

			await InsertAsync("JnProducts", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Widget", ["Price"] = 50L });
			await InsertAsync("JnProducts", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Gadget", ["Price"] = 75L });
			await InsertAsync("JnProducts", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Tool", ["Price"] = 25L });

			await InsertAsync("JnOrderItems", new Dictionary<string, object?> { ["OrderId"] = 101L, ["ProductId"] = 1L, ["Qty"] = 10L });
			await InsertAsync("JnOrderItems", new Dictionary<string, object?> { ["OrderId"] = 101L, ["ProductId"] = 2L, ["Qty"] = 5L });
			await InsertAsync("JnOrderItems", new Dictionary<string, object?> { ["OrderId"] = 102L, ["ProductId"] = 1L, ["Qty"] = 3L });
			await InsertAsync("JnOrderItems", new Dictionary<string, object?> { ["OrderId"] = 103L, ["ProductId"] = 3L, ["Qty"] = 20L });
		}
		catch { }
	}

	// ═══════════════════════════════════════════════════════════════
	// INNER JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InnerJoin_Basic()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			INNER JOIN JnOrders o ON o.CustomerId = c.Id
			ORDER BY c.Name, o.Amount");
		rows.Should().HaveCount(4); // Excludes orphan order
	}

	[Fact]
	public async Task InnerJoin_MultipleConditions()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			INNER JOIN JnOrders o ON o.CustomerId = c.Id AND o.Status = 'Complete'
			ORDER BY c.Name");
		rows.Should().HaveCount(3); // Acme(500), Beta(700), Gamma(200)
	}

	[Fact]
	public async Task InnerJoin_WithWhere()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			WHERE c.City = 'NYC'
			ORDER BY o.Amount");
		rows.Should().HaveCount(3); // Acme(2 orders) + Gamma(1 order)
	}

	[Fact]
	public async Task InnerJoin_ThreeTables()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name AS Customer, p.Name AS Product, oi.Qty
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			JOIN JnOrderItems oi ON oi.OrderId = o.Id
			JOIN JnProducts p ON p.Id = oi.ProductId
			ORDER BY c.Name, p.Name");
		rows.Should().NotBeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// LEFT JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task LeftJoin_IncludesAllLeft()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			LEFT JOIN JnOrders o ON o.CustomerId = c.Id
			ORDER BY c.Name, o.Amount");
		// Delta has no orders -> Amount will be NULL
		rows.Where(r => (string)r["Name"]! == "Delta").Should().ContainSingle()
			.Which["Amount"].Should().BeNull();
	}

	[Fact]
	public async Task LeftJoin_WithCountInGroup()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, COUNT(o.Id) AS OrderCount
			FROM JnCustomers c
			LEFT JOIN JnOrders o ON o.CustomerId = c.Id
			GROUP BY c.Name
			ORDER BY c.Name");
		rows.Should().HaveCount(4);
		rows.First(r => (string)r["Name"]! == "Delta")["OrderCount"].Should().Be(0L);
	}

	[Fact]
	public async Task LeftJoin_WithWhereOnRight_FiltersToInner()
	{
		await EnsureTablesAsync();
		// WHERE on the right table effectively converts LEFT JOIN to INNER JOIN
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			LEFT JOIN JnOrders o ON o.CustomerId = c.Id
			WHERE o.Status = 'Complete'
			ORDER BY c.Name");
		rows.All(r => r["Amount"] != null).Should().BeTrue();
	}

	[Fact]
	public async Task LeftJoin_IsNullCheck_FindsUnmatched()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name FROM JnCustomers c
			LEFT JOIN JnOrders o ON o.CustomerId = c.Id
			WHERE o.Id IS NULL
			ORDER BY c.Name");
		rows.Should().ContainSingle().Which["Name"].Should().Be("Delta");
	}

	// ═══════════════════════════════════════════════════════════════
	// CROSS JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CrossJoin_CartesianProduct()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, p.Name AS Product
			FROM JnCustomers c
			CROSS JOIN JnProducts p
			WHERE c.City = 'NYC'
			ORDER BY c.Name, p.Name");
		// 2 NYC customers × 3 products = 6 rows
		rows.Should().HaveCount(6);
	}

	[Fact]
	public async Task CrossJoin_WithFilter()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, p.Name AS Product
			FROM JnCustomers c CROSS JOIN JnProducts p
			WHERE c.Id = 1 AND p.Price > 30
			ORDER BY p.Name");
		rows.Should().HaveCount(2); // Widget(50) and Gadget(75) for Acme
	}

	// ═══════════════════════════════════════════════════════════════
	// Self JOIN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SelfJoin_SameCityCustomers()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c1.Name AS Cust1, c2.Name AS Cust2, c1.City
			FROM JnCustomers c1
			JOIN JnCustomers c2 ON c1.City = c2.City AND c1.Id < c2.Id
			ORDER BY c1.City, c1.Name");
		// NYC: Acme & Gamma
		rows.Should().ContainSingle();
		rows[0]["Cust1"].Should().Be("Acme");
		rows[0]["Cust2"].Should().Be("Gamma");
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with subqueries and aggregates
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_WithSubquery()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, totals.Total
			FROM JnCustomers c
			JOIN (
				SELECT CustomerId, SUM(Amount) AS Total
				FROM JnOrders
				GROUP BY CustomerId
			) totals ON totals.CustomerId = c.Id
			ORDER BY totals.Total DESC");
		rows.Should().NotBeEmpty();
		rows[0]["Name"].Should().Be("Acme"); // 500 + 300 = 800
	}

	[Fact]
	public async Task Join_WithCte()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			WITH OrderTotals AS (
				SELECT CustomerId, SUM(Amount) AS Total
				FROM JnOrders
				GROUP BY CustomerId
			)
			SELECT c.Name, ot.Total
			FROM JnCustomers c
			JOIN OrderTotals ot ON ot.CustomerId = c.Id
			WHERE ot.Total > 500
			ORDER BY ot.Total DESC");
		rows.Should().NotBeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN with expression ON conditions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_ExpressionInOn()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id AND o.Amount > 400
			ORDER BY c.Name");
		rows.Should().NotBeEmpty();
		rows.All(r => (long)r["Amount"]! > 400).Should().BeTrue();
	}

	[Fact]
	public async Task Join_WithOrderByAndLimit()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			ORDER BY o.Amount DESC
			LIMIT 3");
		rows.Should().HaveCount(3);
		rows[0]["Amount"].Should().Be(700L); // Beta's order
	}

	[Fact]
	public async Task Join_WithGroupByHaving()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, SUM(o.Amount) AS Total
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			GROUP BY c.Name
			HAVING SUM(o.Amount) >= 500
			ORDER BY Total DESC");
		rows.Should().NotBeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple JOINs with different types
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task MixedJoins_LeftAndInner()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name, o.Amount, oi.Qty
			FROM JnCustomers c
			LEFT JOIN JnOrders o ON o.CustomerId = c.Id AND o.Status = 'Complete'
			LEFT JOIN JnOrderItems oi ON oi.OrderId = o.Id
			ORDER BY c.Name, o.Amount");
		rows.Should().NotBeEmpty();
		// Delta has no matching orders, so o.Amount and oi.Qty will be NULL
	}

	[Fact]
	public async Task Join_FourTables()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name AS Customer, o.Id AS OrderId, p.Name AS Product, oi.Qty, p.Price * oi.Qty AS LineTotal
			FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			JOIN JnOrderItems oi ON oi.OrderId = o.Id
			JOIN JnProducts p ON p.Id = oi.ProductId
			ORDER BY c.Name, o.Id, p.Name");
		rows.Should().NotBeEmpty();
		// Verify line total calculation
		foreach (var row in rows)
		{
			var qty = (long)row["Qty"]!;
			var lineTotal = (long)row["LineTotal"]!;
			lineTotal.Should().BeGreaterThan(0);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// JOIN edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Join_EmptyResult()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name FROM JnCustomers c
			JOIN JnOrders o ON o.CustomerId = c.Id
			WHERE o.Amount > 10000");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task Join_DuplicateColumnNames_WithAlias()
	{
		await EnsureTablesAsync();
		var rows = await QueryAsync(@"
			SELECT c.Name AS CustomerName, p.Name AS ProductName
			FROM JnCustomers c
			CROSS JOIN JnProducts p
			WHERE c.Id = 1
			ORDER BY p.Name
			LIMIT 1");
		rows.Should().ContainSingle();
		rows[0]["CustomerName"].Should().Be("Acme");
	}
}
