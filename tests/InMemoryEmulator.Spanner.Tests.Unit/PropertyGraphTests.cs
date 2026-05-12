using FluentAssertions;
using Xunit;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for property graph DDL stubs.
/// Ref: https://cloud.google.com/spanner/docs/graph/schema-overview
/// </summary>
public class PropertyGraphTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Person (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		db.ExecuteDdl("CREATE TABLE Account (Id INT64 NOT NULL, OwnerId INT64 NOT NULL) PRIMARY KEY (Id)");
		return db;
	}

	// ─── DDL: CREATE PROPERTY GRAPH ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/graph-schema-statements#gql_create_graph
	[Fact]
	public void CreatePropertyGraph_Simple_Succeeds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteDdl(
			"CREATE PROPERTY GRAPH FinGraph NODE TABLES (Person, Account)");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreatePropertyGraph_WithEdgeTables_Succeeds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteDdl(
			"CREATE PROPERTY GRAPH FinGraph NODE TABLES (Person, Account) EDGE TABLES (Account AS Owns SOURCE KEY (OwnerId) REFERENCES Person DESTINATION KEY (Id) REFERENCES Account)");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreateOrReplacePropertyGraph_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE PROPERTY GRAPH FinGraph NODE TABLES (Person)");
		var act = () => db.ExecuteDdl(
			"CREATE OR REPLACE PROPERTY GRAPH FinGraph NODE TABLES (Person, Account)");
		act.Should().NotThrow();
	}

	// ─── DDL: DROP PROPERTY GRAPH ───

	// Ref: https://cloud.google.com/spanner/docs/graph/create-update-drop-schema#drop-property-graph
	[Fact]
	public void DropPropertyGraph_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE PROPERTY GRAPH FinGraph NODE TABLES (Person)");
		var act = () => db.ExecuteDdl("DROP PROPERTY GRAPH FinGraph");
		act.Should().NotThrow();
	}

	[Fact]
	public void DropPropertyGraph_IfExists_Succeeds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteDdl("DROP PROPERTY GRAPH IF EXISTS NonExistent");
		act.Should().NotThrow();
	}
}
