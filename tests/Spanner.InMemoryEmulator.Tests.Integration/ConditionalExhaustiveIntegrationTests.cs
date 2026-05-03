using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive conditional expression tests: IF, CASE, COALESCE, IFNULL, NULLIF, IIF.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ConditionalExhaustiveIntegrationTests : IntegrationTestBase
{
	public ConditionalExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── IF ───
	[Theory]
	[InlineData("IF(TRUE, 1, 0)", 1L)]
	[InlineData("IF(FALSE, 1, 0)", 0L)]
	[InlineData("IF(1 = 1, 'yes', 'no')", "yes")]
	[InlineData("IF(1 = 2, 'yes', 'no')", "no")]
	[InlineData("IF(TRUE, 'a', 'b')", "a")]
	[InlineData("IF(FALSE, 'a', 'b')", "b")]
	[InlineData("IF(1 > 0, 100, 200)", 100L)]
	[InlineData("IF(1 < 0, 100, 200)", 200L)]
	[InlineData("IF(NULL IS NULL, 'null', 'not null')", "null")]
	[InlineData("IF(1 IS NOT NULL, 'not null', 'null')", "not null")]
	[InlineData("IF(TRUE AND TRUE, 1, 0)", 1L)]
	[InlineData("IF(TRUE AND FALSE, 1, 0)", 0L)]
	[InlineData("IF(TRUE OR FALSE, 1, 0)", 1L)]
	[InlineData("IF(NOT FALSE, 1, 0)", 1L)]
	[InlineData("IF(NOT TRUE, 1, 0)", 0L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task If_Expression(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CASE simple ───
	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "other")]
	[InlineData("CASE 'a' WHEN 'a' THEN 1 WHEN 'b' THEN 2 ELSE 0 END", 1L)]
	[InlineData("CASE 'b' WHEN 'a' THEN 1 WHEN 'b' THEN 2 ELSE 0 END", 2L)]
	[InlineData("CASE 'c' WHEN 'a' THEN 1 WHEN 'b' THEN 2 ELSE 0 END", 0L)]
	[InlineData("CASE 1 WHEN 1 THEN 'match' END", "match")]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Case_Simple(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CASE searched ───
	[Theory]
	[InlineData("CASE WHEN 1 = 1 THEN 'a' WHEN 1 = 2 THEN 'b' ELSE 'c' END", "a")]
	[InlineData("CASE WHEN 1 = 2 THEN 'a' WHEN 1 = 1 THEN 'b' ELSE 'c' END", "b")]
	[InlineData("CASE WHEN 1 = 2 THEN 'a' WHEN 1 = 3 THEN 'b' ELSE 'c' END", "c")]
	[InlineData("CASE WHEN TRUE THEN 1 ELSE 0 END", 1L)]
	[InlineData("CASE WHEN FALSE THEN 1 ELSE 0 END", 0L)]
	[InlineData("CASE WHEN 5 > 3 THEN 'big' ELSE 'small' END", "big")]
	[InlineData("CASE WHEN 5 < 3 THEN 'big' ELSE 'small' END", "small")]
	[InlineData("CASE WHEN 1 > 0 THEN 'pos' WHEN 1 < 0 THEN 'neg' ELSE 'zero' END", "pos")]
	[InlineData("CASE WHEN -1 > 0 THEN 'pos' WHEN -1 < 0 THEN 'neg' ELSE 'zero' END", "neg")]
	[InlineData("CASE WHEN 0 > 0 THEN 'pos' WHEN 0 < 0 THEN 'neg' ELSE 'zero' END", "zero")]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Case_Searched(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CASE without ELSE (returns NULL) ───
	[Theory]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' END")]
	[InlineData("CASE WHEN FALSE THEN 1 END")]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Case_NoElse_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── COALESCE ───
	[Theory]
	[InlineData("COALESCE(1, 2, 3)", 1L)]
	[InlineData("COALESCE(NULL, 2, 3)", 2L)]
	[InlineData("COALESCE(NULL, NULL, 3)", 3L)]
	[InlineData("COALESCE('a', 'b')", "a")]
	[InlineData("COALESCE(NULL, 'b')", "b")]
	[InlineData("COALESCE(NULL, NULL, NULL, 'last')", "last")]
	[InlineData("COALESCE(1)", 1L)]
	[InlineData("COALESCE(NULL, 0)", 0L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Coalesce(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Coalesce_AllNull_ReturnsNull()
	{
		var result = await Eval("COALESCE(NULL, NULL, NULL)");
		result.Should().BeNull();
	}

	// ─── IFNULL ───
	[Theory]
	[InlineData("IFNULL(1, 99)", 1L)]
	[InlineData("IFNULL(NULL, 99)", 99L)]
	[InlineData("IFNULL('a', 'b')", "a")]
	[InlineData("IFNULL(NULL, 'b')", "b")]
	[InlineData("IFNULL(0, 99)", 0L)]
	[InlineData("IFNULL(NULL, 0)", 0L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Ifnull(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NULLIF ───
	[Theory]
	[InlineData("NULLIF(1, 1)")]
	[InlineData("NULLIF('a', 'a')")]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Nullif_Equal_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("NULLIF(1, 2)", 1L)]
	[InlineData("NULLIF('a', 'b')", "a")]
	[InlineData("NULLIF(0, 1)", 0L)]
	[InlineData("NULLIF(100, 200)", 100L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Nullif_NotEqual_ReturnsFirst(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Boolean logic ───
	[Theory]
	[InlineData("TRUE AND TRUE", true)]
	[InlineData("TRUE AND FALSE", false)]
	[InlineData("FALSE AND TRUE", false)]
	[InlineData("FALSE AND FALSE", false)]
	[InlineData("TRUE OR TRUE", true)]
	[InlineData("TRUE OR FALSE", true)]
	[InlineData("FALSE OR TRUE", true)]
	[InlineData("FALSE OR FALSE", false)]
	[InlineData("NOT TRUE", false)]
	[InlineData("NOT FALSE", true)]
	[InlineData("NOT NOT TRUE", true)]
	[InlineData("TRUE AND NOT FALSE", true)]
	[InlineData("FALSE OR NOT FALSE", true)]
	[InlineData("(TRUE OR FALSE) AND TRUE", true)]
	[InlineData("(TRUE AND FALSE) OR TRUE", true)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task BooleanLogic(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── NULL boolean logic ───
	[Theory]
	[InlineData("NULL AND TRUE")]
	[InlineData("TRUE AND NULL")]
	[InlineData("NULL OR NULL")]
	[InlineData("NULL AND NULL")]
	[InlineData("NOT NULL")]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task NullBooleanLogic_ReturnsNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	[Theory]
	[InlineData("FALSE AND NULL", false)]
	[InlineData("NULL AND FALSE", false)]
	[InlineData("TRUE OR NULL", true)]
	[InlineData("NULL OR TRUE", true)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task NullBooleanLogic_Shortcircuit(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── IS NULL / IS NOT NULL ───
	[Theory]
	[InlineData("NULL IS NULL", true)]
	[InlineData("1 IS NULL", false)]
	[InlineData("'' IS NULL", false)]
	[InlineData("NULL IS NOT NULL", false)]
	[InlineData("1 IS NOT NULL", true)]
	[InlineData("'' IS NOT NULL", true)]
	[InlineData("0 IS NULL", false)]
	[InlineData("FALSE IS NULL", false)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task IsNull(string expr, bool expected)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		rows[0]["R"].Should().Be(expected);
	}

	// ─── Nested conditionals ───
	[Theory]
	[InlineData("IF(COALESCE(NULL, TRUE), 'yes', 'no')", "yes")]
	[InlineData("COALESCE(IF(FALSE, 1, NULL), 99)", 99L)]
	[InlineData("CASE WHEN IFNULL(NULL, 5) > 3 THEN 'big' ELSE 'small' END", "big")]
	[InlineData("IF(NULLIF(1, 1) IS NULL, 'null', 'not null')", "null")]
	[InlineData("IF(NULLIF(1, 2) IS NULL, 'null', 'not null')", "not null")]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 2), 3)", 3L)]
	[InlineData("COALESCE(NULLIF(1, 1), NULLIF(2, 3))", 2L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task NestedConditionals(string expr, object expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── IF with type coercion ───
	[Theory]
	[InlineData("IF(TRUE, 1, 2) + 10", 11L)]
	[InlineData("IF(FALSE, 1, 2) * 5", 10L)]
	[InlineData("LENGTH(IF(TRUE, 'hello', 'hi'))", 5L)]
	[InlineData("LENGTH(IF(FALSE, 'hello', 'hi'))", 2L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task If_InExpression(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CASE in expression context ───
	[Theory]
	[InlineData("CASE WHEN 1=1 THEN 10 ELSE 20 END + 5", 15L)]
	[InlineData("CASE WHEN 1=2 THEN 10 ELSE 20 END + 5", 25L)]
	[InlineData("ABS(CASE WHEN TRUE THEN -5 ELSE 5 END)", 5L)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Case_InExpression(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── Implicit coercion (INT64 + FLOAT64) ───
	[Theory]
	[InlineData("IF(TRUE, 1, 2.0)", 1.0)]
	[InlineData("IF(FALSE, 1, 2.0)", 2.0)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task If_ImplicitCoercion_ReturnsFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		result.Should().BeOfType<double>();
		((double)result!).Should().Be(expected);
	}

	[Theory]
	[InlineData("COALESCE(1, 2.0)", 1.0)]
	[InlineData("COALESCE(NULL, 2.0)", 2.0)]
	[Trait(TestTraits.Category, "ConditionalExhaustive")]
	public async Task Coalesce_ImplicitCoercion_ReturnsFloat64(string expr, double expected)
	{
		var result = await Eval(expr);
		result.Should().BeOfType<double>();
		((double)result!).Should().Be(expected);
	}
}
