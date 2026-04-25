using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 13: INFORMATION_SCHEMA, Views, Sequences.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SchemaIntrospectionIntegrationTests
{
	private readonly ITestDatabaseFixture _fixture;

	public SchemaIntrospectionIntegrationTests(EmulatorSession session)
	{
		_fixture = TestFixtureFactory.Create(session);
	}

	// ─── INFORMATION_SCHEMA.TABLES ───

	[Fact]
	public async Task InformationSchema_Tables_ReturnsTables()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE IS_TestT (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IS_TestT'");
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("IS_TestT");
	}

	// ─── INFORMATION_SCHEMA.COLUMNS ───

	[Fact]
	public async Task InformationSchema_Columns_ReturnsColumnMetadata()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE IS_ColT (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT COLUMN_NAME, SPANNER_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'IS_ColT' AND COLUMN_NAME = 'Val'");
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("Val");
		reader.GetString(1).Should().Be("STRING(100)");
	}

	// ─── INFORMATION_SCHEMA.INDEXES ───

	[Fact]
	public async Task InformationSchema_Indexes_ShowsPrimaryKey()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE IS_IdxT (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'IS_IdxT' AND INDEX_NAME = 'PRIMARY_KEY'");
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("PRIMARY_KEY");
	}

	// ─── VIEWS ───

	[Fact]
	public async Task View_QueryViaSdk()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE IS_ViewT (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("IS_ViewT", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		db.Insert("IS_ViewT", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		db.ExecuteDdl("CREATE VIEW IS_ViewV SQL SECURITY INVOKER AS SELECT Name FROM IS_ViewT WHERE Id = 1");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT Name FROM IS_ViewV");
		using var reader = await cmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("Alice");
		(await reader.ReadAsync()).Should().BeFalse();
	}

	// ─── SEQUENCES ───

	[Fact]
	public async Task Sequence_GetNextValueViaSdk()
	{
		var db = _fixture.Database!;
		db.ExecuteDdl("CREATE TABLE IS_SeqT (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("IS_SeqT", new Dictionary<string, object?> { ["Id"] = 1L });
		db.ExecuteDdl("CREATE SEQUENCE IS_TestSeq OPTIONS (sequence_kind='bit_reversed_positive')");

		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE IS_TestSeq) FROM IS_SeqT WHERE Id=1");
		var result = (long)(await cmd.ExecuteScalarAsync())!;
		result.Should().NotBe(0);
	}
}
