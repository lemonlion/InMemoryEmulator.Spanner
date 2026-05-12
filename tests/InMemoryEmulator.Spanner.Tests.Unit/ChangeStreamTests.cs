using FluentAssertions;
using Xunit;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for change stream DDL and INFORMATION_SCHEMA support.
/// Ref: https://cloud.google.com/spanner/docs/change-streams/manage
/// </summary>
public class ChangeStreamTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		return db;
	}

	// ─── DDL: CREATE ───

	// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#watch-entire-database
	[Fact]
	public void CreateChangeStream_ForAll_Succeeds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteDdl("CREATE CHANGE STREAM EverythingStream FOR ALL");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreateChangeStream_ForSpecificTable_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var act = () => db.ExecuteDdl("CREATE CHANGE STREAM TStream FOR T");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreateChangeStream_ForColumns_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(100)) PRIMARY KEY (Id)");
		var act = () => db.ExecuteDdl("CREATE CHANGE STREAM ColStream FOR T(Name)");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreateChangeStream_WithOptions_Succeeds()
	{
		using var db = CreateDb();
		var act = () => db.ExecuteDdl(
			"CREATE CHANGE STREAM OptStream FOR ALL OPTIONS (retention_period = '7d', value_capture_type = 'NEW_ROW')");
		act.Should().NotThrow();
	}

	[Fact]
	public void CreateChangeStream_Suspended_Succeeds()
	{
		using var db = CreateDb();
		// Ref: https://cloud.google.com/spanner/docs/change-streams/manage#suspend
		//   "You can create a change stream in a suspended state by omitting the FOR clause"
		var act = () => db.ExecuteDdl("CREATE CHANGE STREAM SuspendedStream");
		act.Should().NotThrow();
	}

	// ─── DDL: ALTER ───

	[Fact]
	public void AlterChangeStream_SetForAll_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM MyStream FOR ALL");
		var act = () => db.ExecuteDdl("ALTER CHANGE STREAM MyStream SET FOR ALL");
		act.Should().NotThrow();
	}

	[Fact]
	public void AlterChangeStream_DropForAll_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM MyStream FOR ALL");
		var act = () => db.ExecuteDdl("ALTER CHANGE STREAM MyStream DROP FOR ALL");
		act.Should().NotThrow();
	}

	[Fact]
	public void AlterChangeStream_SetOptions_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM MyStream FOR ALL");
		var act = () => db.ExecuteDdl(
			"ALTER CHANGE STREAM MyStream SET OPTIONS (retention_period = '36h')");
		act.Should().NotThrow();
	}

	// ─── DDL: DROP ───

	[Fact]
	public void DropChangeStream_Succeeds()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM MyStream FOR ALL");
		var act = () => db.ExecuteDdl("DROP CHANGE STREAM MyStream");
		act.Should().NotThrow();
	}

	// ─── INFORMATION_SCHEMA ───

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_streams
	[Fact]
	public void InformationSchema_ChangeStreams_ReturnsDefined()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM TestStream FOR ALL");
		var rows = db.ExecuteQuery(
			"SELECT CHANGE_STREAM_NAME FROM INFORMATION_SCHEMA.CHANGE_STREAMS WHERE CHANGE_STREAM_NAME = 'TestStream'");
		rows.Should().ContainSingle();
		rows[0]["CHANGE_STREAM_NAME"].Should().Be("TestStream");
	}

	[Fact]
	public void InformationSchema_ChangeStreams_ReturnsAll()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM Stream1 FOR ALL");
		db.ExecuteDdl("CREATE CHANGE STREAM Stream2 FOR ALL");
		var rows = db.ExecuteQuery("SELECT CHANGE_STREAM_NAME FROM INFORMATION_SCHEMA.CHANGE_STREAMS");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public void InformationSchema_ChangeStreams_AfterDrop_IsEmpty()
	{
		using var db = CreateDb();
		db.ExecuteDdl("CREATE CHANGE STREAM TestStream FOR ALL");
		db.ExecuteDdl("DROP CHANGE STREAM TestStream");
		var rows = db.ExecuteQuery("SELECT CHANGE_STREAM_NAME FROM INFORMATION_SCHEMA.CHANGE_STREAMS");
		rows.Should().BeEmpty();
	}
}
