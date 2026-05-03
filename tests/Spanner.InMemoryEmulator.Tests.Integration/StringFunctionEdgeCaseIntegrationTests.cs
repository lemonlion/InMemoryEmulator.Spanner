using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for string function edge cases: LPAD/RPAD negative length, SPLIT NULL delimiter.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StringFunctionEdgeCaseIntegrationTests : IntegrationTestBase
{
	public StringFunctionEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── LPAD negative return_length → error ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
	//   "This function returns an error if: return_length is negative"

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Lpad_NegativeReturnLength_ThrowsError()
	{
		Func<Task> act = async () => await Eval("LPAD('abc', -1)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Lpad_NegativeReturnLength_WithPattern_ThrowsError()
	{
		Func<Task> act = async () => await Eval("LPAD('abc', -5, 'x')");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── RPAD negative return_length → error ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#rpad
	//   "This function returns an error if: return_length is negative"

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Rpad_NegativeReturnLength_ThrowsError()
	{
		Func<Task> act = async () => await Eval("RPAD('abc', -1)");
		await act.Should().ThrowAsync<SpannerException>();
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Rpad_NegativeReturnLength_WithPattern_ThrowsError()
	{
		Func<Task> act = async () => await Eval("RPAD('abc', -5, 'x')");
		await act.Should().ThrowAsync<SpannerException>();
	}

	// ─── LPAD/RPAD zero return_length → empty string (NOT an error) ───
	// Ref: "If return_length is less than or equal to the original_value length, 
	//   this function returns the original_value value, truncated to the value of return_length."
	//   For return_length = 0, that means return empty.

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Lpad_ZeroReturnLength_ReturnsEmpty()
	{
		var result = await Eval("LPAD('abc', 0)");
		result.Should().Be("");
	}

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Rpad_ZeroReturnLength_ReturnsEmpty()
	{
		var result = await Eval("RPAD('abc', 0)");
		result.Should().Be("");
	}

	// ─── SPLIT with NULL delimiter → NULL ───
	// Ref: Standard SQL NULL propagation. Any function argument being NULL
	//   returns NULL unless otherwise documented.

	[Fact]
	[Trait(TestTraits.Category, "StringFunction")]
	public async Task Split_NullDelimiter_ReturnsNull()
	{
		var result = await Eval("SPLIT('a,b,c', CAST(NULL AS STRING))");
		result.Should().BeNull();
	}
}
