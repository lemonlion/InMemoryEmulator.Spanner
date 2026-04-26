using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Comprehensive edge-case tests for DML operations (INSERT, UPDATE, DELETE),
/// mutation operations, and constraint enforcement.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DmlExtendedIntegrationTests : IntegrationTestBase
{
	public DmlExtendedIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task EnsureDmlTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlTest (Id INT64 NOT NULL, Name STRING(100), Val INT64, Active BOOL) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task EnsureNotNullTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlNotNull (Id INT64 NOT NULL, Required STRING(100) NOT NULL, Optional STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task EnsureCheckTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlCheck (Id INT64 NOT NULL, Val INT64, CONSTRAINT CK_Val CHECK (Val > 0)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task EnsureFkTablesAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlParent (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE DmlChild (Id INT64 NOT NULL, ParentId INT64 NOT NULL, CONSTRAINT FK_Parent FOREIGN KEY (ParentId) REFERENCES DmlParent(Id)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	private async Task EnsureUniqueTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlUnique (Id INT64 NOT NULL, Code STRING(50)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE UNIQUE INDEX IX_DmlUnique_Code ON DmlUnique(Code)");
		}
		catch { }
	}

	private async Task EnsureLenTableAsync()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DmlLen (Id INT64 NOT NULL, Short STRING(5)) PRIMARY KEY (Id)");
		}
		catch { }
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// INSERT DML
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task InsertDml_BasicInsert()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync(
			"INSERT INTO DmlTest (Id, Name, Val, Active) VALUES (100, 'test', 42, true)");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Name FROM DmlTest WHERE Id = 100");
		rows.Should().ContainSingle().Which["Name"].Should().Be("test");
	}

	[Fact]
	public async Task InsertDml_MultipleRows()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync(
			"INSERT INTO DmlTest (Id, Name, Val) VALUES (201, 'a', 1), (202, 'b', 2), (203, 'c', 3)");
		count.Should().Be(3);

		var rows = await QueryAsync("SELECT Name FROM DmlTest WHERE Id IN (201, 202, 203) ORDER BY Name");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task InsertDml_NullableColumn()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync(
			"INSERT INTO DmlTest (Id, Name) VALUES (300, 'nullable')");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Val FROM DmlTest WHERE Id = 300");
		rows.Should().ContainSingle().Which["Val"].Should().BeNull();
	}

	[Fact]
	public async Task InsertDml_WithParameters()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync(
			"INSERT INTO DmlTest (Id, Name, Val) VALUES (@id, @name, @val)",
			("id", SpannerDbType.Int64, (object?)400L),
			("name", SpannerDbType.String, "param_test"),
			("val", SpannerDbType.Int64, 99L));
		count.Should().Be(1);
	}

	[Fact]
	public async Task InsertDml_DuplicateKey_Throws()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (500, 'first')"); } catch { }

		var act = () => ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (500, 'duplicate')");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UPDATE DML
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task UpdateDml_SingleRow()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (600, 'original', 10)"); } catch { }

		var count = await ExecuteDmlAsync("UPDATE DmlTest SET Name = 'updated' WHERE Id = 600");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Name FROM DmlTest WHERE Id = 600");
		rows.Should().ContainSingle().Which["Name"].Should().Be("updated");
	}

	[Fact]
	public async Task UpdateDml_MultipleColumns()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val, Active) VALUES (601, 'old', 1, false)"); } catch { }

		var count = await ExecuteDmlAsync(
			"UPDATE DmlTest SET Name = 'new', Val = 99, Active = true WHERE Id = 601");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Name, Val, Active FROM DmlTest WHERE Id = 601");
		rows[0]["Name"].Should().Be("new");
		rows[0]["Val"].Should().Be(99L);
		rows[0]["Active"].Should().Be(true);
	}

	[Fact]
	public async Task UpdateDml_MultipleRows()
	{
		await EnsureDmlTableAsync();
		try
		{
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (701, 'a', 1)");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (702, 'b', 2)");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (703, 'c', 3)");
		}
		catch { }

		var count = await ExecuteDmlAsync("UPDATE DmlTest SET Val = 0 WHERE Id IN (701, 702, 703)");
		count.Should().Be(3);
	}

	[Fact]
	public async Task UpdateDml_NoMatch_ReturnsZero()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync("UPDATE DmlTest SET Name = 'nope' WHERE Id = 99999");
		count.Should().Be(0);
	}

	[Fact]
	public async Task UpdateDml_WithExpression()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (800, 'expr', 10)"); } catch { }

		var count = await ExecuteDmlAsync("UPDATE DmlTest SET Val = Val * 2 WHERE Id = 800");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Val FROM DmlTest WHERE Id = 800");
		rows[0]["Val"].Should().Be(20L);
	}

	[Fact]
	public async Task UpdateDml_SetNull()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (801, 'before', 10)"); } catch { }

		var count = await ExecuteDmlAsync("UPDATE DmlTest SET Val = NULL WHERE Id = 801");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT Val FROM DmlTest WHERE Id = 801");
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task UpdateDml_WithParameters()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (802, 'param', 5)"); } catch { }

		var count = await ExecuteDmlAsync(
			"UPDATE DmlTest SET Val = @newVal WHERE Id = @id",
			("newVal", SpannerDbType.Int64, (object?)42L),
			("id", SpannerDbType.Int64, 802L));
		count.Should().Be(1);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DELETE DML
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#delete_statement
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task DeleteDml_SingleRow()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (900, 'todelete')"); } catch { }

		var count = await ExecuteDmlAsync("DELETE FROM DmlTest WHERE Id = 900");
		count.Should().Be(1);

		var rows = await QueryAsync("SELECT * FROM DmlTest WHERE Id = 900");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task DeleteDml_MultipleRows()
	{
		await EnsureDmlTableAsync();
		try
		{
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (901, 'a')");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (902, 'b')");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name) VALUES (903, 'c')");
		}
		catch { }

		var count = await ExecuteDmlAsync("DELETE FROM DmlTest WHERE Id IN (901, 902, 903)");
		count.Should().Be(3);
	}

	[Fact]
	public async Task DeleteDml_NoMatch_ReturnsZero()
	{
		await EnsureDmlTableAsync();
		var count = await ExecuteDmlAsync("DELETE FROM DmlTest WHERE Id = 99999");
		count.Should().Be(0);
	}

	[Fact]
	public async Task DeleteDml_WithWhereExpression()
	{
		await EnsureDmlTableAsync();
		try
		{
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (1001, 'x', 5)");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (1002, 'y', 15)");
			await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (1003, 'z', 25)");
		}
		catch { }

		var count = await ExecuteDmlAsync("DELETE FROM DmlTest WHERE Val > 10 AND Id IN (1001, 1002, 1003)");
		count.Should().Be(2);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SDK Mutation commands (Insert, Update, InsertOrUpdate, Delete)
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#mutation
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task SdkInsert_ThenQuery()
	{
		await EnsureDmlTableAsync();
		await InsertAsync("DmlTest", new Dictionary<string, object?> { ["Id"] = 1100L, ["Name"] = "sdk_insert", ["Val"] = 77L });

		var rows = await QueryAsync("SELECT Name, Val FROM DmlTest WHERE Id = 1100");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("sdk_insert");
		rows[0]["Val"].Should().Be(77L);
	}

	[Fact]
	public async Task SdkInsertOrUpdate_InsertsBranch()
	{
		await EnsureDmlTableAsync();
		await InsertOrUpdateAsync("DmlTest", new Dictionary<string, object?> { ["Id"] = 1200L, ["Name"] = "upsert_new", ["Val"] = 11L });

		var rows = await QueryAsync("SELECT Name FROM DmlTest WHERE Id = 1200");
		rows.Should().ContainSingle().Which["Name"].Should().Be("upsert_new");
	}

	[Fact]
	public async Task SdkInsertOrUpdate_UpdatesBranch()
	{
		await EnsureDmlTableAsync();
		await InsertAsync("DmlTest", new Dictionary<string, object?> { ["Id"] = 1201L, ["Name"] = "original", ["Val"] = 11L });
		await InsertOrUpdateAsync("DmlTest", new Dictionary<string, object?> { ["Id"] = 1201L, ["Name"] = "updated", ["Val"] = 22L });

		var rows = await QueryAsync("SELECT Name, Val FROM DmlTest WHERE Id = 1201");
		rows.Should().ContainSingle();
		rows[0]["Name"].Should().Be("updated");
		rows[0]["Val"].Should().Be(22L);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NOT NULL constraint enforcement
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#mutation
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task NotNull_Insert_ValidValue_Succeeds()
	{
		await EnsureNotNullTableAsync();
		await InsertAsync("DmlNotNull", new Dictionary<string, object?> { ["Id"] = 1L, ["Required"] = "ok" });

		var rows = await QueryAsync("SELECT Required FROM DmlNotNull WHERE Id = 1");
		rows.Should().ContainSingle().Which["Required"].Should().Be("ok");
	}

	[Fact]
	public async Task NotNull_Insert_NullRequired_Throws()
	{
		await EnsureNotNullTableAsync();
		var act = () => ExecuteDmlAsync(
			"INSERT INTO DmlNotNull (Id, Required) VALUES (2, NULL)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task NotNull_Insert_OptionalNull_Succeeds()
	{
		await EnsureNotNullTableAsync();
		await InsertAsync("DmlNotNull", new Dictionary<string, object?> { ["Id"] = 3L, ["Required"] = "ok" });

		var rows = await QueryAsync("SELECT Optional FROM DmlNotNull WHERE Id = 3");
		rows[0]["Optional"].Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// CHECK constraint enforcement
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Check_Insert_ValidValue_Succeeds()
	{
		await EnsureCheckTableAsync();
		await InsertAsync("DmlCheck", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });

		var rows = await QueryAsync("SELECT Val FROM DmlCheck WHERE Id = 1");
		rows.Should().ContainSingle().Which["Val"].Should().Be(10L);
	}

	[Fact]
	public async Task Check_Insert_InvalidValue_Throws()
	{
		await EnsureCheckTableAsync();
		var act = () => ExecuteDmlAsync(
			"INSERT INTO DmlCheck (Id, Val) VALUES (2, 0)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Check_Insert_NegativeValue_Throws()
	{
		await EnsureCheckTableAsync();
		var act = () => ExecuteDmlAsync(
			"INSERT INTO DmlCheck (Id, Val) VALUES (3, -5)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	public async Task Check_Update_ToInvalidValue_Throws()
	{
		await EnsureCheckTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlCheck (Id, Val) VALUES (4, 10)"); } catch { }

		var act = () => ExecuteDmlAsync("UPDATE DmlCheck SET Val = 0 WHERE Id = 4");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// FOREIGN KEY enforcement
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_keys
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Fk_Insert_ValidReference_Succeeds()
	{
		await EnsureFkTablesAsync();
		await InsertAsync("DmlParent", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "parent1" });
		await InsertAsync("DmlChild", new Dictionary<string, object?> { ["Id"] = 1L, ["ParentId"] = 1L });

		var rows = await QueryAsync("SELECT ParentId FROM DmlChild WHERE Id = 1");
		rows.Should().ContainSingle().Which["ParentId"].Should().Be(1L);
	}

	[Fact]
	public async Task Fk_Insert_InvalidReference_Throws()
	{
		await EnsureFkTablesAsync();
		var act = () => InsertAsync("DmlChild", new Dictionary<string, object?> { ["Id"] = 2L, ["ParentId"] = 999L });
		await act.Should().ThrowAsync<Exception>();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// UNIQUE INDEX enforcement
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_index
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Unique_Insert_DistinctValues_Succeeds()
	{
		await EnsureUniqueTableAsync();
		await InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 1L, ["Code"] = "A" });
		await InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 2L, ["Code"] = "B" });

		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM DmlUnique WHERE Code IN ('A', 'B')");
		rows[0]["C"].Should().Be(2L);
	}

	[Fact]
	public async Task Unique_Insert_DuplicateValue_Throws()
	{
		await EnsureUniqueTableAsync();
		try { await InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 10L, ["Code"] = "DUP" }); } catch { }

		var act = () => InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 11L, ["Code"] = "DUP" });
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task Unique_MultipleNulls_Allowed()
	{
		await EnsureUniqueTableAsync();
		// UNIQUE index allows multiple NULL values
		// Ref: https://cloud.google.com/spanner/docs/secondary-indexes#unique_indexes
		await InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 20L });
		await InsertAsync("DmlUnique", new Dictionary<string, object?> { ["Id"] = 21L });
		// Both have NULL Code â€” should succeed
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM DmlUnique WHERE Id IN (20, 21)");
		rows[0]["C"].Should().Be(2L);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// STRING(N) length enforcement
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#string
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task StringLength_ExactLength_Succeeds()
	{
		await EnsureLenTableAsync();
		await InsertAsync("DmlLen", new Dictionary<string, object?> { ["Id"] = 1L, ["Short"] = "abcde" }); // exactly 5
		var rows = await QueryAsync("SELECT Short FROM DmlLen WHERE Id = 1");
		rows[0]["Short"].Should().Be("abcde");
	}

	[Fact]
	public async Task StringLength_WithinLimit_Succeeds()
	{
		await EnsureLenTableAsync();
		await InsertAsync("DmlLen", new Dictionary<string, object?> { ["Id"] = 2L, ["Short"] = "ab" }); // 2 < 5
		var rows = await QueryAsync("SELECT Short FROM DmlLen WHERE Id = 2");
		rows[0]["Short"].Should().Be("ab");
	}

	[Fact]
	public async Task StringLength_ExceedsLimit_Throws()
	{
		await EnsureLenTableAsync();
		var act = () => InsertAsync("DmlLen", new Dictionary<string, object?> { ["Id"] = 3L, ["Short"] = "toolong" }); // 7 > 5
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task StringLength_Empty_Succeeds()
	{
		await EnsureLenTableAsync();
		await InsertAsync("DmlLen", new Dictionary<string, object?> { ["Id"] = 4L, ["Short"] = "" });
		var rows = await QueryAsync("SELECT Short FROM DmlLen WHERE Id = 4");
		rows[0]["Short"].Should().Be("");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Transaction behavior
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#committransactionrequest
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	public async Task Transaction_MultipleDml_AllCommitted()
	{
		await EnsureDmlTableAsync();
		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();
		using var txn = await conn.BeginTransactionAsync();

		for (int i = 0; i < 5; i++)
		{
			var cmd = conn.CreateDmlCommand($"INSERT INTO DmlTest (Id, Name) VALUES ({1500 + i}, 'txn_{i}')");
			cmd.Transaction = txn;
			await cmd.ExecuteNonQueryAsync();
		}

		await txn.CommitAsync();

		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM DmlTest WHERE Id BETWEEN 1500 AND 1504");
		rows[0]["C"].Should().Be(5L);
	}

	[Fact]
	public async Task Transaction_ReadAfterWrite()
	{
		await EnsureDmlTableAsync();
		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();
		using var txn = await conn.BeginTransactionAsync();

		var insertCmd = conn.CreateDmlCommand("INSERT INTO DmlTest (Id, Name, Val) VALUES (1600, 'within_txn', 42)");
		insertCmd.Transaction = txn;
		await insertCmd.ExecuteNonQueryAsync();

		// Read within same transaction
		var selectCmd = conn.CreateSelectCommand("SELECT Name FROM DmlTest WHERE Id = 1600");
		selectCmd.Transaction = txn;
		using var reader = await selectCmd.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue();
		reader.GetString(0).Should().Be("within_txn");

		await txn.CommitAsync();
	}

	[Fact]
	public async Task Transaction_UpdateAndReadBack()
	{
		await EnsureDmlTableAsync();
		try { await ExecuteDmlAsync("INSERT INTO DmlTest (Id, Name, Val) VALUES (1700, 'start', 0)"); } catch { }

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();
		using var txn = await conn.BeginTransactionAsync();

		var updateCmd = conn.CreateDmlCommand("UPDATE DmlTest SET Val = Val + 1 WHERE Id = 1700");
		updateCmd.Transaction = txn;
		await updateCmd.ExecuteNonQueryAsync();

		var selectCmd = conn.CreateSelectCommand("SELECT Val FROM DmlTest WHERE Id = 1700");
		selectCmd.Transaction = txn;
		var newVal = (long)(await selectCmd.ExecuteScalarAsync())!;
		newVal.Should().Be(1L);

		await txn.CommitAsync();
	}
}
