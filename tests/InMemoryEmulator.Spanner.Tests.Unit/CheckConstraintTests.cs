using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for CHECK constraints (Phase 11).
/// </summary>
public class CheckConstraintTests
{
	[Fact]
	public void CheckConstraint_Passes_WhenConditionIsTrue()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Age INT64, CONSTRAINT CK_Age CHECK (Age > 0)) PRIMARY KEY (Id)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Age"] = 25L });

		var rows = db.ExecuteQuery("SELECT Age FROM T WHERE Id = 1");
		rows[0]["Age"].Should().Be(25L);
	}

	[Fact]
	public void CheckConstraint_Passes_WhenNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#check_constraint
		//   "The CHECK constraint expression must evaluate to TRUE or NULL for any row."
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Age INT64, CONSTRAINT CK_Age CHECK (Age > 0)) PRIMARY KEY (Id)");

		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Age"] = null });

		var rows = db.ExecuteQuery("SELECT COUNT(*) AS C FROM T");
		rows[0]["C"].Should().Be(1L);
	}

	[Fact]
	public void CheckConstraint_Fails_WhenConditionIsFalse()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Age INT64, CONSTRAINT CK_Age CHECK (Age > 0)) PRIMARY KEY (Id)");

		var act = () => db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Age"] = -5L });

		act.Should().Throw<InvalidOperationException>().WithMessage("*CHECK constraint*CK_Age*");
	}

	[Fact]
	public void CheckConstraint_Unnamed_Fails()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Val INT64, CHECK (Val >= 0)) PRIMARY KEY (Id)");

		var act = () => db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = -1L });

		act.Should().Throw<InvalidOperationException>().WithMessage("*CHECK constraint*");
	}

	[Fact]
	public void CheckConstraint_Update_Fails()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, Age INT64, CONSTRAINT CK_Age CHECK (Age > 0)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["Age"] = 25L });

		var act = () => db.ExecuteDml("UPDATE T SET Age = -1 WHERE Id = 1");

		act.Should().Throw<InvalidOperationException>().WithMessage("*CHECK constraint*CK_Age*");
	}
}
