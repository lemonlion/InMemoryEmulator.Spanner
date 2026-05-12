using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive mutation tests: INSERT, INSERT_OR_UPDATE via Spanner mutations API.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MutationExhaustiveIntegrationTests : IntegrationTestBase
{
	public MutationExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> FreshTable(string prefix)
	{
		var t = $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");
		return t;
	}

	// ─── INSERT mutation ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_SingleRow()
	{
		var t = await FreshTable("MutIns");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 100L });
		var rows = await QueryAsync($"SELECT Name, Val FROM {t}");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Val"].Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_WithNull()
	{
		var t = await FreshTable("MutIns");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = null, ["Val"] = null });
		var rows = await QueryAsync($"SELECT Name, Val FROM {t}");
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_MultipleRows()
	{
		var t = await FreshTable("MutIns");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A" });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "B" });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "C" });
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(3L);
	}

	// ─── INSERT_OR_UPDATE mutation ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertOrUpdateMutation_NewRow()
	{
		var t = await FreshTable("MutIou");
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertOrUpdateMutation_ExistingRow()
	{
		var t = await FreshTable("MutIou");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "old" });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "new" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("new");
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertOrUpdateMutation_PartialUpdate()
	{
		var t = await FreshTable("MutIou");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Val"] = 100L });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Bob" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Bob");
	}

	// ─── Multiple data types via mutations ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_BoolColumn()
	{
		var t = $"MutBool_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = false });
		var rows = await QueryAsync($"SELECT Flag FROM {t} ORDER BY Id");
		rows[0]["Flag"].Should().Be(true);
		rows[1]["Flag"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_Float64Column()
	{
		var t = $"MutF64_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val FLOAT64) PRIMARY KEY (Id)");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 3.14 });
		var rows = await QueryAsync($"SELECT Val FROM {t}");
		((double)rows[0]["Val"]!).Should().BeApproximately(3.14, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_DateColumn()
	{
		var t = $"MutDate_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, D DATE) PRIMARY KEY (Id)");
		var d = new DateTime(2024, 6, 15);
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["D"] = d });
		var rows = await QueryAsync($"SELECT D FROM {t}");
		((DateTime)rows[0]["D"]!).Date.Should().Be(d.Date);
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_TimestampColumn()
	{
		var t = $"MutTs_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Ts TIMESTAMP) PRIMARY KEY (Id)");
		var ts = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Ts"] = ts });
		var rows = await QueryAsync($"SELECT Ts FROM {t}");
		rows[0]["Ts"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_BytesColumn()
	{
		var t = $"MutBytes_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Data BYTES(MAX)) PRIMARY KEY (Id)");
		var data = new byte[] { 1, 2, 3, 4, 5 };
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Data"] = data });
		var rows = await QueryAsync($"SELECT Data FROM {t}");
		rows[0]["Data"].Should().NotBeNull();
	}

	// ─── Mutation with composite key ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_CompositeKey()
	{
		var t = $"MutComp_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K1 INT64 NOT NULL, K2 STRING(MAX) NOT NULL, Val INT64) PRIMARY KEY (K1, K2)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K1"] = 1L, ["K2"] = "a", ["Val"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K1"] = 1L, ["K2"] = "b", ["Val"] = 20L });
		var rows = await QueryAsync($"SELECT Val FROM {t} ORDER BY K2");
		rows[0]["Val"].Should().Be(10L);
		rows[1]["Val"].Should().Be(20L);
	}

	// ─── Verify mutation sees its own writes ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_ThenQuery()
	{
		var t = await FreshTable("MutTQ");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "test" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("test");
	}

	// ─── Large batch ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertMutation_MultipleBatch()
	{
		var t = await FreshTable("MutBatch");
		for (int i = 1; i <= 20; i++)
		{
			await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = (long)i, ["Name"] = $"Row{i}", ["Val"] = (long)(i * 10) });
		}
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(20L);
	}

	// ─── InsertOrUpdate new row then update again ───
	[Fact]
	[Trait(TestTraits.Category, "MutationExhaustive")]
	public async Task InsertOrUpdate_MultipleUpserts()
	{
		var t = await FreshTable("MutMulti");
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "first", ["Val"] = 10L });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "second", ["Val"] = 20L });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "third", ["Val"] = 30L });
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("third");
		rows[0]["Val"].Should().Be(30L);
	}
}
