using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for SQL JOINs flowing through the full gRPC pipeline:
/// SpannerConnection → gRPC → FakeSpannerService → SqlEngine → QueryExecutor
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JoinIntegrationTests : IntegrationTestBase
{
public JoinIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task SetupSingersAndAlbums(string prefix)
	{
		await ExecuteDdlAsync(
			$"CREATE TABLE {prefix}_Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)",
			$"CREATE TABLE {prefix}_Albums (AlbumId INT64 NOT NULL, SingerId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (AlbumId)");

		await InsertAsync($"{prefix}_Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		await InsertAsync($"{prefix}_Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });
		await InsertAsync($"{prefix}_Singers", new Dictionary<string, object?> { ["SingerId"] = 3L, ["Name"] = "Charlie" });

		await InsertAsync($"{prefix}_Albums", new Dictionary<string, object?> { ["AlbumId"] = 10L, ["SingerId"] = 1L, ["Title"] = "Album A" });
		await InsertAsync($"{prefix}_Albums", new Dictionary<string, object?> { ["AlbumId"] = 20L, ["SingerId"] = 1L, ["Title"] = "Album B" });
		await InsertAsync($"{prefix}_Albums", new Dictionary<string, object?> { ["AlbumId"] = 30L, ["SingerId"] = 2L, ["Title"] = "Album C" });
	}

	// ─── INNER JOIN ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task InnerJoin_ReturnsMatchedRows()
	{
		var prefix = "J_Inner";
		await SetupSingersAndAlbums(prefix);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT s.Name, a.Title FROM {prefix}_Singers s JOIN {prefix}_Albums a ON s.SingerId = a.SingerId ORDER BY a.AlbumId");
		using var reader = await cmd.ExecuteReaderAsync();

		var rows = new List<(string Name, string Title)>();
		while (await reader.ReadAsync())
		{
			rows.Add((reader.GetString(0), reader.GetString(1)));
		}

		rows.Should().HaveCount(3);
		rows[0].Should().Be(("Alice", "Album A"));
		rows[1].Should().Be(("Alice", "Album B"));
		rows[2].Should().Be(("Bob", "Album C"));
	}

	// ─── LEFT JOIN ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task LeftJoin_IncludesUnmatchedLeft()
	{
		var prefix = "J_Left";
		await SetupSingersAndAlbums(prefix);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT s.Name, a.Title FROM {prefix}_Singers s LEFT JOIN {prefix}_Albums a ON s.SingerId = a.SingerId ORDER BY s.SingerId, a.AlbumId");
		using var reader = await cmd.ExecuteReaderAsync();

		var rows = new List<(string Name, string? Title)>();
		while (await reader.ReadAsync())
		{
			rows.Add((
				reader.GetString(0),
				reader.IsDBNull(1) ? null : reader.GetString(1)));
		}

		rows.Should().HaveCount(4); // Alice: 2, Bob: 1, Charlie: NULL
		rows.Last().Name.Should().Be("Charlie");
		rows.Last().Title.Should().BeNull();
	}

	// ─── CROSS JOIN ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task CrossJoin_ReturnsCartesianProduct()
	{
		var prefix = "J_Cross";
		await ExecuteDdlAsync(
			$"CREATE TABLE {prefix}_T1 (Id INT64 NOT NULL) PRIMARY KEY (Id)",
			$"CREATE TABLE {prefix}_T2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync($"{prefix}_T1", new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync($"{prefix}_T1", new Dictionary<string, object?> { ["Id"] = 2L });
		await InsertAsync($"{prefix}_T2", new Dictionary<string, object?> { ["Id"] = 10L });
		await InsertAsync($"{prefix}_T2", new Dictionary<string, object?> { ["Id"] = 20L });
		await InsertAsync($"{prefix}_T2", new Dictionary<string, object?> { ["Id"] = 30L });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT a.Id AS AId, b.Id AS BId FROM {prefix}_T1 a CROSS JOIN {prefix}_T2 b");
		using var reader = await cmd.ExecuteReaderAsync();

		int count = 0;
		while (await reader.ReadAsync()) count++;

		count.Should().Be(6); // 2 × 3
	}

	// ─── JOIN with WHERE ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task Join_WithWhere_Filters()
	{
		var prefix = "J_Where";
		await SetupSingersAndAlbums(prefix);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT s.Name, a.Title FROM {prefix}_Singers s JOIN {prefix}_Albums a ON s.SingerId = a.SingerId WHERE s.Name = 'Alice'");
		using var reader = await cmd.ExecuteReaderAsync();

		int count = 0;
		while (await reader.ReadAsync()) count++;

		count.Should().Be(2);
	}

	// ─── JOIN with GROUP BY ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task Join_WithGroupBy_Aggregates()
	{
		var prefix = "J_Group";
		await SetupSingersAndAlbums(prefix);

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT s.Name, COUNT(*) AS AlbumCount FROM {prefix}_Singers s JOIN {prefix}_Albums a ON s.SingerId = a.SingerId GROUP BY s.Name ORDER BY s.Name");
		using var reader = await cmd.ExecuteReaderAsync();

		var rows = new List<(string Name, long Count)>();
		while (await reader.ReadAsync())
		{
			rows.Add((reader.GetString(0), reader.GetInt64(1)));
		}

		rows.Should().HaveCount(2);
		rows[0].Should().Be(("Alice", 2));
		rows[1].Should().Be(("Bob", 1));
	}

	// ─── Self JOIN ───

	[Fact]
	[Trait(TestTraits.Category, "Join")]
	public async Task SelfJoin_Works()
	{
		var prefix = "J_Self";
		await ExecuteDdlAsync(
			$"CREATE TABLE {prefix}_Employees (Id INT64 NOT NULL, Name STRING(MAX), ManagerId INT64) PRIMARY KEY (Id)");
		await InsertAsync($"{prefix}_Employees", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["ManagerId"] = null });
		await InsertAsync($"{prefix}_Employees", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["ManagerId"] = 1L });

		using var connection = Fixture.CreateConnection();
		using var cmd = connection.CreateSelectCommand(
			$"SELECT e.Name AS EmpName, m.Name AS MgrName FROM {prefix}_Employees e JOIN {prefix}_Employees m ON e.ManagerId = m.Id");
		using var reader = await cmd.ExecuteReaderAsync();

		var rows = new List<(string Emp, string Mgr)>();
		while (await reader.ReadAsync())
		{
			rows.Add((reader.GetString(0), reader.GetString(1)));
		}

		rows.Should().HaveCount(1);
		rows[0].Should().Be(("Bob", "Alice"));
	}
}
