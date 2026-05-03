using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive DML tests: INSERT, UPDATE, DELETE with complex WHERE clauses,
/// subqueries, RETURNING, and edge cases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DmlExhaustiveIntegrationTests : IntegrationTestBase
{
	public DmlExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> FreshTable(string prefix)
	{
		var table = $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, Category STRING(MAX), Active BOOL) PRIMARY KEY (Id)");
		return table;
	}

	// ─── INSERT ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_SingleRow()
	{
		var t = await FreshTable("DmlIns");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val, Category, Active) VALUES (1, 'Alice', 100, 'A', TRUE)");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_MultipleRows()
	{
		var t = await FreshTable("DmlIns");
		var count = await ExecuteDmlAsync(
			$"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		count.Should().Be(3);
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_WithNull()
	{
		var t = await FreshTable("DmlIns");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, NULL)");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t}");
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_WithExpression()
	{
		var t = await FreshTable("DmlIns");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, CONCAT('hello', ' world'), 10 + 20)");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t}");
		rows[0]["Name"].Should().Be("hello world");
		rows[0]["Val"].Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_FromSelect()
	{
		var src = await FreshTable("DmlSrc");
		var dst = await FreshTable("DmlDst");
		await ExecuteDmlAsync($"INSERT INTO {src} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync($"INSERT INTO {dst} (Id, Name, Val) SELECT Id, Name, Val FROM {src}");
		count.Should().Be(2);
	}

	// ─── UPDATE ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_SingleRow()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 100)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 200 WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(200L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_MultipleColumns()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 100)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Bob', Val = 200 WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Bob");
		rows[0]["Val"].Should().Be(200L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_MultipleRows()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val, Category) VALUES (1, 'A', 10, 'X'), (2, 'B', 20, 'X'), (3, 'C', 30, 'Y')");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = Val * 2 WHERE Category = 'X'");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Category = 'X' ORDER BY Id");
		rows[0]["Val"].Should().Be(20L);
		rows[1]["Val"].Should().Be(40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_SetToNull()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 100)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = NULL WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_WithExpression()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'hello', 10)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = UPPER(Name), Val = Val + 100 WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("HELLO");
		rows[0]["Val"].Should().Be(110L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_NoMatch()
	{
		var t = await FreshTable("DmlUpd");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A')");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'B' WHERE Id = 999");
		count.Should().Be(0);
	}

	// ─── DELETE ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_SingleRow()
	{
		var t = await FreshTable("DmlDel");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A'), (2, 'B'), (3, 'C')");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 2");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_MultipleRows()
	{
		var t = await FreshTable("DmlDel");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Category) VALUES (1, 'A', 'X'), (2, 'B', 'X'), (3, 'C', 'Y')");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Category = 'X'");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_All()
	{
		var t = await FreshTable("DmlDel");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A'), (2, 'B')");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE TRUE");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_NoMatch()
	{
		var t = await FreshTable("DmlDel");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A')");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 999");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_WithSubquery()
	{
		var t = await FreshTable("DmlDel");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Val IN (SELECT Val FROM {t} WHERE Val > 15)");
		count.Should().Be(2);
	}

	// ─── DML with parameters ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_WithParameter()
	{
		var t = await FreshTable("DmlPar");
		var count = await ExecuteDmlAsync(
			$"INSERT INTO {t} (Id, Name, Val) VALUES (@id, @name, @val)",
			("id", SpannerDbType.Int64, (object?)1L),
			("name", SpannerDbType.String, "Alice"),
			("val", SpannerDbType.Int64, 100L));
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_WithParameter()
	{
		var t = await FreshTable("DmlPar");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'old')");
		var count = await ExecuteDmlAsync(
			$"UPDATE {t} SET Name = @name WHERE Id = @id",
			("name", SpannerDbType.String, (object?)"new"),
			("id", SpannerDbType.Int64, 1L));
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("new");
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_WithParameter()
	{
		var t = await FreshTable("DmlPar");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'A'), (2, 'B')");
		var count = await ExecuteDmlAsync(
			$"DELETE FROM {t} WHERE Id = @id",
			("id", SpannerDbType.Int64, (object?)1L));
		count.Should().Be(1);
	}

	// ─── DML with LIKE in WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Delete_WithLike()
	{
		var t = await FreshTable("DmlLike");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'test_item'), (2, 'other'), (3, 'test_thing')");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Name LIKE 'test%'");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_WithBetween()
	{
		var t = await FreshTable("DmlBtw");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 5), (2, 15), (3, 25)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 0 WHERE Val BETWEEN 10 AND 20");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 2");
		rows[0]["Val"].Should().Be(0L);
	}

	// ─── Verify data integrity after DML ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task InsertUpdateDelete_Sequence()
	{
		var t = await FreshTable("DmlSeq");
		// Insert
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Start', 100)");
		// Update
		await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Updated', Val = 200 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Updated");
		rows[0]["Val"].Should().Be(200L);
		// Delete
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		var rows2 = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows2[0]["C"].Should().Be(0L);
	}

	// ─── UPDATE with CASE ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Update_WithCase()
	{
		var t = await FreshTable("DmlCase");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val, Category) VALUES (1, 10, 'A'), (2, 20, 'B'), (3, 30, 'A')");
		var count = await ExecuteDmlAsync(
			$"UPDATE {t} SET Val = CASE WHEN Category = 'A' THEN Val * 2 ELSE Val * 3 END WHERE TRUE");
		count.Should().Be(3);
		var rows = await QueryAsync($"SELECT Id, Val FROM {t} ORDER BY Id");
		rows[0]["Val"].Should().Be(20L);
		rows[1]["Val"].Should().Be(60L);
		rows[2]["Val"].Should().Be(60L);
	}

	// ─── INSERT with DEFAULT ───
	[Fact]
	[Trait(TestTraits.Category, "DmlExhaustive")]
	public async Task Insert_PartialColumns()
	{
		var t = await FreshTable("DmlDef");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id) VALUES (1)");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}
}
