using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for UNNEST and array access features (Phase 15).
/// </summary>
public class UnnestAndArrayTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		return db;
	}

	// ─── UNNEST ───

	[Fact]
	public void Unnest_FlattenArrayLiteral()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT val FROM UNNEST([1, 2, 3]) AS val");
		rows.Should().HaveCount(3);
		rows[0]["val"]?.ToString().Should().Be("1");
		rows[1]["val"]?.ToString().Should().Be("2");
		rows[2]["val"]?.ToString().Should().Be("3");
	}

	[Fact]
	public void Unnest_WithOffset()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT val, pos FROM UNNEST(['a', 'b', 'c']) AS val WITH OFFSET AS pos ORDER BY pos");
		rows.Should().HaveCount(3);
		rows[0]["val"]?.ToString().Should().Be("a");
		rows[0]["pos"]?.ToString().Should().Be("0");
		rows[2]["val"]?.ToString().Should().Be("c");
		rows[2]["pos"]?.ToString().Should().Be("2");
	}

	[Fact]
	public void Unnest_EmptyArray()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT val FROM UNNEST([]) AS val");
		rows.Should().HaveCount(0);
	}

	// ─── Array element access ───

	[Fact]
	public void ArrayAccess_Offset()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT [10, 20, 30][OFFSET(1)] AS val FROM T WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("20");
	}

	[Fact]
	public void ArrayAccess_Ordinal()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT [10, 20, 30][ORDINAL(1)] AS val FROM T WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("10");
	}

	[Fact]
	public void ArrayAccess_SafeOffset_OutOfBounds()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT [10, 20, 30][SAFE_OFFSET(5)] AS val FROM T WHERE Id = 1");
		rows[0]["val"].Should().BeNull();
	}

	[Fact]
	public void ArrayAccess_SafeOrdinal_OutOfBounds()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT [10, 20][SAFE_ORDINAL(5)] AS val FROM T WHERE Id = 1");
		rows[0]["val"].Should().BeNull();
	}

	[Fact]
	public void ArrayAccess_Offset_ThrowsOnOutOfBounds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteQuery("SELECT [10, 20][OFFSET(5)] AS val FROM T WHERE Id = 1");
		act.Should().Throw<Exception>();
	}
}
