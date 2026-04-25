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
}
