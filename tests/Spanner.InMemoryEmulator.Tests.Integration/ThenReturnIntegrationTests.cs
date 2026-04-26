using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for THEN RETURN clause on DML statements.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#then_return
///   "THEN RETURN returns data from rows that are modified by a DML statement."
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ThenReturnIntegrationTests : IntegrationTestBase
{
	public ThenReturnIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE ThenReturnTest (Id INT64 NOT NULL, Name STRING(100), Val INT64) PRIMARY KEY (Id)");
		}
		catch { }
	}

	/// <summary>
	/// Executes a DML statement with THEN RETURN and returns the result rows.
	/// </summary>
	private async Task<List<Dictionary<string, object?>>> ExecuteDmlWithReturnAsync(string sql,
		params (string name, SpannerDbType type, object? value)[] parameters)
	{
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand(sql);
		cmd.Transaction = txn;
		foreach (var (name, type, value) in parameters)
		{
			cmd.Parameters.Add(name, type, value ?? DBNull.Value);
		}

		var results = new List<Dictionary<string, object?>>();
		using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>();
			for (int i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			results.Add(row);
		}
		await txn.CommitAsync();
		return results;
	}

	// ═══════════════════════════════════════════════════════════════
	// INSERT ... THEN RETURN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Insert_ThenReturn_AllColumns()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (1, 'Alice', 10) THEN RETURN *");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(1L);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Val"].Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Insert_ThenReturn_SpecificColumns()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (2, 'Bob', 20) THEN RETURN Id, Name");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(2L);
		rows[0]["Name"].Should().Be("Bob");
		rows[0].Should().NotContainKey("Val");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Insert_ThenReturn_MultipleRows()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (3, 'C', 30), (4, 'D', 40) THEN RETURN Id, Name");

		rows.Should().HaveCount(2);
		rows.Select(r => (long)r["Id"]!).Should().BeEquivalentTo(new[] { 3L, 4L });
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Insert_ThenReturn_Expression()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (5, 'Eve', 50) THEN RETURN Id, Val * 2 AS DoubleVal");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(5L);
		rows[0]["DoubleVal"].Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InsertOrUpdate_ThenReturn_WithAction_Insert()
	{
		await EnsureTableAsync();
		// Ensure row doesn't exist
		await ExecuteDmlAsync("DELETE FROM ThenReturnTest WHERE Id = 6");

		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT OR UPDATE INTO ThenReturnTest (Id, Name, Val) VALUES (6, 'Frank', 60) THEN RETURN WITH ACTION AS op *");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(6L);
		rows[0]["op"].Should().Be("INSERT");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task InsertOrUpdate_ThenReturn_WithAction_Update()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (7, 'Gina', 70)");

		var rows = await ExecuteDmlWithReturnAsync(
			"INSERT OR UPDATE INTO ThenReturnTest (Id, Name, Val) VALUES (7, 'Gina2', 77) THEN RETURN WITH ACTION *");

		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Gina2");
		rows[0]["ACTION"].Should().Be("UPDATE");
	}

	// ═══════════════════════════════════════════════════════════════
	// UPDATE ... THEN RETURN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Update_ThenReturn_AllColumns()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (10, 'Update1', 100)");

		var rows = await ExecuteDmlWithReturnAsync(
			"UPDATE ThenReturnTest SET Val = 200 WHERE Id = 10 THEN RETURN *");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(10L);
		rows[0]["Val"].Should().Be(200L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Update_ThenReturn_SpecificColumns()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (11, 'Update2', 110)");

		var rows = await ExecuteDmlWithReturnAsync(
			"UPDATE ThenReturnTest SET Val = 220 WHERE Id = 11 THEN RETURN Name, Val");

		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Update2");
		rows[0]["Val"].Should().Be(220L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Update_ThenReturn_MultipleRows()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (12, 'A', 1)");
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (13, 'B', 2)");

		var rows = await ExecuteDmlWithReturnAsync(
			"UPDATE ThenReturnTest SET Val = Val + 100 WHERE Id IN (12, 13) THEN RETURN Id, Val");

		rows.Should().HaveCount(2);
		rows.Select(r => (long)r["Val"]!).Should().BeEquivalentTo(new[] { 101L, 102L });
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Update_ThenReturn_NoMatchingRows()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"UPDATE ThenReturnTest SET Val = 999 WHERE Id = 99999 THEN RETURN *");

		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// DELETE ... THEN RETURN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Delete_ThenReturn_AllColumns()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (20, 'Delete1', 200)");

		var rows = await ExecuteDmlWithReturnAsync(
			"DELETE FROM ThenReturnTest WHERE Id = 20 THEN RETURN *");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(20L);
		rows[0]["Name"].Should().Be("Delete1");
		rows[0]["Val"].Should().Be(200L);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Delete_ThenReturn_SpecificColumns()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (21, 'Delete2', 210)");

		var rows = await ExecuteDmlWithReturnAsync(
			"DELETE FROM ThenReturnTest WHERE Id = 21 THEN RETURN Id, Name");

		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(21L);
		rows[0]["Name"].Should().Be("Delete2");
		rows[0].Should().NotContainKey("Val");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Delete_ThenReturn_MultipleRows()
	{
		await EnsureTableAsync();
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (22, 'X', 1)");
		await ExecuteDmlAsync("INSERT INTO ThenReturnTest (Id, Name, Val) VALUES (23, 'Y', 2)");

		var rows = await ExecuteDmlWithReturnAsync(
			"DELETE FROM ThenReturnTest WHERE Id IN (22, 23) THEN RETURN Id");

		rows.Should().HaveCount(2);
		rows.Select(r => (long)r["Id"]!).Should().BeEquivalentTo(new[] { 22L, 23L });
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Delete_ThenReturn_NoMatchingRows()
	{
		await EnsureTableAsync();
		var rows = await ExecuteDmlWithReturnAsync(
			"DELETE FROM ThenReturnTest WHERE Id = 99998 THEN RETURN *");

		rows.Should().BeEmpty();
	}
}
