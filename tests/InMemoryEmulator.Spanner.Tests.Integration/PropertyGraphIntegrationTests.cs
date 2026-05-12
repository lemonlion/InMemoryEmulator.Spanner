using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;
using Xunit;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for property graph DDL stubs.
/// Ref: https://cloud.google.com/spanner/docs/graph/schema-overview
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class PropertyGraphIntegrationTests : IntegrationTestBase
{
	public PropertyGraphIntegrationTests(EmulatorSession session) : base(session) { }

	[Fact]
	public async Task CreatePropertyGraph_Simple_Succeeds()
	{
		await ExecuteDdlAsync("CREATE TABLE PgPerson (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE TABLE PgAccount (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = () => ExecuteDdlAsync(
			"CREATE PROPERTY GRAPH PgFinGraph NODE TABLES (PgPerson, PgAccount)");
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task CreateOrReplacePropertyGraph_Succeeds()
	{
		await ExecuteDdlAsync("CREATE TABLE PgPerson2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE PROPERTY GRAPH PgGraph2 NODE TABLES (PgPerson2)");
		var act = () => ExecuteDdlAsync(
			"CREATE OR REPLACE PROPERTY GRAPH PgGraph2 NODE TABLES (PgPerson2)");
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DropPropertyGraph_Succeeds()
	{
		await ExecuteDdlAsync("CREATE TABLE PgPerson3 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE PROPERTY GRAPH PgGraph3 NODE TABLES (PgPerson3)");
		var act = () => ExecuteDdlAsync("DROP PROPERTY GRAPH PgGraph3");
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DropPropertyGraph_IfExists_Succeeds()
	{
		var act = () => ExecuteDdlAsync("DROP PROPERTY GRAPH IF EXISTS PgNonExistent");
		await act.Should().NotThrowAsync();
	}
}
