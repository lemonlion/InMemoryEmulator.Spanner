using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for newly implemented GCP Spanner functions that close
/// parity gaps between the emulator and the real GCP Spanner service.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NewFunctionParityIntegrationTests : IntegrationTestBase
{
	public NewFunctionParityIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ADDDATE / SUBDATE — aliases for DATE_ADD / DATE_SUB
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#adddate
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#subdate
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	[InlineData("ADDDATE(DATE '2023-01-15', INTERVAL 10 DAY)", "2023-01-25")]
	[InlineData("ADDDATE(DATE '2023-12-28', INTERVAL 5 DAY)", "2024-01-02")]
	[InlineData("SUBDATE(DATE '2023-01-15', INTERVAL 10 DAY)", "2023-01-05")]
	[InlineData("SUBDATE(DATE '2023-01-01', INTERVAL 1 DAY)", "2022-12-31")]
	public async Task DateAlias_ReturnsExpected(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().NotBeNull();
		var date = (DateTime)result!;
		date.ToString("yyyy-MM-dd").Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// SPLIT_SUBSTR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split_substr
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 1, 2)", "www.abc")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 1, 1)", "www")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', -1, 1)", "com")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 3)", "xyz.com")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 1, 0)", "")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 5, 3)", "")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 0, 3)", "www.abc.xyz")]
	[InlineData("SPLIT_SUBSTR('www.abc.xyz.com', '.', 1, 3)", "www.abc.xyz")]
	public async Task SplitSubstr_ReturnsExpected(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SplitSubstr_Null_ReturnsNull()
		=> (await Eval("SPLIT_SUBSTR(NULL, '.', 1)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// BIT_REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_reverse
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("BIT_REVERSE(0, true)", 0L)]
	public async Task BitReverse_ReturnsExpected(string expr, long expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task BitReverse_Null_ReturnsNull()
		=> (await Eval("BIT_REVERSE(NULL, true)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// IS_FIRST window function
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#is_first
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task IsFirst_WithOrderBy_ReturnsCorrectBooleans()
	{
		var table = $"IsFirst_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "V", 30L } });
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 2L }, { "V", 10L } });
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 3L }, { "V", 20L } });

		var rows = await QueryAsync($"SELECT K, IS_FIRST(2) OVER (ORDER BY V ASC) AS IsTop FROM {table} ORDER BY K");
		rows.Should().HaveCount(3);
		// V=10 (K=2) is 1st, V=20 (K=3) is 2nd, V=30 (K=1) is 3rd
		rows[0]["IsTop"].Should().Be(false); // K=1, V=30, rank 3
		rows[1]["IsTop"].Should().Be(true);  // K=2, V=10, rank 1
		rows[2]["IsTop"].Should().Be(true);  // K=3, V=20, rank 2
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
	public async Task IsFirst_WithPartitionBy_ReturnsPerPartition()
	{
		var table = $"IsFirst2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync($"CREATE TABLE {table} (K INT64 NOT NULL, Cat STRING(10), V INT64) PRIMARY KEY (K)");
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 1L }, { "Cat", "A" }, { "V", 30L } });
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 2L }, { "Cat", "A" }, { "V", 10L } });
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 3L }, { "Cat", "B" }, { "V", 20L } });
		await InsertAsync(table, new Dictionary<string, object?> { { "K", 4L }, { "Cat", "B" }, { "V", 5L } });

		var rows = await QueryAsync(
			$"SELECT K, IS_FIRST(1) OVER (PARTITION BY Cat ORDER BY V ASC) AS IsTop FROM {table} ORDER BY K");
		rows.Should().HaveCount(4);
		rows[0]["IsTop"].Should().Be(false); // K=1, Cat=A, V=30, not first
		rows[1]["IsTop"].Should().Be(true);  // K=2, Cat=A, V=10, first in A
		rows[2]["IsTop"].Should().Be(false); // K=3, Cat=B, V=20, not first
		rows[3]["IsTop"].Should().Be(true);  // K=4, Cat=B, V=5, first in B
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE_ARRAY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value_array
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonValueArray_ReturnsStringArray()
	{
		var result = await Eval("JSON_VALUE_ARRAY('[\"a\", \"b\", \"c\"]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(3);
		arr[0].Should().Be("a");
		arr[1].Should().Be("b");
		arr[2].Should().Be("c");
	}

	[Fact]
	public async Task JsonValueArray_MixedTypes_ReturnsStrings()
	{
		var result = await Eval("JSON_VALUE_ARRAY('[1, \"two\", true, null]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(4);
		arr[0].Should().Be("1");
		arr[1].Should().Be("two");
		arr[2].Should().Be("true");
		arr[3].Should().BeNull();
	}

	[Fact]
	public async Task JsonValueArray_Null_ReturnsNull()
		=> (await Eval("JSON_VALUE_ARRAY(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// JSON_ARRAY_APPEND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonArrayAppend_AppendsToArray()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append
		//   Signature: JSON_ARRAY_APPEND(json_expr, json_path, value, ...)
		var result = await Eval("JSON_ARRAY_APPEND(JSON '[1, 2]', '$', 3)");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("[1,2,3]");
	}

	[Fact]
	public async Task JsonArrayAppend_MultiplePathValuePairs()
	{
		var result = await Eval("JSON_ARRAY_APPEND(JSON '[\"a\"]', '$', 'b', '$', 'c')");
		result.Should().NotBeNull();
		result!.ToString().Should().Contain("a").And.Contain("b").And.Contain("c");
	}

	[Fact]
	public async Task JsonArrayAppend_Null_ReturnsNull()
		=> (await Eval("JSON_ARRAY_APPEND(NULL, '$', 1)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// JSON_ARRAY_INSERT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_insert
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonArrayInsert_InsertsAtIndex()
	{
		var result = await Eval("JSON_ARRAY_INSERT(JSON '[1, 3]', '$[1]', 2)");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("[1,2,3]");
	}

	[Fact]
	public async Task JsonArrayInsert_AtEnd()
	{
		var result = await Eval("JSON_ARRAY_INSERT(JSON '[1, 2]', '$[2]', 3)");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("[1,2,3]");
	}

	[Fact]
	public async Task JsonArrayInsert_Null_ReturnsNull()
		=> (await Eval("JSON_ARRAY_INSERT(NULL, '$[0]', 1)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// JSON_CONTAINS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_contains
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_CONTAINS(JSON '{\"a\": 1, \"b\": 2}', JSON '{\"a\": 1}')", true)]
	[InlineData("JSON_CONTAINS(JSON '{\"a\": 1}', JSON '{\"a\": 1, \"b\": 2}')", false)]
	[InlineData("JSON_CONTAINS(JSON '[1, 2, 3]', JSON '[1, 2]')", true)]
	[InlineData("JSON_CONTAINS(JSON '[1, 2]', JSON '[1, 3]')", false)]
	public async Task JsonContains_ReturnsExpected(string expr, bool expected)
		=> (await Eval(expr)).Should().Be(expected);

	[Fact]
	public async Task JsonContains_Null_ReturnsNull()
		=> (await Eval("JSON_CONTAINS(CAST(NULL AS JSON), JSON '{\"a\": 1}')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// JSON_REMOVE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_remove
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task JsonRemove_RemovesKey()
	{
		var result = await Eval("JSON_REMOVE(JSON '{\"a\": 1, \"b\": 2}', '$.a')");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("{\"b\":2}");
	}

	[Fact]
	public async Task JsonRemove_RemovesArrayElement()
	{
		var result = await Eval("JSON_REMOVE(JSON '[1, 2, 3]', '$[1]')");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("[1,3]");
	}

	[Fact]
	public async Task JsonRemove_Null_ReturnsNull()
		=> (await Eval("JSON_REMOVE(NULL, '$.a')")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// SAFE_TO_JSON
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#safe_to_json
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SafeToJson_ValidInput_ReturnsJson()
	{
		var result = await Eval("SAFE_TO_JSON(JSON '{\"a\": 1}')");
		result.Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeToJson_Null_ReturnsNull()
		=> (await Eval("SAFE_TO_JSON(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// JSON Array Conversion Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Float64Array_ConvertsJsonArray()
	{
		var result = await Eval("FLOAT64_ARRAY(JSON '[1.1, 2.2, 3.3]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(3);
		Convert.ToDouble(arr[0]).Should().BeApproximately(1.1, 0.001);
		Convert.ToDouble(arr[1]).Should().BeApproximately(2.2, 0.001);
		Convert.ToDouble(arr[2]).Should().BeApproximately(3.3, 0.001);
	}

	[Fact]
	public async Task Int64Array_ConvertsJsonArray()
	{
		var result = await Eval("INT64_ARRAY(JSON '[10, 20, 30]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(3);
		arr[0].Should().Be(10L);
		arr[1].Should().Be(20L);
		arr[2].Should().Be(30L);
	}

	[Fact]
	public async Task BoolArray_ConvertsJsonArray()
	{
		var result = await Eval("BOOL_ARRAY(JSON '[true, false, true]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(3);
		arr[0].Should().Be(true);
		arr[1].Should().Be(false);
		arr[2].Should().Be(true);
	}

	[Fact]
	public async Task StringArray_ConvertsJsonArray()
	{
		var result = await Eval("STRING_ARRAY(JSON '[\"a\", \"b\", \"c\"]')");
		result.Should().NotBeNull();
		var arr = (System.Collections.IList)result!;
		arr.Count.Should().Be(3);
		arr[0].Should().Be("a");
		arr[1].Should().Be("b");
		arr[2].Should().Be("c");
	}

	[Fact]
	public async Task JsonArrayConversion_WithNull_ReturnsNull()
		=> (await Eval("INT64_ARRAY(NULL)")).Should().BeNull();

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JsonArrayConversion_WithNullElements_ReturnsMixedArray()
	{
		// Null elements in typed arrays are valid in GCP Spanner.
		// Use STRING_ARRAY to avoid INT64 null conversion issues in the SDK reader.
		var result = await Eval("STRING_ARRAY(JSON '[\"a\", null, \"c\"]')");
		result.Should().NotBeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IP_NET_MASK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_net_mask
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NetIpNetMask_IPv4_24Bits()
	{
		var result = await Eval("NET.IP_NET_MASK(4, 24)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 255, 255, 255, 0 });
	}

	[Fact]
	public async Task NetIpNetMask_IPv4_16Bits()
	{
		var result = await Eval("NET.IP_NET_MASK(4, 16)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 255, 255, 0, 0 });
	}

	[Fact]
	public async Task NetIpNetMask_IPv4_0Bits()
	{
		var result = await Eval("NET.IP_NET_MASK(4, 0)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 0, 0, 0, 0 });
	}

	[Fact]
	public async Task NetIpNetMask_IPv4_32Bits()
	{
		var result = await Eval("NET.IP_NET_MASK(4, 32)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 255, 255, 255, 255 });
	}

	[Fact]
	public async Task NetIpNetMask_IPv6_64Bits()
	{
		var result = await Eval("NET.IP_NET_MASK(16, 64)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Length.Should().Be(16);
		bytes.Take(8).Should().AllBeEquivalentTo((byte)255);
		bytes.Skip(8).Should().AllBeEquivalentTo((byte)0);
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_trunc
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NetIpTrunc_TruncatesIPv4()
	{
		var result = await Eval("NET.IP_TRUNC(NET.IP_FROM_STRING('192.168.1.100'), 24)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 192, 168, 1, 0 });
	}

	[Fact]
	public async Task NetIpTrunc_TruncatesTo16()
	{
		var result = await Eval("NET.IP_TRUNC(NET.IP_FROM_STRING('192.168.1.100'), 16)");
		result.Should().NotBeNull();
		var bytes = (byte[])result!;
		bytes.Should().Equal(new byte[] { 192, 168, 0, 0 });
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IPV4_FROM_INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_from_int64
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(0L, new byte[] { 0, 0, 0, 0 })]
	[InlineData(1L, new byte[] { 0, 0, 0, 1 })]
	[InlineData(256L, new byte[] { 0, 0, 1, 0 })]
	public async Task NetIpv4FromInt64_ReturnsExpected(long intVal, byte[] expected)
	{
		var result = await Eval($"NET.IPV4_FROM_INT64({intVal})");
		result.Should().NotBeNull();
		((byte[])result!).Should().Equal(expected);
	}

	[Fact]
	public async Task NetIpv4FromInt64_Null_ReturnsNull()
		=> (await Eval("NET.IPV4_FROM_INT64(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// NET.IPV4_TO_INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_to_int64
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NetIpv4ToInt64_ReturnsExpected()
	{
		var result = await Eval("NET.IPV4_TO_INT64(NET.IP_FROM_STRING('0.0.1.0'))");
		result.Should().Be(256L);
	}

	[Fact]
	public async Task NetIpv4ToInt64_Loopback()
	{
		var result = await Eval("NET.IPV4_TO_INT64(NET.IP_FROM_STRING('127.0.0.1'))");
		result.Should().Be(2130706433L);
	}

	[Fact]
	public async Task NetIpv4ToInt64_Null_ReturnsNull()
		=> (await Eval("NET.IPV4_TO_INT64(NULL)")).Should().BeNull();

	// ═══════════════════════════════════════════════════════════════
	// Combined NET function round-trips
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("NET.IP_TO_STRING(NET.IPV4_FROM_INT64(NET.IPV4_TO_INT64(NET.IP_FROM_STRING('192.168.1.1'))))", "192.168.1.1")]
	public async Task NetFunction_Roundtrip(string expr, string expected)
		=> (await Eval(expr)).Should().Be(expected);
}
