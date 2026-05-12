using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for query hints which should be silently ignored.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#table_hints
/// </summary>
[Collection(IntegrationCollection.Name)]
public class QueryHintIntegrationTests : IntegrationTestBase
{
	public QueryHintIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE HintTest (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT OR IGNORE INTO HintTest (Id, Name) VALUES (1, 'alpha')");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SelectWithForceIndexHint_IgnoredAndReturnsResults()
	{
		await EnsureTableAsync();
		var rows = await QueryAsync("SELECT * FROM HintTest@{FORCE_INDEX=_BASE_TABLE} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("alpha");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SelectWithStatementHint_Ignored()
	{
		await EnsureTableAsync();
		// Statement-level hints appear before SELECT with a space
		var rows = await QueryAsync("SELECT * FROM HintTest @{FORCE_JOIN_ORDER=TRUE} WHERE Id = 1");
		rows.Should().ContainSingle();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SelectWithJoinHint_Ignored()
	{
		await EnsureTableAsync();
		var rows = await QueryAsync("SELECT * FROM HintTest @{FORCE_JOIN_ORDER=TRUE} WHERE Id = 1");
		rows.Should().ContainSingle();
	}
}
