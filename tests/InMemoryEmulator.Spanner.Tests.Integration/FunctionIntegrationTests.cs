using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for Phase 12: Core SQL Functions.
/// Tests flow through the full gRPC pipeline: SpannerConnection â†’ gRPC â†’ FakeSpannerService.
/// Each test uses a dummy table with one row to satisfy the FROM clause requirement.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FunctionIntegrationTests : IntegrationTestBase
{
private bool _initialized;

public FunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		if (_initialized) return;
		try
		{
			await ExecuteDdlAsync("CREATE TABLE FnDummy (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await InsertAsync("FnDummy", new Dictionary<string, object?> { ["Id"] = 1L });
		}
		catch { /* table may already exist from another test run */ }
		_initialized = true;
	}

	private async Task<object?> ScalarAsync(string selectExpr)
	{
		await EnsureTableAsync();
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {selectExpr} AS R FROM FnDummy WHERE Id=1");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	[Fact] public async Task JsonQuery() => (await ScalarAsync("JSON_QUERY('{\"a\":{\"b\":2}}', '$.a')")).Should().Be("{\"b\":2}");

	[Fact] public async Task JsonType() => (await ScalarAsync("JSON_TYPE(PARSE_JSON('{\"a\":1}'))")).Should().Be("object");

	// â”€â”€â”€ NULL PROPAGATION â”€â”€â”€

	[Fact] public async Task NullPropagation_Length() => (await ScalarAsync("LENGTH(NULL)")).Should().BeNull();

	[Fact] public async Task NullPropagation_Abs() => (await ScalarAsync("ABS(NULL)")).Should().BeNull();

	[Fact] public async Task NullPropagation_Lower() => (await ScalarAsync("LOWER(NULL)")).Should().BeNull();
}
