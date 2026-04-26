using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 15: Window Functions, UNNEST, Type Constructors.
/// Tests flow through the full gRPC pipeline: SpannerConnection â†’ gRPC â†’ FakeSpannerService.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowAndUnnestIntegrationTests : IntegrationTestBase
{
private bool _initialized;

public WindowAndUnnestIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		if (_initialized) return;
		try
		{
			await ExecuteDdlAsync("CREATE TABLE WinEmp (Id INT64 NOT NULL, Name STRING(MAX), Dept STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Dept"] = "Eng", ["Salary"] = 100000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Dept"] = "Eng", ["Salary"] = 90000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["Dept"] = "Sales", ["Salary"] = 80000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["Dept"] = "Sales", ["Salary"] = 80000L });
			await InsertAsync("WinEmp", new Dictionary<string, object?> { ["Id"] = 5L, ["Name"] = "Eve", ["Dept"] = "Eng", ["Salary"] = 110000L });

			await ExecuteDdlAsync("CREATE TABLE WinDummy (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await InsertAsync("WinDummy", new Dictionary<string, object?> { ["Id"] = 1L });
		}
		catch { /* table may already exist */ }
		_initialized = true;
	}

	private async Task<List<Dictionary<string, object?>>> QueryAsync(string sql)
	{
		await EnsureTableAsync();
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		var rows = new List<Dictionary<string, object?>>();
		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			rows.Add(row);
		}
		return rows;
	}

	// â”€â”€â”€ UNNEST â”€â”€â”€

	[Fact]
	public async Task Unnest_FlattenArrayLiteral()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST(['a', 'b', 'c']) AS val");
		rows.Should().HaveCount(3);
		rows.Select(r => r["val"]?.ToString()).Should().Contain("a").And.Contain("b").And.Contain("c");
	}

	[Fact]
	public async Task Unnest_WithOffset()
	{
		var rows = await QueryAsync("SELECT val, pos FROM UNNEST(['x', 'y', 'z']) AS val WITH OFFSET AS pos ORDER BY pos");
		rows.Should().HaveCount(3);
		rows[0]["val"]?.ToString().Should().Be("x");
		rows[2]["val"]?.ToString().Should().Be("z");
	}

	[Fact]
	public async Task Unnest_EmptyArray()
	{
		var rows = await QueryAsync("SELECT val FROM UNNEST([]) AS val");
		rows.Should().HaveCount(0);
	}

	// â”€â”€â”€ Array element access â”€â”€â”€

	[Fact]
	public async Task ArrayAccess_Offset()
	{
		var rows = await QueryAsync("SELECT [10, 20, 30][OFFSET(1)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("20");
	}

	[Fact]
	public async Task ArrayAccess_Ordinal()
	{
		var rows = await QueryAsync("SELECT [10, 20, 30][ORDINAL(2)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"]?.ToString().Should().Be("20");
	}

	[Fact]
	public async Task ArrayAccess_SafeOffset_OutOfBounds()
	{
		var rows = await QueryAsync("SELECT [10, 20][SAFE_OFFSET(10)] AS val FROM WinDummy WHERE Id = 1");
		rows[0]["val"].Should().BeNull();
	}

	// â”€â”€â”€ STRUCT â”€â”€â”€

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Struct_Constructor()
	{
		// STRUCT constructor should parse and evaluate â€” result type may be serialized as JSON/string
		var rows = await QueryAsync("SELECT STRUCT(1 AS a, 'hello' AS b) AS s FROM WinDummy WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["s"].Should().NotBeNull();
	}

}
