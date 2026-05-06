using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 11: Indexes, Constraints, Interleaved Tables.
/// Tests flow through the full gRPC pipeline: SpannerConnection → gRPC → FakeSpannerService.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ConstraintIntegrationTests : IntegrationTestBase
{
public ConstraintIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── UNIQUE INDEX ───

	[Fact]
	public async Task UniqueIndex_EnforcedViaSdk()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_UniqueT (Id INT64 NOT NULL, Email STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDdlAsync(
			"CREATE UNIQUE INDEX IX_CI_Email ON CI_UniqueT (Email)");
		await InsertAsync("CI_UniqueT", new Dictionary<string, object?> { ["Id"] = 1L, ["Email"] = "a@b.com" });

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var cmd = conn.CreateInsertCommand("CI_UniqueT");
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 2L);
		cmd.Parameters.Add("Email", SpannerDbType.String, "a@b.com");

		var act = async () => await cmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── INTERLEAVED CASCADE ───

	[Fact]
	public async Task InterleavedCascade_DeletesChildRows_ViaSdk()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_Parents (PId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (PId)");
		await ExecuteDdlAsync(
			"CREATE TABLE CI_Children (PId INT64 NOT NULL, CId INT64 NOT NULL, Val STRING(MAX)) PRIMARY KEY (PId, CId), INTERLEAVE IN PARENT CI_Parents ON DELETE CASCADE");

		await InsertAsync("CI_Parents", new Dictionary<string, object?> { ["PId"] = 1L, ["Name"] = "P1" });
		await InsertAsync("CI_Children", new Dictionary<string, object?> { ["PId"] = 1L, ["CId"] = 10L, ["Val"] = "C1" });
		await InsertAsync("CI_Children", new Dictionary<string, object?> { ["PId"] = 1L, ["CId"] = 20L, ["Val"] = "C2" });

		// Delete parent through SDK delete command
		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var deleteCmd = conn.CreateDeleteCommand("CI_Parents");
		deleteCmd.Parameters.Add("PId", SpannerDbType.Int64, 1L);
		await deleteCmd.ExecuteNonQueryAsync();

		var cmd = conn.CreateSelectCommand("SELECT COUNT(*) FROM CI_Children");
		var count = (long)(await cmd.ExecuteScalarAsync())!;
		count.Should().Be(0);
	}

	// ─── CHECK CONSTRAINT ───

	[Fact]
	public async Task CheckConstraint_EnforcedViaSdk()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_CheckT (Id INT64 NOT NULL, Age INT64, CONSTRAINT CK_CI_Age CHECK (Age > 0)) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var cmd = conn.CreateInsertCommand("CI_CheckT");
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Age", SpannerDbType.Int64, -5L);

		var act = async () => await cmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── FOREIGN KEY ───

	[Fact]
	public async Task ForeignKey_EnforcedViaSdk()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_Depts (DeptId INT64 NOT NULL) PRIMARY KEY (DeptId)");
		await ExecuteDdlAsync(
			"CREATE TABLE CI_Emps (EmpId INT64 NOT NULL, DeptId INT64, CONSTRAINT FK_CI_Dept FOREIGN KEY (DeptId) REFERENCES CI_Depts (DeptId)) PRIMARY KEY (EmpId)");
		await InsertAsync("CI_Depts", new Dictionary<string, object?> { ["DeptId"] = 1L });

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		// Valid FK
		var cmd = conn.CreateInsertCommand("CI_Emps");
		cmd.Parameters.Add("EmpId", SpannerDbType.Int64, 100L);
		cmd.Parameters.Add("DeptId", SpannerDbType.Int64, 1L);
		await cmd.ExecuteNonQueryAsync();

		// Invalid FK
		var cmd2 = conn.CreateInsertCommand("CI_Emps");
		cmd2.Parameters.Add("EmpId", SpannerDbType.Int64, 200L);
		cmd2.Parameters.Add("DeptId", SpannerDbType.Int64, 999L);

		var act = async () => await cmd2.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_key
	//   "ON DELETE CASCADE: When parent row is deleted, child rows referencing it are also deleted."
	[Fact]
	public async Task ForeignKey_OnDeleteCascade_DeletesChildRows()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_FKCParent (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync(
			"CREATE TABLE CI_FKCChild (ChildId INT64 NOT NULL, ParentId INT64, CONSTRAINT FK_CI_Cascade FOREIGN KEY (ParentId) REFERENCES CI_FKCParent (Id) ON DELETE CASCADE) PRIMARY KEY (ChildId)");

		await InsertAsync("CI_FKCParent", new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync("CI_FKCChild", new Dictionary<string, object?> { ["ChildId"] = 10L, ["ParentId"] = 1L });
		await InsertAsync("CI_FKCChild", new Dictionary<string, object?> { ["ChildId"] = 20L, ["ParentId"] = 1L });

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var deleteCmd = conn.CreateDeleteCommand("CI_FKCParent");
		deleteCmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		await deleteCmd.ExecuteNonQueryAsync();

		var cmd = conn.CreateSelectCommand("SELECT COUNT(*) FROM CI_FKCChild");
		var count = (long)(await cmd.ExecuteScalarAsync())!;
		count.Should().Be(0);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#foreign_key
	//   "ON DELETE NO ACTION (default): Delete of a referenced row fails if referencing rows exist."
	[Fact]
	public async Task ForeignKey_OnDeleteNoAction_BlocksDelete()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_FKNParent (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync(
			"CREATE TABLE CI_FKNChild (ChildId INT64 NOT NULL, ParentId INT64, CONSTRAINT FK_CI_NoAction FOREIGN KEY (ParentId) REFERENCES CI_FKNParent (Id)) PRIMARY KEY (ChildId)");

		await InsertAsync("CI_FKNParent", new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync("CI_FKNChild", new Dictionary<string, object?> { ["ChildId"] = 10L, ["ParentId"] = 1L });

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var deleteCmd = conn.CreateDeleteCommand("CI_FKNParent");
		deleteCmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);

		var act = async () => await deleteCmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── STRING LENGTH ───

	[Fact]
	public async Task StringLength_EnforcedViaSdk()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_LenT (Id INT64 NOT NULL, Code STRING(5)) PRIMARY KEY (Id)");

		using var conn = Fixture.CreateConnection();
		await conn.OpenAsync();

		var cmd = conn.CreateInsertCommand("CI_LenT");
		cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
		cmd.Parameters.Add("Code", SpannerDbType.String, "TooLongString");

		var act = async () => await cmd.ExecuteNonQueryAsync();
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── ON CONFLICT ───

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
	[Fact]
	public async Task OnConflict_DoNothing_SkipsExistingRow()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_OC1 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync("INSERT INTO CI_OC1 (Id, Name) VALUES (1, 'Original')");

		// Insert conflicting row with ON CONFLICT DO NOTHING
		var count = await ExecuteDmlAsync(
			"INSERT INTO CI_OC1 (Id, Name) VALUES (1, 'New') ON CONFLICT (Id) DO NOTHING");
		count.Should().Be(0);

		// Verify original row is unchanged
		var result = await QueryAsync("SELECT Name FROM CI_OC1 WHERE Id = 1");
		result.Should().HaveCount(1);
		result[0]["Name"].Should().Be("Original");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
	[Fact]
	public async Task OnConflict_DoNothing_InsertsNewRow()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_OC2 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		// No conflict - should insert
		var count = await ExecuteDmlAsync(
			"INSERT INTO CI_OC2 (Id, Name) VALUES (1, 'New') ON CONFLICT (Id) DO NOTHING");
		count.Should().Be(1);

		var result = await QueryAsync("SELECT Name FROM CI_OC2 WHERE Id = 1");
		result.Should().HaveCount(1);
		result[0]["Name"].Should().Be("New");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_update
	[Fact]
	public async Task OnConflict_DoUpdate_UpdatesExistingRow()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_OC3 (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync("INSERT INTO CI_OC3 (Id, Name, Score) VALUES (1, 'Alice', 100)");

		// Insert conflicting row with ON CONFLICT DO UPDATE
		var count = await ExecuteDmlAsync(
			"INSERT INTO CI_OC3 (Id, Name, Score) VALUES (1, 'Bob', 200) ON CONFLICT (Id) DO UPDATE SET Name = EXCLUDED.Name, Score = EXCLUDED.Score");
		count.Should().Be(1);

		// Verify row was updated with EXCLUDED values
		var result = await QueryAsync("SELECT Name, Score FROM CI_OC3 WHERE Id = 1");
		result.Should().HaveCount(1);
		result[0]["Name"].Should().Be("Bob");
		result[0]["Score"].Should().Be(200L);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_update
	[Fact]
	public async Task OnConflict_DoUpdate_WithWhere_SkipsWhenConditionFalse()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_OC4 (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync("INSERT INTO CI_OC4 (Id, Name, Score) VALUES (1, 'Alice', 100)");

		// UPDATE WHERE condition is false (existing Score > EXCLUDED.Score)
		var count = await ExecuteDmlAsync(
			"INSERT INTO CI_OC4 (Id, Name, Score) VALUES (1, 'Bob', 50) ON CONFLICT (Id) DO UPDATE SET Name = EXCLUDED.Name, Score = EXCLUDED.Score WHERE EXCLUDED.Score > Score");
		count.Should().Be(0);

		// Verify row unchanged
		var result = await QueryAsync("SELECT Name, Score FROM CI_OC4 WHERE Id = 1");
		result[0]["Name"].Should().Be("Alice");
		result[0]["Score"].Should().Be(100L);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/dml-syntax#on_conflict_do_nothing
	[Fact]
	public async Task OnConflict_DoNothing_WithoutConflictTarget()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CI_OC5 (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync("INSERT INTO CI_OC5 (Id, Name) VALUES (1, 'Original')");

		// ON CONFLICT without specifying columns - should still work based on PK
		var count = await ExecuteDmlAsync(
			"INSERT INTO CI_OC5 (Id, Name) VALUES (1, 'New') ON CONFLICT DO NOTHING");
		count.Should().Be(0);

		var result = await QueryAsync("SELECT Name FROM CI_OC5 WHERE Id = 1");
		result[0]["Name"].Should().Be("Original");
	}
}
