using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for interleaved table cascade/no-action behavior (Phase 11).
/// </summary>
public class InterleavedTableTests
{
	private static InMemorySpannerDatabase CreateParentChildDatabase(string onDelete = "CASCADE")
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE Singers (SingerId INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (SingerId)");
		db.ExecuteDdl($"CREATE TABLE Albums (SingerId INT64 NOT NULL, AlbumId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (SingerId, AlbumId), INTERLEAVE IN PARENT Singers ON DELETE {onDelete}");
		return db;
	}

	// ─── CASCADE ───

	[Fact]
	public void DeleteParent_Cascade_DeletesChildRows()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#interleave_in
		//   "ON DELETE CASCADE: When a parent row is deleted, also delete the child rows."
		using var db = CreateParentChildDatabase("CASCADE");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 20L, ["Title"] = "A2" });

		db.Delete("Singers", 1L);

		var albums = db.ExecuteQuery("SELECT * FROM Albums");
		albums.Should().BeEmpty();
	}

	[Fact]
	public void DeleteParent_Cascade_LeavesOtherParentsChildren()
	{
		using var db = CreateParentChildDatabase("CASCADE");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 2L, ["Name"] = "Bob" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 2L, ["AlbumId"] = 20L, ["Title"] = "B1" });

		db.Delete("Singers", 1L);

		var albums = db.ExecuteQuery("SELECT Title FROM Albums");
		albums.Should().HaveCount(1);
		albums[0]["Title"].Should().Be("B1");
	}

	[Fact]
	public void DeleteParent_Cascade_ThreeLevels()
	{
		using var db = CreateParentChildDatabase("CASCADE");
		db.ExecuteDdl("CREATE TABLE Songs (SingerId INT64 NOT NULL, AlbumId INT64 NOT NULL, SongId INT64 NOT NULL, Title STRING(MAX)) PRIMARY KEY (SingerId, AlbumId, SongId), INTERLEAVE IN PARENT Albums ON DELETE CASCADE");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });
		db.Insert("Songs", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["SongId"] = 100L, ["Title"] = "S1" });

		db.Delete("Singers", 1L);

		db.ExecuteQuery("SELECT * FROM Albums").Should().BeEmpty();
		db.ExecuteQuery("SELECT * FROM Songs").Should().BeEmpty();
	}

	// ─── NO ACTION ───

	[Fact]
	public void DeleteParent_NoAction_ThrowsWhenChildrenExist()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#interleave_in
		//   "ON DELETE NO ACTION: If any child rows exist, the parent delete fails."
		using var db = CreateParentChildDatabase("NO ACTION");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });

		var act = () => db.Delete("Singers", 1L);

		act.Should().Throw<InvalidOperationException>().WithMessage("*child rows exist*NO ACTION*");
	}

	[Fact]
	public void DeleteParent_NoAction_SucceedsWhenNoChildren()
	{
		using var db = CreateParentChildDatabase("NO ACTION");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });

		db.Delete("Singers", 1L);

		db.ExecuteQuery("SELECT * FROM Singers").Should().BeEmpty();
	}

	// ─── DML DELETE with cascade ───

	[Fact]
	public void DmlDelete_Cascade_DeletesChildren()
	{
		using var db = CreateParentChildDatabase("CASCADE");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });

		db.ExecuteDml("DELETE FROM Singers WHERE SingerId = 1");

		db.ExecuteQuery("SELECT * FROM Albums").Should().BeEmpty();
	}

	[Fact]
	public void DmlDelete_NoAction_ThrowsWhenChildrenExist()
	{
		using var db = CreateParentChildDatabase("NO ACTION");
		db.Insert("Singers", new Dictionary<string, object?> { ["SingerId"] = 1L, ["Name"] = "Alice" });
		db.Insert("Albums", new Dictionary<string, object?> { ["SingerId"] = 1L, ["AlbumId"] = 10L, ["Title"] = "A1" });

		var act = () => db.ExecuteDml("DELETE FROM Singers WHERE SingerId = 1");

		act.Should().Throw<InvalidOperationException>().WithMessage("*child rows exist*NO ACTION*");
	}
}
