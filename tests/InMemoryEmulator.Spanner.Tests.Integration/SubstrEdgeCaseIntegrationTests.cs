using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Tests for SUBSTR edge cases around position 0, very negative positions, and length interactions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
///   "If position is 0 or less than -LENGTH(value), position is set to 1"
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SubstrEdgeCaseIntegrationTests : IntegrationTestBase
{
	public SubstrEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── SUBSTR position = 0 ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
	//   "If position is 0 or less than -LENGTH(value), position is set to 1"

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_Position0_WithLength_TreatsAs1()
	{
		// SUBSTR('apple', 0, 3) → position is set to 1 → 'app'
		var result = await Eval("SUBSTR('apple', 0, 3)");
		result.Should().Be("app");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_Position0_NoLength_TreatsAs1()
	{
		// SUBSTR('apple', 0) → position is set to 1 → 'apple'
		var result = await Eval("SUBSTR('apple', 0)");
		result.Should().Be("apple");
	}

	// ─── SUBSTR position very negative (less than -LENGTH) ───

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_VeryNegativePosition_WithLength_TreatsAs1()
	{
		// SUBSTR('apple', -10, 3) → -10 < -5 → position is set to 1 → 'app'
		var result = await Eval("SUBSTR('apple', -10, 3)");
		result.Should().Be("app");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_VeryNegativePosition_NoLength_TreatsAs1()
	{
		// SUBSTR('apple', -10) → -10 < -5 → position is set to 1 → 'apple'
		var result = await Eval("SUBSTR('apple', -10)");
		result.Should().Be("apple");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_PositionExactlyNegativeLength_TreatsAs1()
	{
		// SUBSTR('apple', -5) → -5 == -LENGTH(5), NOT less than → counts from end
		// -5 from end = 1st char → 'apple'
		var result = await Eval("SUBSTR('apple', -5)");
		result.Should().Be("apple");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_PositionNeg6_LessThanNegLength_TreatsAs1()
	{
		// SUBSTR('apple', -6) → -6 < -5 → position is set to 1 → 'apple'
		var result = await Eval("SUBSTR('apple', -6)");
		result.Should().Be("apple");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_PositionNeg6_WithLength3_TreatsAs1()
	{
		// SUBSTR('apple', -6, 3) → -6 < -5 → position is set to 1 → 'app'
		var result = await Eval("SUBSTR('apple', -6, 3)");
		result.Should().Be("app");
	}

	[Theory]
	[InlineData("SUBSTR('hello', 0, 5)", "hello")]
	[InlineData("SUBSTR('hello', -100, 5)", "hello")]
	[InlineData("SUBSTR('hello', -100)", "hello")]
	[InlineData("SUBSTR('x', 0, 1)", "x")]
	[InlineData("SUBSTR('x', -100, 1)", "x")]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Substr_PositionClampedTo1_ReturnsFromStart(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}
}
