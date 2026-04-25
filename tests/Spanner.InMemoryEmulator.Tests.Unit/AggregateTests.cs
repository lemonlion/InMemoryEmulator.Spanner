using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for P1/P2 aggregate functions.
/// </summary>
public class AggregateTests
{
	private static InMemorySpannerDatabase CreateDatabaseWithData()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Items (Id INT64 NOT NULL, Name STRING(MAX), Category STRING(MAX), Price FLOAT64, Active BOOL, Flags INT64) PRIMARY KEY (Id)");

		db.Insert("Items", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A", ["Category"] = "X", ["Price"] = 10.0, ["Active"] = true, ["Flags"] = 5L });
		db.Insert("Items", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "B", ["Category"] = "X", ["Price"] = 20.0, ["Active"] = true, ["Flags"] = 3L });
		db.Insert("Items", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "C", ["Category"] = "Y", ["Price"] = 30.0, ["Active"] = false, ["Flags"] = 7L });
		db.Insert("Items", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "D", ["Category"] = "Y", ["Price"] = 40.0, ["Active"] = true, ["Flags"] = 1L });

		return db;
	}

	// ─── ARRAY_AGG ───

	[Fact]
	public void ArrayAgg_CollectsAllValues()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT ARRAY_AGG(Name) AS Names FROM Items");

		rows.Should().HaveCount(1);
		var names = (List<object?>)rows[0]["Names"]!;
		names.Should().HaveCount(4);
	}

	[Fact]
	public void ArrayAgg_WithDistinct_RemovesDuplicates()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT ARRAY_AGG(DISTINCT Category) AS Categories FROM Items");

		rows.Should().HaveCount(1);
		var cats = (List<object?>)rows[0]["Categories"]!;
		cats.Should().HaveCount(2);
	}

	// ─── STRING_AGG ───

	[Fact]
	public void StringAgg_ConcatenatesWithDelimiter()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT STRING_AGG(Name, ', ') AS Names FROM Items");

		rows.Should().HaveCount(1);
		var result = (string)rows[0]["Names"]!;
		result.Should().Contain("A");
		result.Should().Contain("B");
		result.Should().Contain("C");
		result.Should().Contain("D");
		result.Should().Contain(", ");
	}

	[Fact]
	public void StringAgg_DefaultDelimiterIsComma()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT STRING_AGG(Category) AS Cats FROM Items");

		rows.Should().HaveCount(1);
		var result = (string)rows[0]["Cats"]!;
		// Default delimiter is comma
		result.Should().Contain(",");
	}

	// ─── COUNTIF ───

	[Fact]
	public void CountIf_CountsTrueValues()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT COUNTIF(Active) AS ActiveCount FROM Items");

		rows.Should().HaveCount(1);
		Convert.ToInt64(rows[0]["ActiveCount"]).Should().Be(3);
	}

	[Fact]
	public void CountIf_WithExpression_Counts()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT COUNTIF(Price > 15.0) AS ExpensiveCount FROM Items");

		rows.Should().HaveCount(1);
		Convert.ToInt64(rows[0]["ExpensiveCount"]).Should().Be(3);
	}

	// ─── ANY_VALUE ───

	[Fact]
	public void AnyValue_ReturnsSomeValue()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT ANY_VALUE(Name) AS AnyName FROM Items");

		rows.Should().HaveCount(1);
		rows[0]["AnyName"].Should().NotBeNull();
	}

	[Fact]
	public void AnyValue_WithGroupBy_ReturnsPerGroup()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT Category, ANY_VALUE(Name) AS AnyName FROM Items GROUP BY Category");

		rows.Should().HaveCount(2);
		rows.All(r => r["AnyName"] != null).Should().BeTrue();
	}

	// ─── LOGICAL_AND / LOGICAL_OR ───

	[Fact]
	public void LogicalAnd_AllTrue_ReturnsTrue()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = true });

		var rows = db.ExecuteQuery("SELECT LOGICAL_AND(Flag) AS AllTrue FROM T");
		rows[0]["AllTrue"].Should().Be(true);
	}

	[Fact]
	public void LogicalAnd_OneFalse_ReturnsFalse()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT LOGICAL_AND(Active) AS AllActive FROM Items");
		rows[0]["AllActive"].Should().Be(false);
	}

	[Fact]
	public void LogicalOr_OneTrue_ReturnsTrue()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery("SELECT LOGICAL_OR(Active) AS AnyActive FROM Items");
		rows[0]["AnyActive"].Should().Be(true);
	}

	[Fact]
	public void LogicalOr_AllFalse_ReturnsFalse()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = false });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = false });

		var rows = db.ExecuteQuery("SELECT LOGICAL_OR(Flag) AS AnyTrue FROM T");
		rows[0]["AnyTrue"].Should().Be(false);
	}

	// ─── BIT_AND / BIT_OR / BIT_XOR ───

	[Fact]
	public void BitAnd_ComputesBitwiseAnd()
	{
		using var db = CreateDatabaseWithData();
		// 5 & 3 & 7 & 1 = (101 & 011) = 001 & 111 = 001 & 001 = 001 = 1
		var rows = db.ExecuteQuery("SELECT BIT_AND(Flags) AS Result FROM Items");
		Convert.ToInt64(rows[0]["Result"]).Should().Be(1L);
	}

	[Fact]
	public void BitOr_ComputesBitwiseOr()
	{
		using var db = CreateDatabaseWithData();
		// 5 | 3 | 7 | 1 = 111 = 7
		var rows = db.ExecuteQuery("SELECT BIT_OR(Flags) AS Result FROM Items");
		Convert.ToInt64(rows[0]["Result"]).Should().Be(7L);
	}

	[Fact]
	public void BitXor_ComputesBitwiseXor()
	{
		using var db = CreateDatabaseWithData();
		// 5 ^ 3 ^ 7 ^ 1 = (5^3)=6 ^7=1 ^1=0
		var rows = db.ExecuteQuery("SELECT BIT_XOR(Flags) AS Result FROM Items");
		Convert.ToInt64(rows[0]["Result"]).Should().Be(0L);
	}

	// ─── Aggregates with GROUP BY ───

	[Fact]
	public void StringAgg_WithGroupBy_AggregatesPerGroup()
	{
		using var db = CreateDatabaseWithData();
		var rows = db.ExecuteQuery(
			"SELECT Category, STRING_AGG(Name, '-') AS Names FROM Items GROUP BY Category ORDER BY Category");

		rows.Should().HaveCount(2);
		((string)rows[0]["Names"]!).Should().Contain("-");
	}

	// ─── Empty table edge cases ───

	[Fact]
	public void Aggregates_OnEmptyTable_ReturnNullOrZero()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Empty (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C, SUM(Val) AS S, ANY_VALUE(Val) AS A FROM Empty");

		rows.Should().HaveCount(1);
		Convert.ToInt64(rows[0]["C"]).Should().Be(0);
		rows[0]["S"].Should().BeNull();
		rows[0]["A"].Should().BeNull();
	}
}
