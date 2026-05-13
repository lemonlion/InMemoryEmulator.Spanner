using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for change streams DDL and INFORMATION_SCHEMA support.
/// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
/// Note: Cloud Spanner limits to 3 change streams per column/table.
///   Tests use unique tables per test to avoid hitting this limit.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class ChangeStreamIntegrationTests : IntegrationTestBase
{
	public ChangeStreamIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── DDL: CREATE CHANGE STREAM ───

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#watch-entire-database
	//   CREATE CHANGE STREAM EverythingStream FOR ALL;
	[Fact]
	public async Task CreateChangeStream_ForAll_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsAll1 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"CREATE CHANGE STREAM EverythingStream FOR CsAll1");
		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#watch-specific-tables
	//   CREATE CHANGE STREAM SingerStream FOR Singers;
	[Fact]
	public async Task CreateChangeStream_ForSpecificTable_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsSingers (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"CREATE CHANGE STREAM SingerStream FOR CsSingers");
		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#watch-specific-columns
	//   CREATE CHANGE STREAM NamesStream FOR Singers(FirstName, LastName);
	[Fact]
	public async Task CreateChangeStream_ForSpecificColumns_DoesNotThrow()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE CsSingers2 (Id INT64 NOT NULL, FirstName STRING(100), LastName STRING(100)) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"CREATE CHANGE STREAM NamesStream FOR CsSingers2(FirstName, LastName)");
		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#specify-longer-retention
	//   CREATE CHANGE STREAM ... OPTIONS (retention_period = '7d');
	[Fact]
	public async Task CreateChangeStream_WithOptions_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsRetention (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"CREATE CHANGE STREAM RetentionStream FOR CsRetention OPTIONS (retention_period = '7d')");
		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#specify-value-capture-type
	//   OPTIONS (value_capture_type = 'NEW_ROW')
	[Fact]
	public async Task CreateChangeStream_WithValueCaptureType_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsCapture (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"CREATE CHANGE STREAM CaptureStream FOR CsCapture OPTIONS (value_capture_type = 'NEW_ROW')");
		await act.Should().NotThrowAsync();
	}

	// ─── DDL: ALTER CHANGE STREAM ───

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#modify-what-stream-watches
	[Fact]
	public async Task AlterChangeStream_SetForAll_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsAlter (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE CHANGE STREAM AlterTestStream FOR CsAlter");
		await ExecuteDdlAsync("CREATE TABLE CsAlter2 (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = async () => await ExecuteDdlAsync(
			"ALTER CHANGE STREAM AlterTestStream SET FOR CsAlter2");
		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#suspend
	[Fact]
	public async Task AlterChangeStream_DropForAll_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsSuspend (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE CHANGE STREAM SuspendTestStream FOR CsSuspend");
		var act = async () => await ExecuteDdlAsync(
			"ALTER CHANGE STREAM SuspendTestStream DROP FOR ALL");
		await act.Should().NotThrowAsync();
	}

	// ─── DDL: DROP CHANGE STREAM ───

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#delete
	[Fact]
	public async Task DropChangeStream_DoesNotThrow()
	{
		await ExecuteDdlAsync("CREATE TABLE CsDrop (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE CHANGE STREAM DropTestStream FOR CsDrop");
		var act = async () => await ExecuteDdlAsync(
			"DROP CHANGE STREAM DropTestStream");
		await act.Should().NotThrowAsync();
	}

	// ─── INFORMATION_SCHEMA ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_streams
	[Fact]
	public async Task InformationSchema_ChangeStreams_ReturnsDefined()
	{
		await ExecuteDdlAsync("CREATE TABLE CsInfoSchema (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		await ExecuteDdlAsync("CREATE CHANGE STREAM InfoSchemaStream FOR CsInfoSchema");

		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT CHANGE_STREAM_NAME FROM INFORMATION_SCHEMA.CHANGE_STREAMS WHERE CHANGE_STREAM_NAME = 'InfoSchemaStream'");
		using var reader = await cmd.ExecuteReaderAsync();
		var found = await reader.ReadAsync();
		found.Should().BeTrue();
		reader.GetString(0).Should().Be("InfoSchemaStream");
	}
}
