using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for STRING/BYTES length validation (Phase 11).
/// </summary>
public class LengthValidationTests
{
	[Fact]
	public void String_WithinLength_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(10)) PRIMARY KEY (Id)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Hello" });

		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 1");
		rows[0]["Name"].Should().Be("Hello");
	}

	[Fact]
	public void String_ExceedsLength_Throws()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_table
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(5)) PRIMARY KEY (Id)");

		var act = () => db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "TooLongString" });

		act.Should().Throw<InvalidOperationException>().WithMessage("*exceeds maximum length*5*");
	}

	[Fact]
	public void String_ExactLength_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(5)) PRIMARY KEY (Id)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Hello" });

		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 1");
		rows[0]["Name"].Should().Be("Hello");
	}

	[Fact]
	public void String_Max_NoLengthLimit()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

		var longString = new string('x', 10000);
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = longString });

		var rows = db.ExecuteQuery("SELECT Name FROM T WHERE Id = 1");
		((string)rows[0]["Name"]!).Length.Should().Be(10000);
	}

	[Fact]
	public void String_Null_Succeeds()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Name STRING(5)) PRIMARY KEY (Id)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = null });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		rows[0]["C"].Should().Be(1L);
	}
}
