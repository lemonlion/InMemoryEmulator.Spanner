using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for SQL SELECT query execution via the direct API.
/// </summary>
public class QueryExecutorTests
{
	private InMemorySpannerDatabase CreatePopulatedDatabase()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");

		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie", ["Age"] = 35L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 4L, ["Name"] = "Diana", ["Age"] = 25L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 5L, ["Name"] = "Eve", ["Age"] = 28L });

		return db;
	}

	// ─── Basic SELECT ───

	[Fact]
	public void Select_StarFromTable_ReturnsAllColumns()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT * FROM Singers");

		rows.Should().HaveCount(5);
		rows[0].Should().ContainKeys("SingerId", "Name", "Age");
	}

	[Fact]
	public void Select_SpecificColumns_ReturnsOnlyThoseColumns()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers");

		rows.Should().HaveCount(5);
		rows[0].Should().ContainKey("Name");
	}

	[Fact]
	public void Select_WithAlias_ReturnsAliasedColumn()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name AS SingerName FROM Singers");

		rows[0].Should().ContainKey("SingerName");
	}

	// ─── WHERE clause ───

	[Fact]
	public void Select_WhereEqual_FiltersRows()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId = 1");

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void Select_WhereGreaterThan_FiltersRows()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE Age > 28");

		rows.Should().HaveCount(2); // Alice (30), Charlie (35)
	}

	[Fact]
	public void Select_WhereAnd_FiltersWithMultipleConditions()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE Age >= 25 AND Age <= 28");

		rows.Should().HaveCount(3); // Bob (25), Diana (25), Eve (28)
	}

	[Fact]
	public void Select_WhereOr_FiltersWithEitherCondition()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE Name = 'Alice' OR Name = 'Bob'");

		rows.Should().HaveCount(2);
	}

	[Fact]
	public void Select_WhereIsNull_FiltersNullValues()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Value STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Value"] = null });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Value"] = "hello" });

		var rows = db.ExecuteQuery("SELECT Id FROM T WHERE Value IS NULL");

		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
	}

	[Fact]
	public void Select_WhereIsNotNull_FiltersNonNullValues()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Value STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Value"] = null });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Value"] = "hello" });

		var rows = db.ExecuteQuery("SELECT Id FROM T WHERE Value IS NOT NULL");

		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(2L);
	}

	[Fact]
	public void Select_WhereIn_FiltersMatchingValues()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE SingerId IN (1, 3, 5)");

		rows.Should().HaveCount(3);
	}

	[Fact]
	public void Select_WhereBetween_FiltersRange()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers WHERE Age BETWEEN 25 AND 30");

		rows.Should().HaveCount(4); // Bob, Alice, Diana, Eve
	}

	// ─── ORDER BY ───

	[Fact]
	public void Select_OrderByAsc_ReturnsInOrder()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers ORDER BY Age ASC");

		rows[0]["Name"].Should().BeOneOf("Bob", "Diana"); // Both 25
		rows[^1]["Name"].Should().Be("Charlie"); // 35
	}

	[Fact]
	public void Select_OrderByDesc_ReturnsInReverseOrder()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers ORDER BY Age DESC");

		rows[0]["Name"].Should().Be("Charlie"); // 35
	}

	[Fact]
	public void Select_OrderByMultipleColumns_ReturnsCorrectOrder()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Name FROM Singers ORDER BY Age ASC, Name ASC");

		// Age 25: Bob, Diana; Age 28: Eve; Age 30: Alice; Age 35: Charlie
		rows[0]["Name"].Should().Be("Bob");
		rows[1]["Name"].Should().Be("Diana");
		rows[2]["Name"].Should().Be("Eve");
		rows[3]["Name"].Should().Be("Alice");
		rows[4]["Name"].Should().Be("Charlie");
	}

	// ─── LIMIT / OFFSET ───

	[Fact]
	public void Select_Limit_ReturnsLimitedRows()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers ORDER BY SingerId LIMIT 3");

		rows.Should().HaveCount(3);
	}

	[Fact]
	public void Select_LimitOffset_ReturnsPagedRows()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT SingerId FROM Singers ORDER BY SingerId LIMIT 2 OFFSET 2");

		rows.Should().HaveCount(2);
		rows[0]["SingerId"].Should().Be(3L);
		rows[1]["SingerId"].Should().Be(4L);
	}

	// ─── DISTINCT ───

	[Fact]
	public void Select_Distinct_RemovesDuplicates()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT DISTINCT Age FROM Singers ORDER BY Age");

		rows.Should().HaveCount(4); // 25, 28, 30, 35
	}

	// ─── Aggregate functions ───

	[Fact]
	public void Select_CountStar_ReturnsRowCount()
	{
		using var db = CreatePopulatedDatabase();

		var count = db.ExecuteScalar<long>("SELECT COUNT(*) FROM Singers");

		count.Should().Be(5);
	}

	[Fact]
	public void Select_Sum_ReturnsSumOfColumn()
	{
		using var db = CreatePopulatedDatabase();

		var result = db.ExecuteQuery("SELECT SUM(Age) AS TotalAge FROM Singers");

		// 30 + 25 + 35 + 25 + 28 = 143
		var totalAge = Convert.ToDouble(result[0]["TotalAge"]);
		totalAge.Should().Be(143.0);
	}

	[Fact]
	public void Select_Avg_ReturnsAverage()
	{
		using var db = CreatePopulatedDatabase();

		var result = db.ExecuteQuery("SELECT AVG(Age) AS AvgAge FROM Singers");

		var avgAge = Convert.ToDouble(result[0]["AvgAge"]);
		avgAge.Should().BeApproximately(28.6, 0.1); // 143/5
	}

	[Fact]
	public void Select_MinMax_ReturnsMinAndMax()
	{
		using var db = CreatePopulatedDatabase();

		var result = db.ExecuteQuery("SELECT MIN(Age) AS MinAge, MAX(Age) AS MaxAge FROM Singers");

		Convert.ToInt64(result[0]["MinAge"]).Should().Be(25L);
		Convert.ToInt64(result[0]["MaxAge"]).Should().Be(35L);
	}

	// ─── GROUP BY ───

	[Fact]
	public void Select_GroupBy_GroupsCorrectly()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT Age, COUNT(*) AS Cnt FROM Singers GROUP BY Age ORDER BY Age");

		rows.Should().HaveCount(4); // 25, 28, 30, 35
		// Age 25 has 2 rows (Bob, Diana)
		var age25 = rows.First(r => Convert.ToInt64(r["Age"]) == 25L);
		Convert.ToInt64(age25["Cnt"]).Should().Be(2);
	}

	// ─── Parameterized queries ───

	[Fact]
	public void Select_WithParameters_BindsParameterValues()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Singers WHERE SingerId = @id",
			new Dictionary<string, object?> { ["id"] = 1L });

		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void Select_WithMultipleParameters_BindsAll()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery(
			"SELECT Name FROM Singers WHERE Age >= @minAge AND Age <= @maxAge",
			new Dictionary<string, object?> { ["minAge"] = 25L, ["maxAge"] = 28L });

		rows.Should().HaveCount(3); // Bob, Diana, Eve
	}

	// ─── Expression evaluation ───

	[Fact]
	public void Select_ArithmeticExpression_EvaluatesCorrectly()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT SingerId, Age + 10 AS AgePlus10 FROM Singers WHERE SingerId = 1");

		Convert.ToInt64(rows[0]["AgePlus10"]).Should().Be(40);
	}

	[Fact]
	public void Select_StringConcatenation_Works()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT 'Hello ' || Name AS Greeting FROM Singers WHERE SingerId = 1");

		rows[0]["Greeting"].Should().Be("Hello Alice");
	}

	// ─── Built-in functions ───

	[Fact]
	public void Select_LengthFunction_ReturnsStringLength()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT LENGTH(Name) AS NameLen FROM Singers WHERE SingerId = 1");

		Convert.ToInt64(rows[0]["NameLen"]).Should().Be(5);
	}

	[Fact]
	public void Select_UpperLowerFunction_Works()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT UPPER(Name) AS UpperName, LOWER(Name) AS LowerName FROM Singers WHERE SingerId = 1");

		rows[0]["UpperName"].Should().Be("ALICE");
		rows[0]["LowerName"].Should().Be("alice");
	}

	[Fact]
	public void Select_CoalesceFunction_ReturnsFirstNonNull()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, A STRING(MAX), B STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["A"] = null, ["B"] = "fallback" });

		var rows = db.ExecuteQuery("SELECT COALESCE(A, B) AS Result FROM T");

		rows[0]["Result"].Should().Be("fallback");
	}

	[Fact]
	public void Select_CaseExpression_Works()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery(
			"SELECT Name, CASE WHEN Age >= 30 THEN 'Senior' ELSE 'Junior' END AS Category FROM Singers ORDER BY SingerId");

		rows[0]["Category"].Should().Be("Senior"); // Alice, 30
		rows[1]["Category"].Should().Be("Junior"); // Bob, 25
		rows[2]["Category"].Should().Be("Senior"); // Charlie, 35
	}

	[Fact]
	public void Select_CastExpression_ConvertsType()
	{
		using var db = CreatePopulatedDatabase();

		var rows = db.ExecuteQuery("SELECT CAST(SingerId AS STRING) AS IdStr FROM Singers WHERE SingerId = 1");

		rows[0]["IdStr"].Should().Be("1");
	}

	// ─── SELECT without FROM ───

	[Fact]
	public void Select_WithoutFrom_ReturnsLiteralValues()
	{
		using var db = new InMemorySpannerDatabase();

		var rows = db.ExecuteQuery("SELECT 1 AS One, 'hello' AS Greeting, TRUE AS Flag");

		rows.Should().HaveCount(1);
		rows[0]["One"].Should().Be(1L);
		rows[0]["Greeting"].Should().Be("hello");
		rows[0]["Flag"].Should().Be(true);
	}

	// ─── NULL handling ───

	[Fact]
	public void Select_NullComparison_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
		//   "NULL comparison: any comparison with NULL returns NULL."
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Value INT64) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Value"] = null });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Value"] = 42L });

		// NULL = 42 should return NULL (false in WHERE context)
		var rows = db.ExecuteQuery("SELECT Id FROM T WHERE Value = 42");

		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(2L);
	}

	// ─── Empty table ───

	[Fact]
	public void Select_EmptyTable_ReturnsNoRows()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var rows = db.ExecuteQuery("SELECT * FROM T");

		rows.Should().BeEmpty();
	}

	[Fact]
	public void Select_CountStarEmptyTable_ReturnsZero()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var count = db.ExecuteScalar<long>("SELECT COUNT(*) FROM T");

		count.Should().Be(0);
	}

	// ─── ExecuteScalar ───

	[Fact]
	public void ExecuteScalar_NoRows_ThrowsException()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		var act = () => db.ExecuteScalar<long>("SELECT Id FROM T");

		act.Should().Throw<InvalidOperationException>().WithMessage("*no rows*");
	}
}
