using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense window function, ORDER BY, LIMIT/OFFSET, and DISTINCT tests.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowAndOrderIntegrationTests : IntegrationTestBase
{
	public WindowAndOrderIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureDataTable()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE WinOrd (
				Id INT64 NOT NULL,
				Dept STRING(10),
				Name STRING(50),
				Salary INT64,
				HireDate DATE
			) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task SeedData()
	{
		await EnsureDataTable();
		using var conn = Fixture.CreateConnection();
		using var delCmd = conn.CreateDmlCommand("DELETE FROM WinOrd WHERE TRUE");
		await delCmd.ExecuteNonQueryAsync();
		await InsertAsync("WinOrd",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Dept"] = "Eng", ["Name"] = "Alice", ["Salary"] = 100000L, ["HireDate"] = new DateTime(2020, 1, 15) },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Dept"] = "Eng", ["Name"] = "Bob", ["Salary"] = 90000L, ["HireDate"] = new DateTime(2019, 6, 1) },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Dept"] = "Eng", ["Name"] = "Charlie", ["Salary"] = 110000L, ["HireDate"] = new DateTime(2021, 3, 10) },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Dept"] = "Sales", ["Name"] = "Diana", ["Salary"] = 80000L, ["HireDate"] = new DateTime(2020, 7, 20) },
			new Dictionary<string, object?> { ["Id"] = 5L, ["Dept"] = "Sales", ["Name"] = "Eve", ["Salary"] = 85000L, ["HireDate"] = new DateTime(2018, 11, 5) },
			new Dictionary<string, object?> { ["Id"] = 6L, ["Dept"] = "Sales", ["Name"] = "Frank", ["Salary"] = 95000L, ["HireDate"] = new DateTime(2022, 2, 28) },
			new Dictionary<string, object?> { ["Id"] = 7L, ["Dept"] = "HR", ["Name"] = "Grace", ["Salary"] = 75000L, ["HireDate"] = new DateTime(2021, 8, 15) },
			new Dictionary<string, object?> { ["Id"] = 8L, ["Dept"] = "HR", ["Name"] = "Hank", ["Salary"] = 70000L, ["HireDate"] = new DateTime(2020, 4, 1) });
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ORDER BY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#order_by_clause
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task OrderBy_Ascending()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Salary ASC");
		rows[0]["Name"].Should().Be("Hank");
		rows[^1]["Name"].Should().Be("Charlie");
	}

	[Fact]
	public async Task OrderBy_Descending()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Salary DESC");
		rows[0]["Name"].Should().Be("Charlie");
		rows[^1]["Name"].Should().Be("Hank");
	}

	[Fact]
	public async Task OrderBy_MultipleColumns()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd ORDER BY Dept ASC, Salary DESC");
		rows[0]["Name"].Should().Be("Charlie"); // Eng, highest
		rows[1]["Name"].Should().Be("Alice");   // Eng, middle
		rows[2]["Name"].Should().Be("Bob");     // Eng, lowest
	}

	[Fact]
	public async Task OrderBy_NullsFirst()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinOrdNull (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinOrdNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });

		var rows = await QueryAsync("SELECT Id, Val FROM WinOrdNull ORDER BY Val ASC");
		// In Spanner, NULLs sort first by default in ASC
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task OrderBy_NullsLast_Desc()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinOrdNull2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinOrdNull2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 5L });

		var rows = await QueryAsync("SELECT Id, Val FROM WinOrdNull2 ORDER BY Val DESC");
		// NULLs sort last in DESC
		rows[^1]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task OrderBy_Expression()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name, Salary FROM WinOrd ORDER BY Salary * -1");
		rows[0]["Name"].Should().Be("Charlie"); // Highest salary * -1 = most negative
	}

	[Fact]
	public async Task OrderBy_Alias()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name, Salary AS S FROM WinOrd ORDER BY S DESC");
		rows[0]["Name"].Should().Be("Charlie");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// LIMIT and OFFSET
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#limit_and_offset_clause
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(5)]
	[InlineData(8)]
	[InlineData(100)]
	public async Task Limit_VariousSizes(int limit)
	{
		await SeedData();
		var rows = await QueryAsync($"SELECT Name FROM WinOrd ORDER BY Id LIMIT {limit}");
		rows.Should().HaveCount(Math.Min(limit, 8));
	}

	[Theory]
	[InlineData(0, 3)]
	[InlineData(1, 3)]
	[InlineData(5, 3)]
	[InlineData(7, 3)]
	[InlineData(8, 3)]
	public async Task Offset_VariousPositions(int offset, int limit)
	{
		await SeedData();
		var rows = await QueryAsync($"SELECT Id FROM WinOrd ORDER BY Id LIMIT {limit} OFFSET {offset}");
		rows.Should().HaveCount(Math.Min(limit, Math.Max(0, 8 - offset)));
		if (rows.Count > 0)
			((long)rows[0]["Id"]!).Should().Be(offset + 1);
	}

	[Fact]
	public async Task Limit_Zero()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT Name FROM WinOrd LIMIT 0");
		rows.Should().BeEmpty();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DISTINCT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_distinct
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Distinct_SingleColumn()
	{
		await SeedData();
		var rows = await QueryAsync("SELECT DISTINCT Dept FROM WinOrd ORDER BY Dept");
		rows.Should().HaveCount(3);
		rows[0]["Dept"].Should().Be("Eng");
		rows[1]["Dept"].Should().Be("HR");
		rows[2]["Dept"].Should().Be("Sales");
	}

	[Fact]
	public async Task Distinct_MultipleColumns()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinDistinct (Id INT64 NOT NULL, A STRING(10), B STRING(10)) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinDistinct",
			new Dictionary<string, object?> { ["Id"] = 1L, ["A"] = "x", ["B"] = "1" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["A"] = "x", ["B"] = "1" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["A"] = "x", ["B"] = "2" },
			new Dictionary<string, object?> { ["Id"] = 4L, ["A"] = "y", ["B"] = "1" });

		var rows = await QueryAsync("SELECT DISTINCT A, B FROM WinDistinct ORDER BY A, B");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Distinct_WithNull()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinDistNull (Id INT64 NOT NULL, Val STRING(10)) PRIMARY KEY (Id)");
		}
		catch { }
		await InsertAsync("WinDistNull",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = null },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = null });

		var rows = await QueryAsync("SELECT DISTINCT Val FROM WinDistNull ORDER BY Val");
		rows.Should().HaveCount(2); // "a" and NULL
	}

}
