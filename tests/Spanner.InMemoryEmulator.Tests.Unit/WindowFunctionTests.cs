using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for window functions, UNNEST, and array/struct constructors (Phase 15).
/// </summary>
public class WindowFunctionTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Employees (Id INT64 NOT NULL, Name STRING(MAX), Dept STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Dept"] = "Eng", ["Salary"] = 100000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Dept"] = "Eng", ["Salary"] = 90000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["Dept"] = "Sales", ["Salary"] = 80000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["Dept"] = "Sales", ["Salary"] = 80000L });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Eve", ["Dept"] = "Eng", ["Salary"] = 110000L });
		return db;
	}

	// ─── ROW_NUMBER ───

	[Fact]
	public void RowNumber_WithOrderBy()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS rn FROM Employees ORDER BY rn");
		rows.Should().HaveCount(5);
		rows[0]["rn"].Should().Be(1L);
		rows[4]["rn"].Should().Be(5L);
	}

	[Fact]
	public void RowNumber_WithPartitionBy()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, Dept, ROW_NUMBER() OVER (PARTITION BY Dept ORDER BY Salary DESC) AS rn FROM Employees ORDER BY Dept, rn");
		// Eng partition: Eve(110k)=1, Alice(100k)=2, Bob(90k)=3
		// Sales partition: Charlie(80k)=1, Diana(80k)=2
		var engRows = rows.Where(r => (string)r["Dept"]! == "Eng").ToList();
		engRows[0]["rn"].Should().Be(1L);
		engRows[2]["rn"].Should().Be(3L);
	}

	// ─── RANK ───

	[Fact]
	public void Rank_WithTies()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, RANK() OVER (ORDER BY Salary DESC) AS rnk FROM Employees ORDER BY rnk");
		// Eve(110k)=1, Alice(100k)=2, Bob(90k)=3, Charlie(80k)=4, Diana(80k)=4
		rows[0]["rnk"].Should().Be(1L);
		rows[1]["rnk"].Should().Be(2L);
		rows[2]["rnk"].Should().Be(3L);
		rows[3]["rnk"].Should().Be(4L);
		rows[4]["rnk"].Should().Be(4L);
	}

	// ─── DENSE_RANK ───

	[Fact]
	public void DenseRank_WithTies()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, DENSE_RANK() OVER (ORDER BY Salary DESC) AS drnk FROM Employees ORDER BY drnk");
		// Eve(110k)=1, Alice(100k)=2, Bob(90k)=3, Charlie(80k)=4, Diana(80k)=4
		rows[0]["drnk"].Should().Be(1L);
		rows[1]["drnk"].Should().Be(2L);
		rows[2]["drnk"].Should().Be(3L);
		rows[3]["drnk"].Should().Be(4L); // tied
		rows[4]["drnk"].Should().Be(4L); // tied, but DENSE_RANK doesn't skip
	}

	// ─── SUM OVER ───

	[Fact]
	public void Sum_OverPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, Dept, SUM(Salary) OVER (PARTITION BY Dept) AS dept_total FROM Employees WHERE Dept = 'Eng' ORDER BY Name");
		rows.Should().HaveCount(3);
		// All Eng rows should have total = 100000+90000+110000 = 300000
		foreach (var row in rows)
		{
			Convert.ToInt64(row["dept_total"]).Should().Be(300000L);
		}
	}

	// ─── COUNT OVER ───

	[Fact]
	public void Count_OverPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, Dept, COUNT(*) OVER (PARTITION BY Dept) AS dept_count FROM Employees WHERE Dept = 'Sales' ORDER BY Name");
		rows.Should().HaveCount(2);
		foreach (var row in rows)
		{
			row["dept_count"].Should().Be(2L);
		}
	}

	// ─── LAG / LEAD ───

	[Fact]
	public void Lag_ReturnsNullForFirstRow()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, LAG(Name) OVER (ORDER BY Id) AS prev_name FROM Employees ORDER BY Id");
		rows[0]["prev_name"].Should().BeNull();
		rows[1]["prev_name"].Should().Be("Alice");
	}

	[Fact]
	public void Lead_ReturnsNullForLastRow()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, LEAD(Name) OVER (ORDER BY Id) AS next_name FROM Employees ORDER BY Id");
		rows[4]["next_name"].Should().BeNull();
		rows[0]["next_name"].Should().Be("Bob");
	}

	// ─── FIRST_VALUE / LAST_VALUE ───

	[Fact]
	public void FirstValue_ReturnsFirstInPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, FIRST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS top_earner FROM Employees WHERE Dept = 'Eng' ORDER BY Id");
		foreach (var row in rows)
		{
			row["top_earner"].Should().Be("Eve");
		}
	}

	[Fact]
	public void LastValue_ReturnsLastInPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, LAST_VALUE(Name) OVER (PARTITION BY Dept ORDER BY Salary DESC) AS lowest_earner FROM Employees WHERE Dept = 'Eng' ORDER BY Id");
		foreach (var row in rows)
		{
			row["lowest_earner"].Should().Be("Bob");
		}
	}

	// ─── AVG OVER ───

	[Fact]
	public void Avg_OverPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, AVG(Salary) OVER (PARTITION BY Dept) AS avg_salary FROM Employees WHERE Dept = 'Eng' ORDER BY Id");
		foreach (var row in rows)
		{
			Convert.ToDouble(row["avg_salary"]).Should().Be(100000.0);
		}
	}

	// ─── Window function without PARTITION BY ───

	[Fact]
	public void RowNumber_WithoutPartition()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Name, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM Employees ORDER BY rn");
		rows.Should().HaveCount(5);
		for (var i = 0; i < 5; i++)
		{
			rows[i]["rn"].Should().Be((long)(i + 1));
		}
	}
}
