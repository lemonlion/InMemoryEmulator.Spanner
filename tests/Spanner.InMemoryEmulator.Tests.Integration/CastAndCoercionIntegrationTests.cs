using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for CAST, SAFE_CAST, and type coercion between all Spanner types.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CastAndCoercionIntegrationTests : IntegrationTestBase
{
	public CastAndCoercionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── CAST: INT64 ↔ STRING ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast

	[Theory]
	[InlineData("CAST(42 AS STRING)", "42")]
	[InlineData("CAST(-42 AS STRING)", "-42")]
	[InlineData("CAST(0 AS STRING)", "0")]
	[InlineData("CAST(9223372036854775807 AS STRING)", "9223372036854775807")]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_IntToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('42' AS INT64)", 42L)]
	[InlineData("CAST('-42' AS INT64)", -42L)]
	[InlineData("CAST('0' AS INT64)", 0L)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToInt(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CAST: FLOAT64 ↔ STRING ───

	[Theory]
	[InlineData("CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("CAST('0.0' AS FLOAT64)", 0.0)]
	[InlineData("CAST('-1.5' AS FLOAT64)", -1.5)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToFloat(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_FloatToString()
	{
		var result = await Eval("CAST(3.14 AS STRING)");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("3.14");
	}

	// ─── CAST: INT64 ↔ FLOAT64 ───

	[Theory]
	[InlineData("CAST(42 AS FLOAT64)", 42.0)]
	[InlineData("CAST(0 AS FLOAT64)", 0.0)]
	[InlineData("CAST(-1 AS FLOAT64)", -1.0)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_IntToFloat(string expr, double expected)
	{
		var result = await Eval(expr);
		((double)result!).Should().BeApproximately(expected, 1e-10);
	}

	[Theory]
	[InlineData("CAST(3.0 AS INT64)", 3L)]
	[InlineData("CAST(3.9 AS INT64)", 4L)]
	[InlineData("CAST(-3.9 AS INT64)", -4L)]
	[InlineData("CAST(0.0 AS INT64)", 0L)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_FloatToInt(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CAST: BOOL ↔ STRING ───

	[Theory]
	[InlineData("CAST(true AS STRING)", "true")]
	[InlineData("CAST(false AS STRING)", "false")]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_BoolToString(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST('true' AS BOOL)", true)]
	[InlineData("CAST('false' AS BOOL)", false)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToBool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CAST: BOOL ↔ INT64 ───

	[Theory]
	[InlineData("CAST(true AS INT64)", 1L)]
	[InlineData("CAST(false AS INT64)", 0L)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_BoolToInt(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Theory]
	[InlineData("CAST(1 AS BOOL)", true)]
	[InlineData("CAST(0 AS BOOL)", false)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_IntToBool(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── CAST: DATE ↔ STRING ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_DateToString()
	{
		var result = await Eval("CAST(DATE('2024-01-15') AS STRING)");
		result.Should().Be("2024-01-15");
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToDate()
	{
		var result = await Eval("CAST('2024-01-15' AS DATE)");
		result.Should().BeOfType<DateTime>();
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 15));
	}

	// ─── CAST: TIMESTAMP ↔ STRING ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_TimestampToString()
	{
		var result = await Eval("CAST(TIMESTAMP('2024-01-15T10:30:00Z') AS STRING)");
		result.Should().NotBeNull();
		((string)result!).Should().Contain("2024");
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToTimestamp()
	{
		var result = await Eval("CAST('2024-01-15T10:30:00Z' AS TIMESTAMP)");
		result.Should().BeOfType<DateTime>();
	}

	// ─── CAST: DATE ↔ TIMESTAMP ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_DateToTimestamp()
	{
		var result = await Eval("CAST(DATE('2024-01-15') AS TIMESTAMP)");
		result.Should().BeOfType<DateTime>();
		var dt = (DateTime)result!;
		dt.Year.Should().Be(2024);
		dt.Month.Should().Be(1);
		dt.Day.Should().Be(15);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_TimestampToDate()
	{
		var result = await Eval("CAST(TIMESTAMP('2024-01-15T10:30:00Z') AS DATE)");
		result.Should().BeOfType<DateTime>();
		((DateTime)result!).Should().Be(new DateTime(2024, 1, 15));
	}

	// ─── CAST with NULL ───

	[Theory]
	[InlineData("CAST(NULL AS INT64)")]
	[InlineData("CAST(NULL AS STRING)")]
	[InlineData("CAST(NULL AS FLOAT64)")]
	[InlineData("CAST(NULL AS BOOL)")]
	[InlineData("CAST(NULL AS DATE)")]
	[InlineData("CAST(NULL AS TIMESTAMP)")]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_Null_AlwaysNull(string expr)
	{
		var result = await Eval(expr);
		result.Should().BeNull();
	}

	// ─── SAFE_CAST ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#safe_cast

	[Theory]
	[InlineData("SAFE_CAST('42' AS INT64)", 42L)]
	[InlineData("SAFE_CAST('hello' AS INT64)", null)]
	[InlineData("SAFE_CAST('3.14' AS FLOAT64)", 3.14)]
	[InlineData("SAFE_CAST('true' AS BOOL)", true)]
	[Trait(TestTraits.Category, "Cast")]
	public async Task SafeCast_ValidAndInvalid(string expr, object? expected)
	{
		var result = await Eval(expr);
		if (expected is double d)
			((double)result!).Should().BeApproximately(d, 1e-10);
		else
			result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task SafeCast_InvalidDateString_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('not-a-date' AS DATE)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task SafeCast_InvalidTimestamp_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('not-a-timestamp' AS TIMESTAMP)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task SafeCast_InvalidBool_ReturnsNull()
	{
		var result = await Eval("SAFE_CAST('maybe' AS BOOL)");
		result.Should().BeNull();
	}

	// ─── Implicit coercion INT64 → FLOAT64 in arithmetic ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task ImplicitCoercion_IntToFloat_InArithmetic()
	{
		var result = await Eval("1 + 2.5");
		result.Should().BeOfType<double>();
		((double)result!).Should().BeApproximately(3.5, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task ImplicitCoercion_IntToFloat_InComparison()
	{
		var result = await Eval("1 < 1.5");
		result.Should().Be(true);
	}

	// ─── CAST in table queries ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_InSelectWithTable()
	{
		var table = "CastTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, StrVal STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["StrVal"] = "42" });

		var rows = await QueryAsync($"SELECT CAST(StrVal AS INT64) AS IntVal FROM {table}");
		rows[0]["IntVal"].Should().Be(42L);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_InWhereClause()
	{
		var table = "CastTbl2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, StrVal STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["StrVal"] = "10" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["StrVal"] = "30" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["StrVal"] = "20" });

		var rows = await QueryAsync($"SELECT Id FROM {table} WHERE CAST(StrVal AS INT64) > 15 ORDER BY Id");
		rows.Select(r => (long)r["Id"]!).Should().Equal(2L, 3L);
	}

	// ─── Double CAST ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_IntToStringToFloat()
	{
		var result = await Eval("CAST(CAST(42 AS STRING) AS FLOAT64)");
		((double)result!).Should().BeApproximately(42.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_FloatToStringToInt()
	{
		var result = await Eval("CAST(CAST(3.0 AS STRING) AS INT64)");
		result.Should().Be(3L);
	}

	// ─── CAST: STRING → BYTES ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_StringToBytes()
	{
		var result = await Eval("CAST('hello' AS BYTES)");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().Equal(104, 101, 108, 108, 111); // ASCII for 'hello'
	}

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_BytesToString()
	{
		var result = await Eval("CAST(b'hello' AS STRING)");
		result.Should().Be("hello");
	}

	// ─── CAST: FLOAT64 → BOOL (non-standard but might be supported) ───

	[Fact]
	[Trait(TestTraits.Category, "Cast")]
	public async Task Cast_IntToString_InConcatenation()
	{
		var result = await Eval("CONCAT('Value: ', CAST(42 AS STRING))");
		result.Should().Be("Value: 42");
	}
}
