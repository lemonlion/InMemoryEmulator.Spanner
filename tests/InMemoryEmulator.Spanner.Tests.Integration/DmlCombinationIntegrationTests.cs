using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Dense DML operation tests: INSERT, UPDATE, DELETE with various patterns.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DmlCombinationIntegrationTests : IntegrationTestBase
{
	public DmlCombinationIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// INSERT VALUES
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Insert_SingleRow()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlIns1 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlIns1 (Id, Val) VALUES (1, 'hello')");
		var rows = await QueryAsync("SELECT Val FROM DmlIns1 WHERE Id = 1");
		rows[0]["Val"].Should().Be("hello");
	}

	[Fact]
	public async Task Insert_MultipleRows()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlIns2 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlIns2 (Id, Val) VALUES (1, 'a'), (2, 'b'), (3, 'c')");
		var rows = await QueryAsync("SELECT Val FROM DmlIns2 ORDER BY Id");
		rows.Should().HaveCount(3);
		rows[0]["Val"].Should().Be("a");
		rows[1]["Val"].Should().Be("b");
		rows[2]["Val"].Should().Be("c");
	}

	[Fact]
	public async Task Insert_WithNull()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlIns3 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlIns3 (Id, Val) VALUES (1, NULL)");
		var rows = await QueryAsync("SELECT Val FROM DmlIns3 WHERE Id = 1");
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task Insert_AllTypes()
	{
		try
		{
			await ExecuteDdlAsync(@"CREATE TABLE DmlIns4 (
				Id INT64 NOT NULL, ColStr STRING(100), ColInt INT64,
				ColFloat FLOAT64, ColBool BOOL, ColDate DATE,
				ColTs TIMESTAMP
			) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync(
			"INSERT INTO DmlIns4 (Id, ColStr, ColInt, ColFloat, ColBool, ColDate, ColTs) VALUES (1, 'test', 42, 3.14, TRUE, DATE '2024-01-01', TIMESTAMP '2024-01-01T00:00:00Z')");

		var rows = await QueryAsync("SELECT ColStr, ColInt, ColFloat, ColBool FROM DmlIns4 WHERE Id = 1");
		rows[0]["ColStr"].Should().Be("test");
		rows[0]["ColInt"].Should().Be(42L);
		((double)rows[0]["ColFloat"]!).Should().BeApproximately(3.14, 1e-10);
		rows[0]["ColBool"].Should().Be(true);
	}

	[Fact]
	public async Task Insert_DuplicateKey_Throws()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlInsDup (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlInsDup (Id) VALUES (1)");
		var act = () => ExecuteDmlAsync("INSERT INTO DmlInsDup (Id) VALUES (1)");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task Insert_WithExpression()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlInsExpr (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlInsExpr (Id, Val) VALUES (1, 10 + 20)");
		var rows = await QueryAsync("SELECT Val FROM DmlInsExpr WHERE Id = 1");
		rows[0]["Val"].Should().Be(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// UPDATE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#update_statement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Update_SingleRow()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd1 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd1", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "old" });
		await ExecuteDmlAsync("UPDATE DmlUpd1 SET Val = 'new' WHERE Id = 1");
		var rows = await QueryAsync("SELECT Val FROM DmlUpd1 WHERE Id = 1");
		rows[0]["Val"].Should().Be("new");
	}

	[Fact]
	public async Task Update_MultipleRows()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });
		await ExecuteDmlAsync("UPDATE DmlUpd2 SET Val = Val * 2 WHERE Val >= 20");
		var rows = await QueryAsync("SELECT Id, Val FROM DmlUpd2 ORDER BY Id");
		rows[0]["Val"].Should().Be(10L);
		rows[1]["Val"].Should().Be(40L);
		rows[2]["Val"].Should().Be(60L);
	}

	[Fact]
	public async Task Update_AllRows()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd3 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd3",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L });
		await ExecuteDmlAsync("UPDATE DmlUpd3 SET Val = 0 WHERE TRUE");
		var rows = await QueryAsync("SELECT Val FROM DmlUpd3 ORDER BY Id");
		rows.Should().AllSatisfy(r => r["Val"].Should().Be(0L));
	}

	[Fact]
	public async Task Update_WithNull()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd4 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd4", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "hello" });
		await ExecuteDmlAsync("UPDATE DmlUpd4 SET Val = NULL WHERE Id = 1");
		var rows = await QueryAsync("SELECT Val FROM DmlUpd4 WHERE Id = 1");
		rows[0]["Val"].Should().BeNull();
	}

	[Fact]
	public async Task Update_NoRowsMatch()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd5 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd5", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await ExecuteDmlAsync("UPDATE DmlUpd5 SET Val = 99 WHERE Id = 999");
		var rows = await QueryAsync("SELECT Val FROM DmlUpd5 WHERE Id = 1");
		rows[0]["Val"].Should().Be(10L);
	}

	[Fact]
	public async Task Update_SetFromExpression()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd6 (Id INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd6", new Dictionary<string, object?> { ["Id"] = 1L, ["A"] = 10L, ["B"] = 20L });
		await ExecuteDmlAsync("UPDATE DmlUpd6 SET A = A + B WHERE Id = 1");
		var rows = await QueryAsync("SELECT A FROM DmlUpd6 WHERE Id = 1");
		rows[0]["A"].Should().Be(30L);
	}

	[Fact]
	public async Task Update_MultipleColumns()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlUpd7 (Id INT64 NOT NULL, A STRING(100), B INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlUpd7", new Dictionary<string, object?> { ["Id"] = 1L, ["A"] = "old", ["B"] = 0L });
		await ExecuteDmlAsync("UPDATE DmlUpd7 SET A = 'new', B = 42 WHERE Id = 1");
		var rows = await QueryAsync("SELECT A, B FROM DmlUpd7 WHERE Id = 1");
		rows[0]["A"].Should().Be("new");
		rows[0]["B"].Should().Be(42L);
	}

	// ═══════════════════════════════════════════════════════════════
	// DELETE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#delete_statement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Delete_SingleRow()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlDel1 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlDel1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "b" });
		await ExecuteDmlAsync("DELETE FROM DmlDel1 WHERE Id = 1");
		var rows = await QueryAsync("SELECT Id FROM DmlDel1 ORDER BY Id");
		rows.Should().ContainSingle().Which["Id"].Should().Be(2L);
	}

	[Fact]
	public async Task Delete_MultipleRows()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlDel2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlDel2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });
		await ExecuteDmlAsync("DELETE FROM DmlDel2 WHERE Val >= 20");
		var rows = await QueryAsync("SELECT Id FROM DmlDel2");
		rows.Should().ContainSingle().Which["Id"].Should().Be(1L);
	}

	[Fact]
	public async Task Delete_AllRows()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlDel3 (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlDel3",
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L });
		await ExecuteDmlAsync("DELETE FROM DmlDel3 WHERE TRUE");
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM DmlDel3");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	public async Task Delete_NoRowsMatch()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlDel4 (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlDel4", new Dictionary<string, object?> { ["Id"] = 1L });
		await ExecuteDmlAsync("DELETE FROM DmlDel4 WHERE Id = 999");
		var rows = await QueryAsync("SELECT COUNT(*) AS C FROM DmlDel4");
		rows[0]["C"].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// INSERT ... SELECT (INSERT from a query)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#insert_statement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Insert_SelectFrom()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE DmlInsSel_Src (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");
			await ExecuteDdlAsync("CREATE TABLE DmlInsSel_Dst (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("DmlInsSel_Src",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "a" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = "b" });

		await ExecuteDmlAsync("INSERT INTO DmlInsSel_Dst (Id, Val) SELECT Id, Val FROM DmlInsSel_Src");

		var rows = await QueryAsync("SELECT Val FROM DmlInsSel_Dst ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Val"].Should().Be("a");
		rows[1]["Val"].Should().Be("b");
	}

	// ═══════════════════════════════════════════════════════════════
	// DML with transactions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Transaction_InsertAndQuery()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlTx1 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }

		using var conn = Fixture.CreateConnection();
		using var tx = await conn.BeginTransactionAsync();

		var dml = conn.CreateDmlCommand("INSERT INTO DmlTx1 (Id, Val) VALUES (1, 'committed')");
		dml.Transaction = tx;
		await dml.ExecuteNonQueryAsync();
		await tx.CommitAsync();

		var rows = await QueryAsync("SELECT Val FROM DmlTx1 WHERE Id = 1");
		rows[0]["Val"].Should().Be("committed");
	}

	[Fact]
	public async Task Transaction_InsertUpdateDelete()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlTx2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }

		using var conn = Fixture.CreateConnection();
		using var tx = await conn.BeginTransactionAsync();

		var ins = conn.CreateDmlCommand("INSERT INTO DmlTx2 (Id, Val) VALUES (1, 10), (2, 20), (3, 30)");
		ins.Transaction = tx;
		await ins.ExecuteNonQueryAsync();

		var upd = conn.CreateDmlCommand("UPDATE DmlTx2 SET Val = Val + 100 WHERE Id = 2");
		upd.Transaction = tx;
		await upd.ExecuteNonQueryAsync();

		var del = conn.CreateDmlCommand("DELETE FROM DmlTx2 WHERE Id = 3");
		del.Transaction = tx;
		await del.ExecuteNonQueryAsync();

		await tx.CommitAsync();

		var rows = await QueryAsync("SELECT Id, Val FROM DmlTx2 ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Val"].Should().Be(10L);
		rows[1]["Val"].Should().Be(120L);
	}

	// ═══════════════════════════════════════════════════════════════
	// SDK Mutations (InsertAsync, InsertOrUpdateAsync)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Mutation_Insert()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlMut1 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("DmlMut1", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "mutated" });
		var rows = await QueryAsync("SELECT Val FROM DmlMut1 WHERE Id = 1");
		rows[0]["Val"].Should().Be("mutated");
	}

	[Fact]
	public async Task Mutation_InsertOrUpdate_Insert()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlMut2 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertOrUpdateAsync("DmlMut2", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "first" });
		var rows = await QueryAsync("SELECT Val FROM DmlMut2 WHERE Id = 1");
		rows[0]["Val"].Should().Be("first");
	}

	[Fact]
	public async Task Mutation_InsertOrUpdate_Update()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlMut3 (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)"); } catch { }
		await InsertOrUpdateAsync("DmlMut3", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "first" });
		await InsertOrUpdateAsync("DmlMut3", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = "second" });
		var rows = await QueryAsync("SELECT Val FROM DmlMut3 WHERE Id = 1");
		rows[0]["Val"].Should().Be("second");
	}

	// ═══════════════════════════════════════════════════════════════
	// DML with WHERE subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Update_WithSubqueryInWhere()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE DmlSubQ (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("DmlSubQ",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		await ExecuteDmlAsync(
			"UPDATE DmlSubQ SET Val = 99 WHERE Val = (SELECT MAX(Val) FROM DmlSubQ)");

		var rows = await QueryAsync("SELECT Val FROM DmlSubQ WHERE Id = 3");
		rows[0]["Val"].Should().Be(99L);
	}

	[Fact]
	public async Task Delete_WithSubqueryInWhere()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE DmlDelSubQ (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("DmlDelSubQ",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		await ExecuteDmlAsync(
			"DELETE FROM DmlDelSubQ WHERE Val = (SELECT MIN(Val) FROM DmlDelSubQ)");

		var rows = await QueryAsync("SELECT Id FROM DmlDelSubQ ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// NOT NULL constraint enforcement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Insert_NotNull_Violation()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlNotNullCombo (Id INT64 NOT NULL, Req STRING(100) NOT NULL) PRIMARY KEY (Id)"); } catch { }
		var act = () => ExecuteDmlAsync("INSERT INTO DmlNotNullCombo (Id, Req) VALUES (1, NULL)");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task Update_ToNull_NotNullColumn_Violation()
	{
		try { await ExecuteDdlAsync("CREATE TABLE DmlNotNull2 (Id INT64 NOT NULL, Req STRING(100) NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await ExecuteDmlAsync("INSERT INTO DmlNotNull2 (Id, Req) VALUES (1, 'val')");
		var act = () => ExecuteDmlAsync("UPDATE DmlNotNull2 SET Req = NULL WHERE Id = 1");
		await act.Should().ThrowAsync<Exception>();
	}
}
