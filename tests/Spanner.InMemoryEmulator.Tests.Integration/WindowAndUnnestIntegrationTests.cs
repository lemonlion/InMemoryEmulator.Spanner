using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 15: Window Functions, UNNEST, Type Constructors.
/// Tests flow through the full gRPC pipeline: SpannerConnection → gRPC → FakeSpannerService.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowAndUnnestIntegrationTests : IntegrationTestBase
{
private bool _initialized;

public WindowAndUnnestIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		if (_initialized) return;
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinEmp (Id INT64 NOT NULL, Name STRING(MAX), Dept STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Dept"] = "Eng", ["Salary"] = 100000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Dept"] = "Eng", ["Salary"] = 90000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["Dept"] = "Sales", ["Salary"] = 80000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["Dept"] = "Sales", ["Salary"] = 80000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Eve", ["Dept"] = "Eng", ["Salary"] = 110000L });

			await ExecuteDdlAsync("CREATE TABLE WinDummy (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await InsertAsync("WinDummy", new Dictionary<string, object?> { ["Id"] = 1L });
		}
		catch { /* table may already exist */ }
		_initialized = true;
	}

	private async Task<List<Dictionary<string, object?>>> QueryAsync(string sql)
	{
		await EnsureTableAsync();
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = new List<Dictionary<string, object?>>();
		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			rows.Add(row);
		}
		return rows;
	}

	// ─── ROW_NUMBER ───

	[Fact]
	public async Task RowNumber_WithOrderBy()
	{
		var rows = await QueryAsync("SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS rn FROM WinEmp ORDER BY rn");
		rows.Should().HaveCount(5);
		rows[0]["rn"].Should().Be(1L);
		rows[4]["rn"].Should().Be(5L);
	}

	[Fact]
	public async Task RowNumber_WithPartitionBy()
	{
		var rows = await QueryAsync("SELECT Name, Dept, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS rn FROM WinEmp ORDER BY Dept, rn");
		var engRows = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		engRows[0]["rn"].Should().Be(1L); // Eve
		engRows[2]["rn"].Should().Be(3L); // Bob
	}

	// ─── RANK ───

	[Fact]
	public async Task Rank_WithTies()
	{
		var rows = await QueryAsync("SELECT Name, RANK() OVER (ORDER BY Salary DESC) AS rnk FROM WinEmp ORDER BY rnk, Name");
		rows[0]["rnk"].Should().Be(1L); // Eve 110k
		rows[1]["rnk"].Should().Be(2L); // Alice 100k
		rows[2]["rnk"].Should().Be(3L); // Bob 90k
		// Charlie and Diana both 80k → rank 4
		rows[3]["rnk"].Should().Be(4L);
		rows[4]["rnk"].Should().Be(4L);
	}

	// ─── DENSE_RANK ───

	[Fact]
	public async Task DenseRank_WithTies()
	{
		var rows = await QueryAsync("SELECT Name, DENSE_RANK() OVER (ORDER BY Salary DESC) AS drnk FROM WinEmp ORDER BY drnk, Name");
		rows[0]["drnk"].Should().Be(1L);
		rows[1]["drnk"].Should().Be(2L);
		rows[2]["drnk"].Should().Be(3L);
		rows[3]["drnk"].Should().Be(4L);
		rows[4]["drnk"].Should().Be(4L);
	}

	// ─── Aggregate OVER ───

	[Fact]
	public async Task Sum_OverPartition()
	{
		var rows = await QueryAsync("SELECT Name, SUM(Salary) OVER (PARTITION BY Dept) AS dept_total FROM WinEmp WHERE Dept = 'Eng' ORDER BY Name");
		rows.Should().HaveCount(3);
		foreach (var row in rows)
		{
			Convert.ToInt64(row["dept_total"]).Should().Be(300000L);
		}
	}

	[Fact]
	public async Task Count_OverPartition()
	{
		var rows = await QueryAsync("SELECT Name, COUNT(*) OVER (PARTITION BY Dept) AS dept_count FROM WinEmp WHERE Dept = 'Sales' ORDER BY Name");
		rows.Should().HaveCount(2);
		foreach (var row in rows)
		{
			row["dept_count"].Should().Be(2L);
		}
	}

	[Fact]
	public async Task Avg_OverPartition()
	{
		var rows = await QueryAsync("SELECT Name, AVG(Salary) OVER (PARTITION BY Dept) AS avg_salary FROM WinEmp WHERE Dept = 'Eng' ORDER BY Name");
		foreach (var row in rows)
		{
			Convert.ToDouble(row["avg_salary"]).Should().Be(100000.0);
		}
	}

	// ─── LAG / LEAD ───

	[Fact]
	public async Task Lag_ReturnsNullForFirstRow()
	{
		var rows = await QueryAsync("SELECT Name, LAG(Name) OVER (ORDER BY Id) AS prev_name FROM WinEmp ORDER BY Id");
		rows[0]["prev_name"].Should().BeNull();
		rows[1]["prev_name"].Should().Be("Alice");
	}

	[Fact]
	public async Task Lead_ReturnsNullForLastRow()
	{
		var rows = await QueryAsync("SELECT Name, LEAD(Name) OVER (ORDER BY Id) AS next_name FROM WinEmp ORDER BY Id");
		rows[4]["next_name"].Should().BeNull();
		rows[0]["next_name"].Should().Be("Bob");
	}

	// ─── FIRST_VALUE / LAST_VALUE ───

	[Fact]
	public async Task FirstValue_ReturnsFirstInPartition()
	{
		var rows = await QueryAsync("SELECT Name, FIRST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS top_earner FROM WinEmp WHERE Dept = 'Eng' ORDER BY Id");
		foreach (var row in rows)
		{
			row["top_earner"].Should().Be("Eve");
		}
	}

	[Fact]
	public async Task LastValue_ReturnsLastInPartition()
	{
		var rows = await QueryAsync("SELECT Name, LAST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS lowest_earner FROM WinEmp WHERE Dept = 'Eng' ORDER BY Id");
		foreach (var row in rows)
		{
			row["lowest_earner"].Should().Be("Bob");
		}
	}

	// ─── UNNEST ───

	[Fact]
	public async Task Unnest_FlattenArrayLiteral()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(['a', 'b', 'c']) AS val");
		rows.Should().HaveCount(3);
		rows.Select(r => r["val"]?.ToString()).Should().Contain("a").And.Contain("b").And.Contain("c");
	}

	[Fact]
	public async Task Unnest_WithOffset()
	{
		var rows = await QueryAsync("SELECT val, pos FROM UNNEST(['x', 'y', 'z']) AS val WITH OFFSET AS pos ORDER BY pos");
		rows.Should().HaveCount(3);
		rows[0]["val"]?.ToString().Should().Be("x");
		rows[2]["val"]?.ToString().Should().Be("z");
	}

	[Fact]
	public async Task Unnest_EmptyArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST([]) AS val");
		rows.Should().HaveCount(0);
	}

	// ─── Array element access ───

	[Fact]
	public async Task ArrayAccess_Offset()
	{
		var rows = await QueryAsync("SELECT [10, 20, 30][OFFSET(1)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("20");
	}

	[Fact]
	public async Task ArrayAccess_Ordinal()
	{
		var rows = await QueryAsync("SELECT [10, 20, 30][ORDINAL(2)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("20");
	}

	[Fact]
	public async Task ArrayAccess_SafeOffset_OutOfBounds()
	{
		var rows = await QueryAsync("SELECT [10, 20][SAFE_OFFSET(10)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"].Should().BeNull();
	}

	// ─── STRUCT ───

	[Fact]
	public async Task Struct_Constructor()
	{
		// STRUCT constructor should parse and evaluate — result type may be serialized as JSON/string
		var rows = await QueryAsync("SELECT STRUCT(1 AS a, 'hello' AS b) AS s FROM WinDummy WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["s"].Should().NotBeNull();
	}

	// ─── Window function without PARTITION BY ───

	[Fact]
	public async Task RowNumber_WithoutPartition()
	{
		var rows = await QueryAsync("SELECT Name, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM WinEmp ORDER BY rn");
		rows.Should().HaveCount(5);
		for (var i = 0; i < 5; i++)
		{
			rows[i]["rn"].Should().Be((long)(i + 1));
		}
	}

	// ─── Multiple window functions in same query ───

	[Fact]
	public async Task MultipleWindowFunctions_InSameQuery()
	{
		var rows = await QueryAsync("SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS rn, RANK() OVER (ORDER BY Salary DESC) AS rnk FROM WinEmp ORDER BY rn");
		rows.Should().HaveCount(5);
		rows[0]["rn"].Should().Be(1L);
		rows[0]["rnk"].Should().Be(1L);
	}
}
