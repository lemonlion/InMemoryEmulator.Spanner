using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Zstandard compression functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-all
///   ZSTD_COMPRESS, ZSTD_DECOMPRESS_TO_BYTES, ZSTD_DECOMPRESS_TO_STRING
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class CompressionFunctionIntegrationTests : IntegrationTestBase
{
	public CompressionFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ZSTD_COMPRESS
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   Compresses STRING or BYTES input into BYTES output using Zstandard.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ZstdCompress_StringInput_ReturnsBytes()
	{
		var result = await Eval("ZSTD_COMPRESS('hello world')");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().NotBeEmpty();
	}

	[Fact]
	public async Task ZstdCompress_BytesInput_ReturnsBytes()
	{
		var result = await Eval("ZSTD_COMPRESS(b'hello world')");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().NotBeEmpty();
	}

	[Fact]
	public async Task ZstdCompress_NullInput_ReturnsNull()
	{
		var result = await Eval("ZSTD_COMPRESS(NULL)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task ZstdCompress_EmptyString_ReturnsBytes()
	{
		var result = await Eval("ZSTD_COMPRESS('')");
		result.Should().BeOfType<byte[]>();
	}

	// ═══════════════════════════════════════════════════════════════
	// ZSTD_DECOMPRESS_TO_STRING
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   Decompresses BYTES input into STRING output using Zstandard.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ZstdDecompressToString_RoundTrips()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_STRING(ZSTD_COMPRESS('hello world'))");
		result.Should().Be("hello world");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task ZstdDecompressToString_EmptyString_RoundTrips()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_STRING(ZSTD_COMPRESS(''))");
		result.Should().Be("");
	}

	[Fact]
	public async Task ZstdDecompressToString_NullInput_ReturnsNull()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_STRING(NULL)");
		result.Should().BeNull();
	}

	[Fact]
	public async Task ZstdDecompressToString_Unicode_RoundTrips()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_STRING(ZSTD_COMPRESS('こんにちは世界'))");
		result.Should().Be("こんにちは世界");
	}

	// ═══════════════════════════════════════════════════════════════
	// ZSTD_DECOMPRESS_TO_BYTES
	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   Decompresses BYTES input into BYTES output using Zstandard.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ZstdDecompressToBytes_RoundTrips()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_BYTES(ZSTD_COMPRESS(b'hello world'))");
		result.Should().BeOfType<byte[]>();
		System.Text.Encoding.UTF8.GetString((byte[])result!).Should().Be("hello world");
	}

	[Fact]
	public async Task ZstdDecompressToBytes_NullInput_ReturnsNull()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_BYTES(NULL)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task ZstdDecompressToBytes_EmptyBytes_RoundTrips()
	{
		var result = await Eval("ZSTD_DECOMPRESS_TO_BYTES(ZSTD_COMPRESS(b''))");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().BeEmpty();
	}
}
