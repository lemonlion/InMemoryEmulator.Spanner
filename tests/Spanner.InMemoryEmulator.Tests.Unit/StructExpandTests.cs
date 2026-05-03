using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for STRUCT.* expansion (dot star operator).
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#struct_field_access_operator
///   "STRUCT(...).*": The dot star operator returns all fields of a STRUCT.
/// </summary>
public class StructExpandTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		return db;
	}

	[Fact]
	public void Struct_DotStar_ExpandsLiteralFields()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT STRUCT(1 AS a, 'hello' AS b).* FROM T WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0].Should().ContainKey("a").WhoseValue.Should().Be(1L);
		rows[0].Should().ContainKey("b").WhoseValue.Should().Be("hello");
	}

	[Fact]
	public void Struct_DotStar_WithColumnRefs()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT STRUCT(Id AS id, Name AS name).* FROM T ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["id"].Should().Be(1L);
		rows[0]["name"].Should().Be("Alice");
		rows[1]["id"].Should().Be(2L);
		rows[1]["name"].Should().Be("Bob");
	}

	[Fact]
	public void Struct_DotStar_MixedWithOtherColumns()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT Id, STRUCT('x' AS val, 42 AS num).*, Name FROM T WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
		rows[0]["val"].Should().Be("x");
		rows[0]["num"].Should().Be(42L);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void Struct_FieldAccess_SingleField()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT STRUCT(1 AS a, 'hello' AS b).a AS result FROM T WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().Be(1L);
	}
}
