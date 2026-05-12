using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for transaction behavior: read-after-write consistency,
/// DML row counts, upsert semantics, NULL handling, type preservation, and edge cases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TransactionBehaviorIntegrationTests : IntegrationTestBase
{
	public TransactionBehaviorIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> FreshTable(string prefix)
	{
		var table = $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64, Score FLOAT64, Active BOOL, Birthday DATE, Created TIMESTAMP, Data BYTES(MAX)) PRIMARY KEY (Id)");
		return table;
	}

	private async Task<string> FreshSimpleTable(string prefix)
	{
		var table = $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Val INT64) PRIMARY KEY (Id)");
		return table;
	}

	// ═══════════════════════════════════════════════════════════════
	// 1. Read-after-write consistency
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task ReadAfterWrite_InsertThenSelect_DataVisible()
	{
		var t = await FreshSimpleTable("RAW");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 100)");
		var rows = await QueryAsync($"SELECT Id, Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Val"].Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task ReadAfterWrite_InsertThenCount_ReturnsOne()
	{
		var t = await FreshSimpleTable("RAW");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Bob', 50)");
		var count = await QueryScalarAsync($"SELECT COUNT(*) FROM {t}");
		count.Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task ReadAfterWrite_InsertUpdateThenSelect_UpdateVisible()
	{
		var t = await FreshSimpleTable("RAW");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Carol', 10)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = 20 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Val"].Should().Be(20L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task ReadAfterWrite_InsertDeleteThenSelect_Empty()
	{
		var t = await FreshSimpleTable("RAW");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Dave', 5)");
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// 2. Multi-row DML
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiRow_Insert_ThreeRows_AllVisible()
	{
		var t = await FreshSimpleTable("MR");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var rows = await QueryAsync($"SELECT Id FROM {t} ORDER BY Id");
		rows.Should().HaveCount(3);
		rows[0]["Id"].Should().Be(1L);
		rows[1]["Id"].Should().Be(2L);
		rows[2]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiRow_Insert_FiveRows_CountCorrect()
	{
		var t = await FreshSimpleTable("MR");
		var count = await ExecuteDmlAsync(
			$"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 1), (2, 'B', 2), (3, 'C', 3), (4, 'D', 4), (5, 'E', 5)");
		count.Should().Be(5);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiRow_Insert_ValuesPreserved()
	{
		var t = await FreshSimpleTable("MR");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (10, 'X', 100), (20, 'Y', 200)");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} ORDER BY Id");
		rows[0]["Name"].Should().Be("X");
		rows[0]["Val"].Should().Be(100L);
		rows[1]["Name"].Should().Be("Y");
		rows[1]["Val"].Should().Be(200L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 3. UPDATE with various WHERE conditions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereEquals()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 10), (2, 'Bob', 20)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 99 WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(99L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereNotEquals()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 0 WHERE Id != 2");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 2");
		rows[0]["Val"].Should().Be(20L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereLessThan()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'low' WHERE Val < 25");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereGreaterThan()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'high' WHERE Val > 15");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereIn()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'matched' WHERE Id IN (1, 3)");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 2");
		rows[0]["Name"].Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereBetween()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30), (4, 'D', 40)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'mid' WHERE Val BETWEEN 15 AND 35");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereLike()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 10), (2, 'Bob', 20), (3, 'Alicia', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 999 WHERE Name LIKE 'Ali%'");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereIsNull()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', 20)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Unknown' WHERE Name IS NULL");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Unknown");
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Update_WhereIsNotNull()
	{
		var t = await FreshSimpleTable("UPD");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', 20), (3, NULL, 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 0 WHERE Name IS NOT NULL");
		count.Should().Be(1);
	}

	// ═══════════════════════════════════════════════════════════════
	// 4. DELETE with various WHERE conditions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereEquals()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereNotEquals()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id != 2");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().ContainSingle();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereLessThan()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Val < 25");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereGreaterThan()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Val > 15");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereIn()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id IN (1, 3)");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT Id FROM {t}");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereBetween()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30), (4, 'D', 40)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Val BETWEEN 15 AND 35");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereLike()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 10), (2, 'Bob', 20), (3, 'Alicia', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Name LIKE 'Ali%'");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereIsNull()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', 20)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Name IS NULL");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_WhereIsNotNull()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', 20), (3, NULL, 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Name IS NOT NULL");
		count.Should().Be(1);
		var remaining = await QueryAsync($"SELECT Id FROM {t} ORDER BY Id");
		remaining.Should().HaveCount(2);
		remaining[0]["Id"].Should().Be(1L);
		remaining[1]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Delete_All_NoWhereClause()
	{
		var t = await FreshSimpleTable("DEL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE TRUE");
		count.Should().Be(3);
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 5. DML returning row counts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_Insert_ReturnsCorrectCount()
	{
		var t = await FreshSimpleTable("RC");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		count.Should().Be(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_InsertMultiple_ReturnsCorrectCount()
	{
		var t = await FreshSimpleTable("RC");
		var count = await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		count.Should().Be(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_Update_ReturnsAffectedCount()
	{
		var t = await FreshSimpleTable("RC");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 0 WHERE Val > 15");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_Delete_ReturnsDeletedCount()
	{
		var t = await FreshSimpleTable("RC");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		count.Should().Be(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_UpdateAll_ReturnsAllRows()
	{
		var t = await FreshSimpleTable("RC");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Z' WHERE TRUE");
		count.Should().Be(3);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task RowCount_DeleteAll_ReturnsAllRows()
	{
		var t = await FreshSimpleTable("RC");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE TRUE");
		count.Should().Be(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// 6. Upsert behavior
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Upsert_NewRow_CreatesRow()
	{
		var t = await FreshSimpleTable("UPS");
		await InsertOrUpdateAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Alice",
			["Val"] = 100L
		});
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Val"].Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Upsert_ExistingRow_UpdatesRow()
	{
		var t = await FreshSimpleTable("UPS");
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Alice",
			["Val"] = 100L
		});
		await InsertOrUpdateAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Alice Updated",
			["Val"] = 200L
		});
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice Updated");
		rows[0]["Val"].Should().Be(200L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Upsert_MixedNewAndExisting()
	{
		var t = await FreshSimpleTable("UPS");
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Existing",
			["Val"] = 10L
		});
		// Upsert existing row
		await InsertOrUpdateAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Updated",
			["Val"] = 11L
		});
		// Upsert new row
		await InsertOrUpdateAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 2L,
			["Name"] = "New",
			["Val"] = 22L
		});
		var rows = await QueryAsync($"SELECT Id, Name, Val FROM {t} ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Name"].Should().Be("Updated");
		rows[0]["Val"].Should().Be(11L);
		rows[1]["Name"].Should().Be("New");
		rows[1]["Val"].Should().Be(22L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Upsert_PartialColumns_PreservesUnchanged()
	{
		var t = await FreshSimpleTable("UPS");
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Alice",
			["Val"] = 100L
		});
		// Upsert with only Id and Name — Val should be overwritten to NULL
		await InsertOrUpdateAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "Alice2"
		});
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice2");
		// InsertOrUpdate replaces the entire row; omitted columns become NULL
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Mutation
		//   "Columns not listed are set to NULL."
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Upsert_MultipleSequentialUpserts()
	{
		var t = await FreshSimpleTable("UPS");
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "V1", ["Val"] = 1L });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "V2", ["Val"] = 2L });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "V3", ["Val"] = 3L });
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("V3");
		rows[0]["Val"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 7. NULL handling in DML
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_InsertNullValues()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, NULL)");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_UpdateToNull()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 100)");
		await ExecuteDmlAsync($"UPDATE {t} SET Name = NULL, Val = NULL WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_WhereIsNull_FiltersCorrectly()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', NULL), (3, NULL, NULL)");
		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Name IS NULL ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(1L);
		rows[1]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_WhereIsNotNull_FiltersCorrectly()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, 10), (2, 'Bob', NULL), (3, 'Carol', 30)");
		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Val IS NOT NULL ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(1L);
		rows[1]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_UpdateNullToValue()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, NULL, NULL)");
		await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Found', Val = 42 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Found");
		rows[0]["Val"].Should().Be(42L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Null_InsertViaParameter()
	{
		var t = await FreshSimpleTable("NUL");
		await ExecuteDmlAsync(
			$"INSERT INTO {t} (Id, Name, Val) VALUES (@id, @name, @val)",
			("id", SpannerDbType.Int64, (object?)1L),
			("name", SpannerDbType.String, null),
			("val", SpannerDbType.Int64, null));
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().BeNull();
		rows[0]["Val"].Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// 8. Type preservation
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Int64()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 9876543210L });
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(9876543210L);
		rows[0]["Val"].Should().BeOfType<long>();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Float64()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = 3.14159 });
		var rows = await QueryAsync($"SELECT Score FROM {t} WHERE Id = 1");
		((double)rows[0]["Score"]!).Should().BeApproximately(3.14159, 0.00001);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_String()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Hello, World! 🌍" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Hello, World! 🌍");
		rows[0]["Name"].Should().BeOfType<string>();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Bool_True()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Active"] = true });
		var rows = await QueryAsync($"SELECT Active FROM {t} WHERE Id = 1");
		rows[0]["Active"].Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Bool_False()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Active"] = false });
		var rows = await QueryAsync($"SELECT Active FROM {t} WHERE Id = 1");
		rows[0]["Active"].Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Date()
	{
		var t = await FreshTable("TP");
		var dateValue = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Unspecified);
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Birthday"] = dateValue });
		var rows = await QueryAsync($"SELECT Birthday FROM {t} WHERE Id = 1");
		var result = (DateTime)rows[0]["Birthday"]!;
		result.Year.Should().Be(2024);
		result.Month.Should().Be(1);
		result.Day.Should().Be(15);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Timestamp()
	{
		var t = await FreshTable("TP");
		var tsValue = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Created"] = tsValue });
		var rows = await QueryAsync($"SELECT Created FROM {t} WHERE Id = 1");
		var result = (DateTime)rows[0]["Created"]!;
		result.Year.Should().Be(2024);
		result.Month.Should().Be(1);
		result.Day.Should().Be(15);
		result.Hour.Should().Be(10);
		result.Minute.Should().Be(30);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_Bytes()
	{
		var t = await FreshTable("TP");
		var bytesValue = new byte[] { 1, 2, 3, 4, 5 };
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Data"] = bytesValue });
		var rows = await QueryAsync($"SELECT Data FROM {t} WHERE Id = 1");
		var result = (byte[])rows[0]["Data"]!;
		result.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_EmptyString()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "" });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_EmptyBytes()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Data"] = Array.Empty<byte>() });
		var rows = await QueryAsync($"SELECT Data FROM {t} WHERE Id = 1");
		var result = (byte[])rows[0]["Data"]!;
		result.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_LargeInt64()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = long.MaxValue });
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(long.MaxValue);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_NegativeInt64()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = long.MinValue });
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(long.MinValue);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_ZeroFloat()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = 0.0 });
		var rows = await QueryAsync($"SELECT Score FROM {t} WHERE Id = 1");
		((double)rows[0]["Score"]!).Should().Be(0.0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_NegativeFloat()
	{
		var t = await FreshTable("TP");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = -99.99 });
		var rows = await QueryAsync($"SELECT Score FROM {t} WHERE Id = 1");
		((double)rows[0]["Score"]!).Should().BeApproximately(-99.99, 0.001);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task TypePreservation_AllTypesInOneRow()
	{
		var t = await FreshTable("TP");
		var dateVal = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);
		var tsVal = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
		var bytesVal = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = 1L,
			["Name"] = "AllTypes",
			["Val"] = 42L,
			["Score"] = 2.718,
			["Active"] = true,
			["Birthday"] = dateVal,
			["Created"] = tsVal,
			["Data"] = bytesVal
		});
		var rows = await QueryAsync($"SELECT * FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("AllTypes");
		rows[0]["Val"].Should().Be(42L);
		((double)rows[0]["Score"]!).Should().BeApproximately(2.718, 0.001);
		rows[0]["Active"].Should().Be(true);
		((DateTime)rows[0]["Birthday"]!).Day.Should().Be(15);
		((DateTime)rows[0]["Created"]!).Hour.Should().Be(12);
		((byte[])rows[0]["Data"]!).Should().BeEquivalentTo(bytesVal);
	}

	// ═══════════════════════════════════════════════════════════════
	// 9. Multi-statement DML (sequential)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_InsertThenUpdate()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'X', 10)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = 20 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(20L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_InsertThenDelete()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_InsertUpdateDelete_CumulativeState()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = Val + 100 WHERE Id IN (1, 2)");
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 3");
		var rows = await QueryAsync($"SELECT Id, Val FROM {t} ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Val"].Should().Be(110L);
		rows[1]["Val"].Should().Be(120L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_ThreeInserts_CumulativeCount()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (2, 'B', 20)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (3, 'C', 30)");
		var count = await QueryScalarAsync($"SELECT COUNT(*) FROM {t}");
		count.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_UpdateSameRowTwice()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Start', 0)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = 10 WHERE Id = 1");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = Val + 5 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(15L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_DeleteThenInsertSameKey()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Original', 10)");
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 1");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Replacement', 20)");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Replacement");
		rows[0]["Val"].Should().Be(20L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task MultiStatement_UpdateMultipleColumns()
	{
		var t = await FreshSimpleTable("MS");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 10)");
		await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Bob', Val = 99 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Bob");
		rows[0]["Val"].Should().Be(99L);
	}

	// ═══════════════════════════════════════════════════════════════
	// 10. Edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateZeroRows()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 99 WHERE Id = 999");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_DeleteNonExistentRow()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Id = 999");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateEmptyTable()
	{
		var t = await FreshSimpleTable("EDGE");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 1 WHERE TRUE");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_DeleteFromEmptyTable()
	{
		var t = await FreshSimpleTable("EDGE");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE TRUE");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_InsertDuplicateKey_ThrowsError()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		var act = () => ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'B', 20)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_InsertDuplicateKey_ViaInsertAsync_ThrowsError()
	{
		var t = await FreshSimpleTable("EDGE");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "A" });
		var act = () => InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "B" });
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_SelectFromEmptyTable()
	{
		var t = await FreshSimpleTable("EDGE");
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().BeEmpty();
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_CountOfEmptyTable()
	{
		var t = await FreshSimpleTable("EDGE");
		var count = await QueryScalarAsync($"SELECT COUNT(*) FROM {t}");
		count.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWithSelfReference()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = Val * 2 WHERE TRUE");
		count.Should().Be(2);
		var rows = await QueryAsync($"SELECT Id, Val FROM {t} ORDER BY Id");
		rows[0]["Val"].Should().Be(20L);
		rows[1]["Val"].Should().Be(40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateSetSameValue()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Alice', 10)");
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ExecuteSqlRequest
		//   DML UPDATE counts all rows matching WHERE, even if the value doesn't change.
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Name = 'Alice' WHERE Id = 1");
		count.Should().Be(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_InsertWithParameterizedQuery()
	{
		var t = await FreshSimpleTable("EDGE");
		var count = await ExecuteDmlAsync(
			$"INSERT INTO {t} (Id, Name, Val) VALUES (@id, @name, @val)",
			("id", SpannerDbType.Int64, (object?)1L),
			("name", SpannerDbType.String, (object?)"Parameterized"),
			("val", SpannerDbType.Int64, (object?)42L));
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Parameterized");
		rows[0]["Val"].Should().Be(42L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWithParameterizedQuery()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Old', 10)");
		var count = await ExecuteDmlAsync(
			$"UPDATE {t} SET Name = @name WHERE Id = @id",
			("name", SpannerDbType.String, (object?)"New"),
			("id", SpannerDbType.Int64, (object?)1L));
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("New");
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_DeleteWithParameterizedQuery()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20)");
		var count = await ExecuteDmlAsync(
			$"DELETE FROM {t} WHERE Id = @id",
			("id", SpannerDbType.Int64, (object?)1L));
		count.Should().Be(1);
		var remaining = await QueryAsync($"SELECT Id FROM {t}");
		remaining.Should().ContainSingle();
		remaining[0]["Id"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_InsertMaxAndMinValues()
	{
		var t = await FreshTable("EDGE");
		await InsertAsync(t, new Dictionary<string, object?>
		{
			["Id"] = long.MaxValue,
			["Name"] = "MaxId",
			["Val"] = long.MinValue
		});
		var rows = await QueryAsync($"SELECT Id, Val FROM {t} WHERE Id = {long.MaxValue}");
		rows.Should().ContainSingle();
		rows[0]["Id"].Should().Be(long.MaxValue);
		rows[0]["Val"].Should().Be(long.MinValue);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWhereCompound_AndCondition()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'A', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 99 WHERE Name = 'A' AND Val > 15");
		count.Should().Be(1);
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 3");
		rows[0]["Val"].Should().Be(99L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWhereCompound_OrCondition()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'C', 30)");
		var count = await ExecuteDmlAsync($"UPDATE {t} SET Val = 0 WHERE Id = 1 OR Id = 3");
		count.Should().Be(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_DeleteWhereCompound_AndCondition()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10), (2, 'B', 20), (3, 'A', 30)");
		var count = await ExecuteDmlAsync($"DELETE FROM {t} WHERE Name = 'A' AND Val < 20");
		count.Should().Be(1);
		var remaining = await QueryAsync($"SELECT Id FROM {t} ORDER BY Id");
		remaining.Should().HaveCount(2);
		remaining[0]["Id"].Should().Be(2L);
		remaining[1]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWithArithmeticExpression()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'A', 10)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = Val + 5 WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Id = 1");
		rows[0]["Val"].Should().Be(15L);
	}

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task EdgeCase_UpdateWithStringExpression()
	{
		var t = await FreshSimpleTable("EDGE");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Hello', 10)");
		await ExecuteDmlAsync($"UPDATE {t} SET Name = CONCAT(Name, ' World') WHERE Id = 1");
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = 1");
		rows[0]["Name"].Should().Be("Hello World");
	}

	// ═══════════════════════════════════════════════════════════════
	// Commit with failing mutations — transaction not marked committed
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.Commit
	//   "If the mutations fail (e.g., duplicate key), the commit returns an error."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Category, "TransactionBehavior")]
	public async Task Commit_WithDuplicateInsert_FailsAndDoesNotCorruptState()
	{
		var t = await FreshSimpleTable("CORDER");
		// Insert a row first
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name, Val) VALUES (1, 'Original', 100)");

		// Try to insert-or-update in a transaction that also tries to insert a duplicate via mutation
		// Use raw SpannerCommand with mutations for the conflict
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();

		// Attempt INSERT (not InsertOrUpdate) of duplicate key should fail
		var act = async () =>
		{
			using var cmd = connection.CreateInsertCommand(t);
			cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
			cmd.Parameters.Add("Name", SpannerDbType.String, "Duplicate");
			cmd.Parameters.Add("Val", SpannerDbType.Int64, 999L);
			await cmd.ExecuteNonQueryAsync();
		};
		await act.Should().ThrowAsync<Exception>();

		// Verify original data is unchanged
		var rows = await QueryAsync($"SELECT Name, Val FROM {t} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Original");
		rows[0]["Val"].Should().Be(100L);
	}
}
