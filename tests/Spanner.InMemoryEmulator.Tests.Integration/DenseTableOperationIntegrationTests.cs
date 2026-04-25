using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Dense data-driven tests for table operations: INSERT, SELECT, UPDATE, DELETE.
/// Each test manipulates rows in various patterns and validates results.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DenseTableOperationIntegrationTests : IntegrationTestBase
{
	public DenseTableOperationIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// INSERT with various value types
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(1, "Alice")]
	[InlineData(2, "Bob")]
	[InlineData(3, "Charlie")]
	[InlineData(4, "")]
	[InlineData(5, "A long name with spaces and 123")]
	public async Task Insert_And_ReadBack(int id, string name)
	{
		var t = $"InsR_{id}_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = id, ["Name"] = name });
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = {id}");
		rows.Should().ContainSingle().Which["Name"].Should().Be(name);
	}

	[Fact]
	public async Task Insert_AllColumnTypes()
	{
		var t = $"InsAll_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($@"CREATE TABLE {t} (
			K INT64 NOT NULL,
			IntCol INT64,
			DblCol FLOAT64,
			BoolCol BOOL,
			StrCol STRING(MAX),
			DateCol DATE,
			TsCol TIMESTAMP
		) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> {
			["K"] = 1L,
			["IntCol"] = 42L,
			["DblCol"] = 3.14,
			["BoolCol"] = true,
			["StrCol"] = "hello",
			["DateCol"] = new DateTime(2024, 1, 15),
			["TsCol"] = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc)
		});
		var rows = await QueryAsync($"SELECT * FROM {t} WHERE K = 1");
		rows.Should().ContainSingle();
		rows[0]["IntCol"].Should().Be(42L);
		((double)rows[0]["DblCol"]!).Should().BeApproximately(3.14, 1e-10);
		rows[0]["BoolCol"].Should().Be(true);
		rows[0]["StrCol"].Should().Be("hello");
	}

	[Fact]
	public async Task Insert_NullColumns()
	{
		var t = $"InsNull_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = DBNull.Value });
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows.Should().ContainSingle();
		rows[0]["V"].Should().BeNull();
	}

	[Fact]
	public async Task Insert_MultipleRows_InSequence()
	{
		var t = $"InsMul_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = $"row{i}" });
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(10L);
	}

	[Fact]
	public async Task Insert_DuplicateKey_Fails()
	{
		var t = $"InsDup_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "first" });
		var act = async () => await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "second" });
		// Direct API path throws InvalidOperationException; SDK path wraps it as SpannerException
		await act.Should().ThrowAsync<Exception>().Where(e => e is SpannerException || e is InvalidOperationException);
	}

	// ═══════════════════════════════════════════════════════════════
	// UPDATE operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Update_SingleColumn()
	{
		var t = $"UpSi_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "old" });
		await ExecuteDmlAsync($"UPDATE {t} SET V = 'new' WHERE K = 1");
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be("new");
	}

	[Fact]
	public async Task Update_MultipleColumns()
	{
		var t = $"UpMu_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = "old", ["B"] = 0L });
		await ExecuteDmlAsync($"UPDATE {t} SET A = 'new', B = 99 WHERE K = 1");
		var rows = await QueryAsync($"SELECT A, B FROM {t} WHERE K = 1");
		rows[0]["A"].Should().Be("new");
		rows[0]["B"].Should().Be(99L);
	}

	[Fact]
	public async Task Update_MultipleRows()
	{
		var t = $"UpMR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = 0L });
		await ExecuteDmlAsync($"UPDATE {t} SET V = K * 10 WHERE TRUE");
		var rows = await QueryAsync($"SELECT K, V FROM {t} ORDER BY K");
		for (int i = 0; i < 5; i++)
			rows[i]["V"].Should().Be((long)(i + 1) * 10);
	}

	[Fact]
	public async Task Update_WithExpression()
	{
		var t = $"UpEx_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await ExecuteDmlAsync($"UPDATE {t} SET V = V + 5 WHERE K = 1");
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be(15L);
	}

	[Fact]
	public async Task Update_SetNull()
	{
		var t = $"UpNl_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "hello" });
		await ExecuteDmlAsync($"UPDATE {t} SET V = NULL WHERE K = 1");
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().BeNull();
	}

	[Fact]
	public async Task Update_NoMatchingRows()
	{
		var t = $"UpNo_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "hello" });
		await ExecuteDmlAsync($"UPDATE {t} SET V = 'updated' WHERE K = 999");
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be("hello");
	}

	// ═══════════════════════════════════════════════════════════════
	// DELETE operations
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Delete_SingleRow()
	{
		var t = $"DelS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "doomed" });
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE K = 1");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	public async Task Delete_SomeRows()
	{
		var t = $"DelSo_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = (long)i });
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE V > 5");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(5L);
	}

	[Fact]
	public async Task Delete_AllRows()
	{
		var t = $"DelAll_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE TRUE");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(0L);
	}

	[Fact]
	public async Task Delete_NoMatch()
	{
		var t = $"DelNM_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L });
		await ExecuteDmlAsync($"DELETE FROM {t} WHERE K = 999");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// INSERT…SELECT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InsertSelect_CopyRows()
	{
		var src = $"Src_{Guid.NewGuid():N}";
		var dst = $"Dst_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {src} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await ExecuteDdlAsync($"CREATE TABLE {dst} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(src, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = $"r{i}" });
		await ExecuteDmlAsync($"INSERT INTO {dst} (K, V) SELECT K, V FROM {src}");
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {dst}");
		rows[0]["C"].Should().Be(5L);
	}

	[Fact]
	public async Task InsertSelect_WithTransform()
	{
		var src = $"SrcT_{Guid.NewGuid():N}";
		var dst = $"DstT_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {src} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await ExecuteDdlAsync($"CREATE TABLE {dst} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(src, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "hello" });
		await ExecuteDmlAsync($"INSERT INTO {dst} (K, V) SELECT K, UPPER(V) FROM {src}");
		var rows = await QueryAsync($"SELECT V FROM {dst} WHERE K = 1");
		rows[0]["V"].Should().Be("HELLO");
	}

	// ═══════════════════════════════════════════════════════════════
	// SELECT patterns
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Select_Star()
	{
		var t = $"SelS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A STRING(MAX), B INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = "x", ["B"] = 10L });
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().ContainSingle();
		rows[0].Should().ContainKey("K").And.ContainKey("A").And.ContainKey("B");
	}

	[Fact]
	public async Task Select_WithAlias()
	{
		var t = $"SelA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 42L });
		var rows = await QueryAsync($"SELECT V AS Value FROM {t}");
		rows[0]["Value"].Should().Be(42L);
	}

	[Fact]
	public async Task Select_WithExpression()
	{
		var t = $"SelE_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		var rows = await QueryAsync($"SELECT V * 2 AS Doubled FROM {t}");
		rows[0]["Doubled"].Should().Be(20L);
	}

	[Fact]
	public async Task Select_OrderBy_Asc()
	{
		var t = $"SelOA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 30L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		var rows = await QueryAsync($"SELECT V FROM {t} ORDER BY V ASC");
		rows.Select(r => (long)r["V"]!).Should().ContainInOrder(10L, 20L, 30L);
	}

	[Fact]
	public async Task Select_OrderBy_Desc()
	{
		var t = $"SelOD_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 30L });
		var rows = await QueryAsync($"SELECT V FROM {t} ORDER BY V DESC");
		rows.Select(r => (long)r["V"]!).Should().ContainInOrder(30L, 20L, 10L);
	}

	[Fact]
	public async Task Select_Limit()
	{
		var t = $"SelLim_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT K FROM {t} ORDER BY K LIMIT 3");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task Select_LimitOffset()
	{
		var t = $"SelLO_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT K FROM {t} ORDER BY K LIMIT 3 OFFSET 5");
		rows.Should().HaveCount(3);
		rows[0]["K"].Should().Be(6L);
	}

	[Fact]
	public async Task Select_Distinct()
	{
		var t = $"SelD_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = "b" });
		var rows = await QueryAsync($"SELECT DISTINCT V FROM {t} ORDER BY V");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public async Task Select_Where_MultipleConditions()
	{
		var t = $"SelWM_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A INT64, B STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = 10L, ["B"] = "x" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["A"] = 20L, ["B"] = "x" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["A"] = 10L, ["B"] = "y" });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE A = 10 AND B = 'x'");
		rows.Should().ContainSingle().Which["K"].Should().Be(1L);
	}

	[Fact]
	public async Task Select_EmptyResult()
	{
		var t = $"SelEmp_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		var rows = await QueryAsync($"SELECT * FROM {t}");
		rows.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// Aggregate queries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Aggregate_Count()
	{
		var t = $"AggCt_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 7; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT COUNT(*) AS C FROM {t}");
		rows[0]["C"].Should().Be(7L);
	}

	[Fact]
	public async Task Aggregate_Sum()
	{
		var t = $"AggSm_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 30L });
		var rows = await QueryAsync($"SELECT SUM(V) AS S FROM {t}");
		rows[0]["S"].Should().Be(60L);
	}

	[Fact]
	public async Task Aggregate_Avg()
	{
		var t = $"AggAv_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V FLOAT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10.0 });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20.0 });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 30.0 });
		var rows = await QueryAsync($"SELECT AVG(V) AS A FROM {t}");
		((double)rows[0]["A"]!).Should().BeApproximately(20.0, 1e-10);
	}

	[Fact]
	public async Task Aggregate_MinMax()
	{
		var t = $"AggMM_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 5L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 15L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = 10L });
		var rows = await QueryAsync($"SELECT MIN(V) AS Mi, MAX(V) AS Ma FROM {t}");
		rows[0]["Mi"].Should().Be(5L);
		rows[0]["Ma"].Should().Be(15L);
	}

	[Fact]
	public async Task Aggregate_GroupBy()
	{
		var t = $"AggGB_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Cat STRING(MAX), V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["Cat"] = "A", ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["Cat"] = "A", ["V"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["Cat"] = "B", ["V"] = 30L });
		var rows = await QueryAsync($"SELECT Cat, SUM(V) AS S FROM {t} GROUP BY Cat ORDER BY Cat");
		rows.Should().HaveCount(2);
		rows[0]["Cat"].Should().Be("A");
		rows[0]["S"].Should().Be(30L);
		rows[1]["Cat"].Should().Be("B");
		rows[1]["S"].Should().Be(30L);
	}

	[Fact]
	public async Task Aggregate_GroupBy_Having()
	{
		var t = $"AggGH_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, Cat STRING(MAX), V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["Cat"] = "A", ["V"] = 10L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["Cat"] = "A", ["V"] = 20L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["Cat"] = "B", ["V"] = 5L });
		var rows = await QueryAsync($"SELECT Cat, SUM(V) AS S FROM {t} GROUP BY Cat HAVING SUM(V) > 10 ORDER BY Cat");
		rows.Should().ContainSingle().Which["Cat"].Should().Be("A");
	}

	[Fact]
	public async Task Aggregate_CountDistinct()
	{
		var t = $"AggCD_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = "b" });
		var rows = await QueryAsync($"SELECT COUNT(DISTINCT V) AS C FROM {t}");
		rows[0]["C"].Should().Be(2L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Subquery_InWhere()
	{
		var t = $"SubW_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		for (int i = 1; i <= 5; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i, ["V"] = (long)(i * 10) });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE V > (SELECT AVG(V) FROM {t}) ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().BeEquivalentTo([4L, 5L]);
	}

	[Fact]
	public async Task Subquery_Exists()
	{
		var t1 = $"SubE1_{Guid.NewGuid():N}";
		var t2 = $"SubE2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t1} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await ExecuteDdlAsync($"CREATE TABLE {t2} (K INT64 NOT NULL, Ref INT64) PRIMARY KEY (K)");
		await InsertAsync(t1, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 10L });
		await InsertAsync(t1, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = 20L });
		await InsertAsync(t2, new Dictionary<string, object?> { ["K"] = 1L, ["Ref"] = 1L });
		var rows = await QueryAsync($"SELECT K FROM {t1} WHERE EXISTS (SELECT 1 FROM {t2} WHERE {t2}.Ref = {t1}.K)");
		rows.Should().ContainSingle().Which["K"].Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Transaction isolation
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Transaction_MultiDml_Commit()
	{
		var t = $"TxCo_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 0L });

		using var conn = Fixture.CreateConnection();
		using var tx = await conn.BeginTransactionAsync();
		using (var cmd = conn.CreateDmlCommand($"UPDATE {t} SET V = V + 1 WHERE K = 1"))
		{
			cmd.Transaction = tx;
			await cmd.ExecuteNonQueryAsync();
		}
		using (var cmd = conn.CreateDmlCommand($"UPDATE {t} SET V = V + 1 WHERE K = 1"))
		{
			cmd.Transaction = tx;
			await cmd.ExecuteNonQueryAsync();
		}
		await tx.CommitAsync();

		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be(2L);
	}

	[Fact]
	public async Task Transaction_Rollback()
	{
		var t = $"TxRb_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = 0L });

		using var conn = Fixture.CreateConnection();
		using var tx = await conn.BeginTransactionAsync();
		using (var cmd = conn.CreateDmlCommand($"UPDATE {t} SET V = 999 WHERE K = 1"))
		{
			cmd.Transaction = tx;
			await cmd.ExecuteNonQueryAsync();
		}
		await tx.RollbackAsync();

		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be(0L);
	}

	// ═══════════════════════════════════════════════════════════════
	// InsertOrUpdate (UPSERT via mutations)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InsertOrUpdate_NewRow()
	{
		var t = $"UpsN_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "new" });
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be("new");
	}

	[Fact]
	public async Task InsertOrUpdate_ExistingRow()
	{
		var t = $"UpsE_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "old" });
		await InsertOrUpdateAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "updated" });
		var rows = await QueryAsync($"SELECT V FROM {t} WHERE K = 1");
		rows[0]["V"].Should().Be("updated");
	}

	// ═══════════════════════════════════════════════════════════════
	// WHERE clause edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Where_IS_NULL()
	{
		var t = $"WhNull_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = DBNull.Value });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE V IS NULL");
		rows.Should().ContainSingle().Which["K"].Should().Be(2L);
	}

	[Fact]
	public async Task Where_IS_NOT_NULL()
	{
		var t = $"WhNNu_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = DBNull.Value });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE V IS NOT NULL");
		rows.Should().ContainSingle().Which["K"].Should().Be(1L);
	}

	[Fact]
	public async Task Where_IN()
	{
		var t = $"WhIn_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE K IN (2, 4, 6) ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(2L, 4L, 6L);
	}

	[Fact]
	public async Task Where_BETWEEN()
	{
		var t = $"WhBt_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL) PRIMARY KEY (K)");
		for (int i = 1; i <= 10; i++)
			await InsertAsync(t, new Dictionary<string, object?> { ["K"] = (long)i });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE K BETWEEN 3 AND 7 ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(3L, 4L, 5L, 6L, 7L);
	}

	[Fact]
	public async Task Where_Complex_OR_AND()
	{
		var t = $"WhCA_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, A INT64, B INT64) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["A"] = 1L, ["B"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["A"] = 1L, ["B"] = 2L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["A"] = 2L, ["B"] = 1L });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 4L, ["A"] = 2L, ["B"] = 2L });
		// (A=1 AND B=1) OR (A=2 AND B=2) => rows 1, 4
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE (A = 1 AND B = 1) OR (A = 2 AND B = 2) ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(1L, 4L);
	}

	// ═══════════════════════════════════════════════════════════════
	// String functions in WHERE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Where_STARTS_WITH()
	{
		var t = $"WhSW_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "hello" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = "world" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = "help" });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE STARTS_WITH(V, 'hel') ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(1L, 3L);
	}

	[Fact]
	public async Task Where_LENGTH_Filter()
	{
		var t = $"WhLen_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V STRING(MAX)) PRIMARY KEY (K)");
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 1L, ["V"] = "a" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 2L, ["V"] = "bb" });
		await InsertAsync(t, new Dictionary<string, object?> { ["K"] = 3L, ["V"] = "ccc" });
		var rows = await QueryAsync($"SELECT K FROM {t} WHERE LENGTH(V) > 1 ORDER BY K");
		rows.Select(r => (long)r["K"]!).Should().ContainInOrder(2L, 3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Date/Timestamp in tables
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("DATE '2024-01-15'")]
	[InlineData("DATE '2000-01-01'")]
	[InlineData("DATE '2099-12-31'")]
	public async Task Date_InsertAndRead(string dateLiteral)
	{
		var t = $"DateIR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, D DATE) PRIMARY KEY (K)");
		await ExecuteDmlAsync($"INSERT INTO {t} (K, D) VALUES (1, {dateLiteral})");
		var rows = await QueryAsync($"SELECT D FROM {t} WHERE K = 1");
		rows.Should().ContainSingle();
		rows[0]["D"].Should().NotBe(DBNull.Value);
	}

	[Fact]
	public async Task Timestamp_InsertAndRead()
	{
		var t = $"TsIR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, T TIMESTAMP) PRIMARY KEY (K)");
		await ExecuteDmlAsync($"INSERT INTO {t} (K, T) VALUES (1, TIMESTAMP '2024-01-15T10:30:00Z')");
		var rows = await QueryAsync($"SELECT T FROM {t} WHERE K = 1");
		rows.Should().ContainSingle();
		rows[0]["T"].Should().NotBe(DBNull.Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Float special values
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Float_NaN_InTable()
	{
		var t = $"FNaN_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V FLOAT64) PRIMARY KEY (K)");
		await ExecuteDmlAsync($"INSERT INTO {t} (K, V) VALUES (1, IEEE_DIVIDE(0.0, 0.0))");
		var rows = await QueryAsync($"SELECT IS_NAN(V) AS R FROM {t} WHERE K = 1");
		rows[0]["R"].Should().Be(true);
	}

	[Fact]
	public async Task Float_Infinity_InTable()
	{
		var t = $"FInf_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (K INT64 NOT NULL, V FLOAT64) PRIMARY KEY (K)");
		await ExecuteDmlAsync($"INSERT INTO {t} (K, V) VALUES (1, IEEE_DIVIDE(1.0, 0.0))");
		var rows = await QueryAsync($"SELECT IS_INF(V) AS R FROM {t} WHERE K = 1");
		rows[0]["R"].Should().Be(true);
	}

	// ═══════════════════════════════════════════════════════════════
	// Complex expressions in SELECT
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("CAST(1 + 2 AS STRING) || ' items'", "3 items")]
	[InlineData("UPPER(CONCAT('hello', ' ', 'world'))", "HELLO WORLD")]
	[InlineData("IF(MOD(10, 2) = 0, 'even', 'odd')", "even")]
	[InlineData("IF(MOD(11, 2) = 0, 'even', 'odd')", "odd")]
	[InlineData("CASE WHEN 5 BETWEEN 1 AND 10 THEN 'in' ELSE 'out' END", "in")]
	[InlineData("COALESCE(CAST(NULL AS STRING), 'default')", "default")]
	public async Task ComplexSelectExpressions(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);
}
