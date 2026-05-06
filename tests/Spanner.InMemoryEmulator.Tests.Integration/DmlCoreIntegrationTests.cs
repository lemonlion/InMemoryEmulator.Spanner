using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for DML statements: INSERT, UPDATE, DELETE with various patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DmlCoreIntegrationTests : IntegrationTestBase
{
	public DmlCoreIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── INSERT ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_BasicRow()
	{
		var table = "DcIns1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync($"INSERT INTO {table} (Id, Name) VALUES (1, 'Alice')");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Id, Name FROM {table}");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_WithNullValue()
	{
		var table = "DcIns2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync($"INSERT INTO {table} (Id, Name) VALUES (1, NULL)");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name FROM {table}");
		rows[0]["Name"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_MultipleRows()
	{
		var table = "DcIns3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync($"INSERT INTO {table} (Id, Name) VALUES (1, 'Alice'), (2, 'Bob'), (3, 'Charlie')");
		count.Should().Be(3);

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {table}");
		rows[0]["C"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_WithParameters()
	{
		var table = "DcIns4";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync(
			$"INSERT INTO {table} (Id, Name) VALUES (@id, @name)",
			("id", SpannerDbType.Int64, (object?)1L),
			("name", SpannerDbType.String, (object?)"Alice"));
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name FROM {table}");
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_AllDataTypes()
	{
		var table = "DcIns5";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Str STRING(MAX), Flt FLOAT64, Bl BOOL, Dt DATE, Ts TIMESTAMP) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync($"INSERT INTO {table} (Id, Str, Flt, Bl, Dt, Ts) VALUES (1, 'test', 3.14, true, DATE('2024-01-15'), TIMESTAMP('2024-01-15T10:00:00Z'))");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT * FROM {table}");
		rows[0]["Id"].Should().Be(1L);
		rows[0]["Str"].Should().Be("test");
		((double)rows[0]["Flt"]!).Should().BeApproximately(3.14, 0.001);
		rows[0]["Bl"].Should().Be(true);
	}

	// ─── UPDATE ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_SingleRow()
	{
		var table = "DcUpd1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Name = 'Bob' WHERE Id = 1");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name FROM {table}");
		rows[0]["Name"].Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_MultipleRows()
	{
		var table = "DcUpd2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Status STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Status"] = "active" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Status"] = "active" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Status"] = "inactive" });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Status = 'closed' WHERE Status = 'active'");
		count.Should().Be(2);

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {table} WHERE Status = 'closed'");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_SetToNull()
	{
		var table = "DcUpd3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Name = NULL WHERE Id = 1");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name FROM {table}");
		rows[0]["Name"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_SetCalculatedValue()
	{
		var table = "DcUpd4";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Val = Val * 2 WHERE true");
		count.Should().Be(2);

		var rows = await QueryAsync($"SELECT Val FROM {table} ORDER BY Id");
		rows[0]["Val"].Should().Be(20L);
		rows[1]["Val"].Should().Be(40L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_NoMatch_ReturnsZero()
	{
		var table = "DcUpd5";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Name = 'Bob' WHERE Id = 999");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_WithParameters()
	{
		var table = "DcUpd6";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });

		var count = await ExecuteDmlAsync(
			$"UPDATE {table} SET Name = @name WHERE Id = @id",
			("name", SpannerDbType.String, (object?)"Bob"),
			("id", SpannerDbType.Int64, (object?)1L));
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name FROM {table}");
		rows[0]["Name"].Should().Be("Bob");
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_MultipleColumns()
	{
		var table = "DcUpd7";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Age INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Age"] = 25L });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Name = 'Bob', Age = 30 WHERE Id = 1");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT Name, Age FROM {table}");
		rows[0]["Name"].Should().Be("Bob");
		rows[0]["Age"].Should().Be(30L);
	}

	// ─── DELETE ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_SingleRow()
	{
		var table = "DcDel1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		var count = await ExecuteDmlAsync($"DELETE FROM {table} WHERE Id = 1");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {table}");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_MultipleRows()
	{
		var table = "DcDel2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Status STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Status"] = "old" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Status"] = "old" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Status"] = "new" });

		var count = await ExecuteDmlAsync($"DELETE FROM {table} WHERE Status = 'old'");
		count.Should().Be(2);

		var rows = await QueryAsync($"SELECT Id FROM {table}");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_AllRows()
	{
		var table = "DcDel3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L });

		var count = await ExecuteDmlAsync($"DELETE FROM {table} WHERE true");
		count.Should().Be(3);

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {table}");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_NoMatch_ReturnsZero()
	{
		var table = "DcDel4";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L });

		var count = await ExecuteDmlAsync($"DELETE FROM {table} WHERE Id = 999");
		count.Should().Be(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_WithParameters()
	{
		var table = "DcDel5";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L }, new Dictionary<string, object?> { ["Id"] = 2L });

		var count = await ExecuteDmlAsync(
			$"DELETE FROM {table} WHERE Id = @id",
			("id", SpannerDbType.Int64, (object?)1L));
		count.Should().Be(1);
	}

	// ─── INSERT with SELECT ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_FromSelect()
	{
		var src = "DcInsSrc";
		var dst = "DcInsDst";
		await ExecuteDdlAsync(
			$"CREATE TABLE {src} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)",
			$"CREATE TABLE {dst} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(src,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob" });

		var count = await ExecuteDmlAsync($"INSERT INTO {dst} (Id, Name) SELECT Id, Name FROM {src}");
		count.Should().Be(2);

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {dst}");
		rows[0]["C"].Should().Be(2L);
	}

	// ─── UPDATE with subquery ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_WithSubquery()
	{
		var table = "DcUpdSub";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET Val = Val + 100 WHERE Val > (SELECT AVG(Val) FROM {table})");
		count.Should().Be(1); // Only row with Val=30

		var rows = await QueryAsync($"SELECT Val FROM {table} WHERE Id = 3");
		rows[0]["Val"].Should().Be(130L);
	}

	// ─── DELETE with subquery ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task DeleteDml_WithSubquery()
	{
		var table = "DcDelSub";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 50L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var count = await ExecuteDmlAsync($"DELETE FROM {table} WHERE Val < (SELECT AVG(Val) FROM {table})");
		count.Should().Be(1); // Only row with Val=10

		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {table}");
		rows[0]["C"].Should().Be(2L);
	}

	// ─── INSERT with expressions ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task InsertDml_WithExpressions()
	{
		var table = "DcInsExpr";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), NameLen INT64) PRIMARY KEY (Id)");
		var count = await ExecuteDmlAsync($"INSERT INTO {table} (Id, Name, NameLen) VALUES (1, 'Alice', LENGTH('Alice'))");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT NameLen FROM {table}");
		rows[0]["NameLen"].Should().Be(5L);
	}

	// ─── UPDATE multiple columns from expression ───

	[Fact]
	[Trait(TestTraits.Category, "DML")]
	public async Task UpdateDml_SetFromExpression()
	{
		var table = "DcUpdExpr";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), UpperName STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "hello" });

		var count = await ExecuteDmlAsync($"UPDATE {table} SET UpperName = UPPER(Name) WHERE true");
		count.Should().Be(1);

		var rows = await QueryAsync($"SELECT UpperName FROM {table}");
		rows[0]["UpperName"].Should().Be("HELLO");
	}

	// ═══════════════════════════════════════════════════════════════
	// INSERT OR UPDATE (DML)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_or_update
	//   "Columns that aren't specified in the INSERT statement are not modified."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InsertOrUpdate_ExistingRow_OnlyUpdatesSpecifiedColumns()
	{
		var table = "DcInsOrUpd1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Score"] = 100L });

		// INSERT OR UPDATE specifying only Id and Name — Score should remain unchanged
		await ExecuteDmlAsync($"INSERT OR UPDATE INTO {table} (Id, Name) VALUES (1, 'Bob')");

		var rows = await QueryAsync($"SELECT Name, Score FROM {table} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Bob");
		rows[0]["Score"].Should().Be(100L); // Score should NOT be null
	}

	[Fact]
	public async Task InsertOrUpdate_NewRow_InsertsWithNullsForMissing()
	{
		var table = "DcInsOrUpd2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");

		// INSERT OR UPDATE with a new row — unspecified columns should be NULL
		await ExecuteDmlAsync($"INSERT OR UPDATE INTO {table} (Id, Name) VALUES (1, 'Alice')");

		var rows = await QueryAsync($"SELECT Name, Score FROM {table} WHERE Id = 1");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("Alice");
		rows[0]["Score"].Should().BeNull();
	}
}
