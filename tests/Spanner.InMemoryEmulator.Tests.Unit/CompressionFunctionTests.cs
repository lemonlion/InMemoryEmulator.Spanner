using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Xunit;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for Zstandard compression functions.
/// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
/// </summary>
public class CompressionFunctionTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		return db;
	}

	// ─── ZSTD_COMPRESS ───

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_COMPRESS compresses STRING or BYTES input into BYTES output using Zstandard.
	[Fact]
	public void ZstdCompress_StringInput_ReturnsBytesValue()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_COMPRESS('hello') AS C");
		rows.Should().ContainSingle();
		var value = rows[0]["C"];
		value.Should().NotBeNull();
		// The result should be serialized as BYTES in the protobuf
	}

	[Fact]
	public void ZstdCompress_NullInput_ReturnsNull()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_COMPRESS(NULL) AS C");
		rows.Should().ContainSingle();
		rows[0]["C"].Should().BeNull();
	}

	// ─── ZSTD_DECOMPRESS_TO_STRING ───

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_DECOMPRESS_TO_STRING decompresses BYTES input into STRING output using Zstandard.
	[Fact]
	public void ZstdDecompressToString_RoundTrips()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_DECOMPRESS_TO_STRING(ZSTD_COMPRESS('hello world')) AS V");
		rows.Should().ContainSingle();
		rows[0]["V"].Should().Be("hello world");
	}

	[Fact]
	public void ZstdDecompressToString_NullInput_ReturnsNull()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_DECOMPRESS_TO_STRING(NULL) AS V");
		rows.Should().ContainSingle();
		rows[0]["V"].Should().BeNull();
	}

	// ─── ZSTD_DECOMPRESS_TO_BYTES ───

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_DECOMPRESS_TO_BYTES decompresses BYTES input into BYTES output using Zstandard.
	[Fact]
	public void ZstdDecompressToBytes_RoundTrips()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_DECOMPRESS_TO_BYTES(ZSTD_COMPRESS(b'test data')) AS V");
		rows.Should().ContainSingle();
		var value = rows[0]["V"];
		value.Should().NotBeNull();
	}

	[Fact]
	public void ZstdDecompressToBytes_NullInput_ReturnsNull()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT ZSTD_DECOMPRESS_TO_BYTES(NULL) AS V");
		rows.Should().ContainSingle();
		rows[0]["V"].Should().BeNull();
	}
}
