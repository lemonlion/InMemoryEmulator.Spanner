using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 13: INFORMATION_SCHEMA, Views, Sequences.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SchemaIntrospectionIntegrationTests : IntegrationTestBase
{
public SchemaIntrospectionIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── INFORMATION_SCHEMA.TABLES ───

	[Fact]
	public async Task InformationSchema_Tables_ReturnsTables()
	{
		await ExecuteDdlAsync("CREATE TABLE IS_TestT (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE IS_ColT (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE IS_IdxT (Id INT64 NOT NULL) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE IS_ViewT (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync("IS_ViewT", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		await InsertAsync("IS_ViewT", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });
		await ExecuteDdlAsync("CREATE VIEW IS_ViewV SQL SECURITY INVOKER AS SELECT Name FROM IS_ViewT WHERE Id = 1");

		using var conn = Fixture.CreateConnection();
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
		await ExecuteDdlAsync("CREATE TABLE IS_SeqT (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync("IS_SeqT", new Dictionary<string, object?> { ["Id"] = 1L });
		await ExecuteDdlAsync("CREATE SEQUENCE IS_TestSeq OPTIONS (sequence_kind='bit_reversed_positive')");

		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE IS_TestSeq) FROM IS_SeqT WHERE Id=1");
		var result = (long)(await cmd.ExecuteScalarAsync())!;
		result.Should().NotBe(0);
	}
}
