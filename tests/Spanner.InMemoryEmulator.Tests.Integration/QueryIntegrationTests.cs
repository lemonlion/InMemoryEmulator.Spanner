using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for SQL queries flowing through the full gRPC pipeline:
/// SpannerConnection → gRPC → FakeSpannerService → SqlEngine → QueryExecutor
/// </summary>
[Collection(IntegrationCollection.Name)]
public class QueryIntegrationTests : IntegrationTestBase
{
	public QueryIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task SetupSingersTable(string tableName)
	{
		await ExecuteDdlAsync($"CREATE TABLE {tableName} (SingerId INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (SingerId)");
		await InsertAsync(tableName, new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice", ["Age"] = 30L });
		await InsertAsync(tableName, new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob", ["Age"] = 25L });
		await InsertAsync(tableName, new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie", ["Age"] = 35L });
	}

	// ─── Basic SELECT ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectAll_ReturnsAllRows()
	{
		var table = "Q_SelectAll";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT * FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();

		var rows = new List<(long Id, string Name, long? Age)>();
		while (await reader.ReadAsync())
		{
			rows.Add((
				reader.GetInt64(reader.GetOrdinal("SingerId")),
				reader.GetString(reader.GetOrdinal("Name")),
				reader.IsDBNull(reader.GetOrdinal("Age")) ? null : reader.GetInt64(reader.GetOrdinal("Age"))
			));
		}

		rows.Should().HaveCount(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectColumns_ReturnsOnlyRequestedColumns()
	{
		var table = "Q_SelectCols";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table} WHERE SingerId = 1");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.FieldCount.Should().Be(1);
		reader.GetString(0).Should().Be("Alice");
	}

	// ─── WHERE clause ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithWhere_FiltersRows()
	{
		var table = "Q_Where";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table} WHERE Age > 28");
		using var reader = await cmd.ExecuteReaderAsync();

		var names = new List<string>();
		while (await reader.ReadAsync())
		{
			names.Add(reader.GetString(0));
		}

		names.Should().HaveCount(2);
		names.Should().Contain("Alice");
		names.Should().Contain("Charlie");
	}

	// ─── Parameters ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithParameters_BindsCorrectly()
	{
		var table = "Q_Params";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT Name FROM {table} WHERE SingerId = @id",
			new SpannerParameterCollection { { "id", SpannerDbType.Int64, 2L } });
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetString(0).Should().Be("Bob");
	}

	// ─── ORDER BY ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithOrderBy_ReturnsOrderedRows()
	{
		var table = "Q_OrderBy";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table} ORDER BY Age DESC");
		using var reader = await cmd.ExecuteReaderAsync();

		var names = new List<string>();
		while (await reader.ReadAsync())
		{
			names.Add(reader.GetString(0));
		}

		names[0].Should().Be("Charlie"); // 35
		names[^1].Should().Be("Bob"); // 25
	}

	// ─── LIMIT / OFFSET ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithLimit_ReturnsLimitedRows()
	{
		var table = "Q_Limit";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Name FROM {table} ORDER BY SingerId LIMIT 2");
		using var reader = await cmd.ExecuteReaderAsync();

		var names = new List<string>();
		while (await reader.ReadAsync()) names.Add(reader.GetString(0));

		names.Should().HaveCount(2);
	}

	// ─── Aggregates ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectCountStar_ReturnsCount()
	{
		var table = "Q_Count";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT COUNT(*) AS cnt FROM {table}");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(3);
	}

	// ─── Column metadata ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectMetadata_ReturnsCorrectFieldNames()
	{
		var table = "Q_Meta";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT SingerId, Name, Age FROM {table} WHERE SingerId = 1");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetName(0).Should().Be("SingerId");
		reader.GetName(1).Should().Be("Name");
		reader.GetName(2).Should().Be("Age");
	}

	// ─── NULL handling ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectNullColumn_ReturnsDBNull()
	{
		var table = "Q_Null";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = null });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT Val FROM {table} WHERE Id = 1");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	// ─── SELECT without FROM ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectLiteral_ReturnsValue()
	{
		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand("SELECT 42 AS val");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetInt64(0).Should().Be(42);
	}

	// ─── Functions ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithFunction_UpperWorks()
	{
		var table = "Q_Upper";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT UPPER(Name) AS UpperName FROM {table} WHERE SingerId = 1");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetString(0).Should().Be("ALICE");
	}

	// ─── CAST ───

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task SelectWithCast_ConvertsType()
	{
		var table = "Q_Cast";
		await SetupSingersTable(table);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand($"SELECT CAST(SingerId AS STRING) AS IdStr FROM {table} WHERE SingerId = 1");
		using var reader = await cmd.ExecuteReaderAsync();

		await reader.ReadAsync();
		reader.GetString(0).Should().Be("1");
	}
}
