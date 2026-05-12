using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Exhaustive HASH and encoding function tests: SHA1, SHA256, SHA512, MD5,
/// TO_HEX, FROM_HEX, TO_BASE64, FROM_BASE64, TO_BASE32, FROM_BASE32.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HashEncodingExhaustiveIntegrationTests : IntegrationTestBase
{
	public HashEncodingExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── TO_HEX / FROM_HEX ───
	[Theory]
	[InlineData("TO_HEX(b'\\x00')", "00")]
	[InlineData("TO_HEX(b'\\xff')", "ff")]
	[InlineData("TO_HEX(b'\\x48\\x65\\x6c\\x6c\\x6f')", "48656c6c6f")]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task ToHex(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task FromHex()
	{
		var result = await Eval("TO_HEX(FROM_HEX('48656c6c6f'))");
		result.Should().Be("48656c6c6f");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task FromHex_Empty()
	{
		var result = await Eval("TO_HEX(FROM_HEX(''))");
		result.Should().Be("");
	}

	// ─── TO_BASE64 / FROM_BASE64 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task ToBase64_Roundtrip()
	{
		var result = await Eval("TO_BASE64(FROM_BASE64('SGVsbG8='))");
		result.Should().Be("SGVsbG8=");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task ToBase64_FromBytes()
	{
		var result = await Eval("TO_BASE64(b'Hello')");
		result.Should().Be("SGVsbG8=");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task FromBase64()
	{
		// FROM_BASE64 returns BYTES; verify round-trip
		var result = await Eval("TO_BASE64(FROM_BASE64('AQID'))");
		result.Should().Be("AQID");
	}

	// ─── SHA1 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha1_EmptyBytes()
	{
		var result = await Eval("TO_HEX(SHA1(b''))");
		result.Should().Be("da39a3ee5e6b4b0d3255bfef95601890afd80709");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha1_Hello()
	{
		var result = await Eval("TO_HEX(SHA1(b'Hello'))");
		result.Should().Be("f7ff9e8b7bb2e09b70935a5d785e0cc5d9d0abf0");
	}

	// ─── SHA256 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha256_EmptyBytes()
	{
		var result = await Eval("TO_HEX(SHA256(b''))");
		result.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha256_Hello()
	{
		var result = await Eval("TO_HEX(SHA256(b'Hello'))");
		result.Should().Be("185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969");
	}

	// ─── SHA512 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha512_EmptyBytes()
	{
		var result = await Eval("TO_HEX(SHA512(b''))");
		result.Should().Be("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e");
	}

	// ─── MD5 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Md5_EmptyBytes()
	{
		var result = await Eval("TO_HEX(MD5(b''))");
		result.Should().Be("d41d8cd98f00b204e9800998ecf8427e");
	}

	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Md5_Hello()
	{
		var result = await Eval("TO_HEX(MD5(b'Hello'))");
		result.Should().Be("8b1a9953c4611296a827abf8c47804d7");
	}

	// ─── NULL inputs ───
	[Theory]
	[InlineData("SHA1(CAST(NULL AS BYTES))")]
	[InlineData("SHA256(CAST(NULL AS BYTES))")]
	[InlineData("SHA512(CAST(NULL AS BYTES))")]
	[InlineData("MD5(CAST(NULL AS BYTES))")]
	[InlineData("TO_HEX(CAST(NULL AS BYTES))")]
	[InlineData("TO_BASE64(CAST(NULL AS BYTES))")]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task HashFunction_NullInput(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── Chained: SHA256 then TO_BASE64 ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Sha256_ThenBase64()
	{
		var result = await Eval("TO_BASE64(SHA256(b'test'))");
		result.Should().NotBeNull();
		((string)result!).Should().NotBeEmpty();
	}

	// ─── Hash in WHERE clause ───
	[Fact]
	[Trait(TestTraits.Category, "HashEncodingExhaustive")]
	public async Task Hash_InWhere()
	{
		var t = $"HashW_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Data BYTES(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Data) VALUES (1, b'Hello'), (2, b'World')");
		var rows = await QueryAsync(
			$"SELECT Id FROM {t} WHERE TO_HEX(SHA256(Data)) = '185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969'");
		rows.Should().HaveCount(1);
		rows[0]["Id"].Should().Be(1L);
	}
}
