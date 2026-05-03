using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Edge-case tests for array functions: NULL handling, boundary conditions, empty arrays.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArrayFunctionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public ArrayFunctionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_LENGTH
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_length
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_LENGTH([])", 0L)]
	[InlineData("ARRAY_LENGTH([1])", 1L)]
	[InlineData("ARRAY_LENGTH([1, 2, 3])", 3L)]
	[InlineData("ARRAY_LENGTH([1, 2, 3, 4, 5])", 5L)]
	[InlineData("ARRAY_LENGTH([NULL, NULL])", 2L)]
	[InlineData("ARRAY_LENGTH([1, NULL, 3])", 3L)]
	public async Task ArrayLength_Values(string expr, long expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ArrayLength_Null_ReturnsNull()
	{
		(await Eval("ARRAY_LENGTH(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_reverse
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayReverse_EmptyArray_ReturnsEmpty()
	{
		var result = await Eval("ARRAY_LENGTH(ARRAY_REVERSE([]))");
		(result).Should().Be(0L);
	}

	[Fact]
	public async Task ArrayReverse_Null_ReturnsNull()
	{
		(await Eval("ARRAY_REVERSE(NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_FIRST / ARRAY_LAST - NULL input should return NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
	// BUG: Currently throws instead of returning NULL for NULL input
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayFirst_NullArray_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
		//   Scalar functions return NULL when any argument is NULL.
		(await Eval("ARRAY_FIRST(NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task ArrayLast_NullArray_ReturnsNull()
	{
		(await Eval("ARRAY_LAST(NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task ArrayFirst_SingleElement_ReturnsIt()
	{
		(await Eval("ARRAY_FIRST([42])")).Should().Be(42L);
	}

	[Fact]
	public async Task ArrayLast_SingleElement_ReturnsIt()
	{
		(await Eval("ARRAY_LAST([42])")).Should().Be(42L);
	}

	[Fact]
	public async Task ArrayFirst_MultipleElements_ReturnsFirst()
	{
		(await Eval("ARRAY_FIRST([10, 20, 30])")).Should().Be(10L);
	}

	[Fact]
	public async Task ArrayLast_MultipleElements_ReturnsLast()
	{
		(await Eval("ARRAY_LAST([10, 20, 30])")).Should().Be(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// GENERATE_ARRAY - NULL step should return NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
	// BUG: Currently throws instead of returning NULL for NULL step
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GenerateArray_NullStep_ReturnsNull()
	{
		(await Eval("GENERATE_ARRAY(1, 10, NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task GenerateArray_NullStart_ReturnsNull()
	{
		(await Eval("GENERATE_ARRAY(NULL, 10)")).Should().BeNull();
	}

	[Fact]
	public async Task GenerateArray_NullEnd_ReturnsNull()
	{
		(await Eval("GENERATE_ARRAY(1, NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task GenerateArray_StepOne()
	{
		var result = await Eval("ARRAY_LENGTH(GENERATE_ARRAY(1, 5))");
		result.Should().Be(5L);
	}

	[Fact]
	public async Task GenerateArray_StepTwo()
	{
		var result = await Eval("ARRAY_LENGTH(GENERATE_ARRAY(1, 10, 2))");
		result.Should().Be(5L); // 1, 3, 5, 7, 9
	}

	[Fact]
	public async Task GenerateArray_NegativeStep()
	{
		var result = await Eval("ARRAY_LENGTH(GENERATE_ARRAY(5, 1, -1))");
		result.Should().Be(5L); // 5, 4, 3, 2, 1
	}

	[Fact]
	public async Task GenerateArray_StartEqualsEnd()
	{
		var result = await Eval("ARRAY_LENGTH(GENERATE_ARRAY(5, 5))");
		result.Should().Be(1L); // [5]
	}

	[Fact]
	public async Task GenerateArray_EmptyWhenStepWrongDirection()
	{
		var result = await Eval("ARRAY_LENGTH(GENERATE_ARRAY(5, 1, 1))");
		result.Should().Be(0L); // empty
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_TO_STRING - NULL separator should return NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
	// BUG: Currently treats NULL sep as empty string instead of returning NULL
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayToString_NullSeparator_ReturnsNull()
	{
		(await Eval("ARRAY_TO_STRING(['a', 'b', 'c'], NULL)")).Should().BeNull();
	}

	[Theory]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], ',')", "a,b,c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], '-')", "a-b-c")]
	[InlineData("ARRAY_TO_STRING(['a', 'b', 'c'], '')", "abc")]
	[InlineData("ARRAY_TO_STRING(['a'], ',')", "a")]
	[InlineData("ARRAY_TO_STRING([], ',')", "")]
	public async Task ArrayToString_ValidInputs(string expr, string expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ArrayToString_NullArray_ReturnsNull()
	{
		(await Eval("ARRAY_TO_STRING(NULL, ',')")).Should().BeNull();
	}

	[Fact]
	public async Task ArrayToString_WithNullElements_SkipsNulls()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
		//   "NULL array elements are omitted from the resulting string."
		(await Eval("ARRAY_TO_STRING(['a', NULL, 'c'], ',')")).Should().Be("a,c");
	}

	[Fact]
	public async Task ArrayToString_WithNullTextParam_ReplacesNulls()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
		//   "If null_text is provided, NULL array elements are included."
		(await Eval("ARRAY_TO_STRING(['a', NULL, 'c'], ',', 'X')")).Should().Be("a,X,c");
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_INCLUDES / ARRAY_INCLUDES_ANY / ARRAY_INCLUDES_ALL
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("ARRAY_INCLUDES([1, 2, 3], 1)", true)]
	[InlineData("ARRAY_INCLUDES([1, 2, 3], 4)", false)]
	[InlineData("ARRAY_INCLUDES([1, 2, 3], 2)", true)]
	[InlineData("ARRAY_INCLUDES([], 1)", false)]
	public async Task ArrayIncludes_Values(string expr, bool expected)
	{
		(await Eval(expr)).Should().Be(expected);
	}

	[Fact]
	public async Task ArrayIncludes_NullArray_ReturnsNull()
	{
		(await Eval("ARRAY_INCLUDES(NULL, 1)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_CONCAT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_concat
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArrayConcat_TwoArrays()
	{
		var result = await Eval("ARRAY_LENGTH(ARRAY_CONCAT([1, 2], [3, 4]))");
		result.Should().Be(4L);
	}

	[Fact]
	public async Task ArrayConcat_EmptyArrays()
	{
		var result = await Eval("ARRAY_LENGTH(ARRAY_CONCAT([], []))");
		result.Should().Be(0L);
	}

	[Fact]
	public async Task ArrayConcat_NullArray_ReturnsNull()
	{
		(await Eval("ARRAY_CONCAT(NULL, [1])")).Should().BeNull();
	}

	[Fact]
	public async Task ArrayConcat_SecondNull_ReturnsNull()
	{
		(await Eval("ARRAY_CONCAT([1], NULL)")).Should().BeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY_SLICE - NULL args should return NULL
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_slice
	// BUG: Currently throws for NULL start/end instead of returning NULL
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArraySlice_NullStart_ReturnsNull()
	{
		(await Eval("ARRAY_SLICE([1, 2, 3, 4, 5], NULL, 3)")).Should().BeNull();
	}

	[Fact]
	public async Task ArraySlice_NullEnd_ReturnsNull()
	{
		(await Eval("ARRAY_SLICE([1, 2, 3, 4, 5], 0, NULL)")).Should().BeNull();
	}

	[Fact]
	public async Task ArraySlice_NullArray_ReturnsNull()
	{
		(await Eval("ARRAY_SLICE(NULL, 0, 2)")).Should().BeNull();
	}
}
