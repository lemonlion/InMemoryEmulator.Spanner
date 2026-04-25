using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for SQL JOIN execution.
/// </summary>
public class JoinTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithSingersAndAlbums()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)",
			"CREATE TABLE Albums (AlbumId INT64 NOT NULL, SingerId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (AlbumId)");

		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie" });

		db.Insert("Albums", new Dictionary<string, object?> { ["AlbumId"] = 10L, ["SingerId"] = 1L, ["Title"] = "Album A" });
		db.Insert("Albums", new Dictionary<string, object?> { ["AlbumId"] = 20L, ["SingerId"] = 1L, ["Title"] = "Album B" });
		db.Insert("Albums", new Dictionary<string, object?> { ["AlbumId"] = 30L, ["SingerId"] = 2L, ["Title"] = "Album C" });

		return db;
	}

	// ─── INNER JOIN ───

	[Fact]
	public void InnerJoin_OnMatching_ReturnsMatchedRows()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, a.Title FROM Singers s JOIN Albums a ON s.SingerId = a.SingerId ORDER BY a.AlbumId");

		rows.Should().HaveCount(3);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Title"].Should().Be("Album A");
		rows[1]["Title"].Should().Be("Album B");
		rows[2]["Name"].Should().Be("Bob");
		rows[2]["Title"].Should().Be("Album C");
	}

	[Fact]
	public void InnerJoin_NoMatch_ReturnsEmpty()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE T1 (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			"CREATE TABLE T2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 1L });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 2L });

		var rows = db.ExecuteQuery("SELECT T1.Id, T2.Id FROM T1 JOIN T2 ON T1.Id = T2.Id");

		rows.Should().BeEmpty();
	}

	[Fact]
	public void InnerJoin_ExplicitInner_Works()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, a.Title FROM Singers s INNER JOIN Albums a ON s.SingerId = a.SingerId");

		rows.Should().HaveCount(3);
	}

	// ─── LEFT JOIN ───

	[Fact]
	public void LeftJoin_IncludesUnmatchedLeftRows()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, a.Title FROM Singers s LEFT JOIN Albums a ON s.SingerId = a.SingerId ORDER BY s.SingerId");

		rows.Should().HaveCount(4); // Alice: 2 albums, Bob: 1 album, Charlie: 0 albums (NULL)
		var charlieRow = rows.Last();
		charlieRow["Name"].Should().Be("Charlie");
		charlieRow["Title"].Should().BeNull();
	}

	[Fact]
	public void LeftOuterJoin_ExplicitOuter_Works()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, a.Title FROM Singers s LEFT OUTER JOIN Albums a ON s.SingerId = a.SingerId ORDER BY s.SingerId");

		rows.Should().HaveCount(4);
	}

	// ─── RIGHT JOIN ───

	[Fact]
	public void RightJoin_IncludesUnmatchedRightRows()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE T1 (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)",
			"CREATE TABLE T2 (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "A" });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "X" });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "Y" });

		var rows = db.ExecuteQuery(
			"SELECT a.Val AS AVal, b.Val AS BVal FROM T1 a RIGHT JOIN T2 b ON a.Id = b.Id ORDER BY b.Id");

		rows.Should().HaveCount(2);
		// T2.Id=1 matched → a.Val="A", b.Val="X"
		rows[0]["AVal"].Should().Be("A");
		rows[0]["BVal"].Should().Be("X");
		// T2.Id=2 unmatched → a.Val=NULL, b.Val="Y"
		rows[1]["AVal"].Should().BeNull();
		rows[1]["BVal"].Should().Be("Y");
	}

	// ─── CROSS JOIN ───

	[Fact]
	public void CrossJoin_ReturnsCartesianProduct()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE T1 (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			"CREATE TABLE T2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 1L });
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 2L });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 10L });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 20L });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 30L });

		var rows = db.ExecuteQuery("SELECT T1.Id, T2.Id FROM T1 CROSS JOIN T2");

		rows.Should().HaveCount(6); // 2 × 3
	}

	// ─── FULL JOIN ───

	[Fact]
	public void FullJoin_IncludesBothUnmatchedSides()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl(
			"CREATE TABLE T1 (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)",
			"CREATE TABLE T2 (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "A" });
		db.Insert("T1", new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "B" });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "X" });
		db.Insert("T2", new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = "Y" });

		var rows = db.ExecuteQuery(
			"SELECT a.Val, b.Val FROM T1 a FULL JOIN T2 b ON a.Id = b.Id ORDER BY a.Id, b.Id");

		rows.Should().HaveCount(3);
	}

	// ─── JOIN with aggregation ───

	[Fact]
	public void Join_WithGroupBy_Works()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, COUNT(*) AS AlbumCount FROM Singers s JOIN Albums a ON s.SingerId = a.SingerId GROUP BY s.Name ORDER BY s.Name");

		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Alice");
		Convert.ToInt64(rows[0]["AlbumCount"]).Should().Be(2);
		rows[1]["Name"].Should().Be("Bob");
		Convert.ToInt64(rows[1]["AlbumCount"]).Should().Be(1);
	}

	// ─── JOIN with WHERE ───

	[Fact]
	public void Join_WithWhere_FiltersAfterJoin()
	{
		using var db = CreateDatabaseWithSingersAndAlbums();

		var rows = db.ExecuteQuery(
			"SELECT s.Name, a.Title FROM Singers s JOIN Albums a ON s.SingerId = a.SingerId WHERE s.Name = 'Alice'");

		rows.Should().HaveCount(2);
		rows.All(r => (string)r["Name"]! == "Alice").Should().BeTrue();
	}

	// ─── Self JOIN ───

	[Fact]
	public void SelfJoin_Works()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Employees (Id INT64 NOT NULL, Name STRING(MAX), ManagerId INT64) PRIMARY KEY (Id)");
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["ManagerId"] = null });
		db.Insert("Employees", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["ManagerId"] = 1L });

		var rows = db.ExecuteQuery(
			"SELECT e.Name AS EmpName, m.Name AS MgrName FROM Employees e JOIN Employees m ON e.ManagerId = m.Id");

		rows.Should().HaveCount(1);
		rows[0]["EmpName"].Should().Be("Bob");
		rows[0]["MgrName"].Should().Be("Alice");
	}
}
