using FluentAssertions;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for CREATE SEQUENCE / GET_NEXT_SEQUENCE_VALUE / GET_INTERNAL_SEQUENCE_STATE.
/// </summary>
public class SequenceTests
{
	[Fact]
	public void CreateSequence_GetNextValue_ReturnsBitReversedPositive()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });
		db.ExecuteDdl("CREATE SEQUENCE MySeq OPTIONS (sequence_kind='bit_reversed_positive')");

		var val = db.ExecuteScalar<long>("SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE MySeq) FROM T WHERE Id=1");
		val.Should().BePositive(); // Bit-reversed value of 2 (counter starts at 1, increments to 2)
	}

	[Fact]
	public void Sequence_ValuesAreUnique()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });
		db.ExecuteDdl("CREATE SEQUENCE UniqueSeq OPTIONS (sequence_kind='bit_reversed_positive')");

		var values = new HashSet<long>();
		for (int i = 0; i < 10; i++)
		{
			var val = db.ExecuteScalar<long>("SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE UniqueSeq) FROM T WHERE Id=1");
			values.Add(val).Should().BeTrue($"Value {val} should be unique (iteration {i})");
		}
	}

	[Fact]
	public void GetInternalSequenceState_ReturnsCounter()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });
		db.ExecuteDdl("CREATE SEQUENCE StateSeq OPTIONS (sequence_kind='bit_reversed_positive', start_with_counter=100)");

		var state = db.ExecuteScalar<long>("SELECT GET_INTERNAL_SEQUENCE_STATE(SEQUENCE StateSeq) FROM T WHERE Id=1");
		state.Should().Be(100L);
	}

	[Fact]
	public void DropSequence_ThenQueryThrows()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L });
		db.ExecuteDdl("CREATE SEQUENCE TempSeq OPTIONS (sequence_kind='bit_reversed_positive')");
		db.ExecuteDdl("DROP SEQUENCE TempSeq");

		var act = () => db.ExecuteScalar<long>("SELECT GET_NEXT_SEQUENCE_VALUE(SEQUENCE TempSeq) FROM T WHERE Id=1");
		act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
	}

	[Fact]
	public void CreateDuplicateSequence_Throws()
	{
		using var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE SEQUENCE DupSeq OPTIONS (sequence_kind='bit_reversed_positive')");
		var act = () => db.ExecuteDdl("CREATE SEQUENCE DupSeq OPTIONS (sequence_kind='bit_reversed_positive')");
		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
	}
}
