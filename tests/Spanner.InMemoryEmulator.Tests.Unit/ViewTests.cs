using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for CREATE VIEW / DROP VIEW functionality.
/// </summary>
public class ViewTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });
		return db;
	}

	[Fact]
	public void CreateView_QueryReturnsFilteredData()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW OlderSingers SQL SECURITY INVOKER AS SELECT Name FROM Singers WHERE Age > 28");
		var rows = db.ExecuteQuery("SELECT Name FROM OlderSingers");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void CreateOrReplaceView_OverwritesExisting()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW MySingers SQL SECURITY INVOKER AS SELECT Name FROM Singers WHERE Age > 100");
		db.ExecuteQuery("SELECT Name FROM MySingers").Should().BeEmpty();

		db.ExecuteDdl("CREATE OR REPLACE VIEW MySingers SQL SECURITY INVOKER AS SELECT Name FROM Singers");
		db.ExecuteQuery("SELECT Name FROM MySingers").Should().HaveCount(2);
	}

	[Fact]
	public void CreateView_DuplicateThrows()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW V1 SQL SECURITY INVOKER AS SELECT Name FROM Singers");
		var act = () => db.ExecuteDdl("CREATE VIEW V1 SQL SECURITY INVOKER AS SELECT Name FROM Singers");
		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
	}

	[Fact]
	public void DropView_RemovesView()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW V2 SQL SECURITY INVOKER AS SELECT Name FROM Singers");
		db.ExecuteQuery("SELECT Name FROM V2").Should().HaveCount(2);
		db.ExecuteDdl("DROP VIEW V2");
		var act = () => db.ExecuteQuery("SELECT Name FROM V2");
		act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
	}

	[Fact]
	public void View_WithAlias()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW AllSingers SQL SECURITY INVOKER AS SELECT SingerId, Name FROM Singers");
		var rows = db.ExecuteQuery("SELECT v.Name FROM AllSingers v WHERE v.SingerId = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	public void View_WithoutSqlSecurity()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE VIEW SimpleView AS SELECT Name FROM Singers");
		var rows = db.ExecuteQuery("SELECT Name FROM SimpleView");
		rows.Should().HaveCount(2);
	}
}
