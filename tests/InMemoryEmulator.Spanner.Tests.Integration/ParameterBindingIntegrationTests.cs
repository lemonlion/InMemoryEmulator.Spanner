using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Dense parameterized query tests — verifying that parameter binding works for all types.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParameterBindingIntegrationTests : IntegrationTestBase
{
	public ParameterBindingIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// INT64 parameters
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#integer_literals
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(0L)]
	[InlineData(1L)]
	[InlineData(-1L)]
	[InlineData(42L)]
	[InlineData(long.MaxValue)]
	[InlineData(long.MinValue)]
	[InlineData(100L)]
	[InlineData(-100L)]
	[InlineData(999999999L)]
	public async Task Param_Int64_RoundTrips(long value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Int64, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(value);
	}

	// ═══════════════════════════════════════════════════════════════
	// FLOAT64 parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(0.0)]
	[InlineData(1.0)]
	[InlineData(-1.0)]
	[InlineData(3.14)]
	[InlineData(-3.14)]
	[InlineData(1e10)]
	[InlineData(1e-10)]
	[InlineData(double.MaxValue)]
	[InlineData(double.MinValue)]
	[InlineData(double.Epsilon)]
	public async Task Param_Float64_RoundTrips(double value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Float64, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<double>(0).Should().Be(value);
	}

	[Fact]
	public async Task Param_Float64_NaN()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Float64, double.NaN);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		double.IsNaN(reader.GetFieldValue<double>(0)).Should().BeTrue();
	}

	[Theory]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public async Task Param_Float64_Infinity(double value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Float64, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<double>(0).Should().Be(value);
	}

	// ═══════════════════════════════════════════════════════════════
	// BOOL parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Param_Bool_RoundTrips(bool value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Bool, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().Be(value);
	}

	// ═══════════════════════════════════════════════════════════════
	// STRING parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("")]
	[InlineData("hello")]
	[InlineData("hello world")]
	[InlineData("abc123")]
	[InlineData("Special chars: !@#$%^&*()")]
	[InlineData("Unicode: αβγδ")]
	[InlineData("Newline\nTab\tCR\r")]
	[InlineData("'single quotes'")]
	[InlineData("\"double quotes\"")]
	[InlineData("   spaces   ")]
	[InlineData("a")]
	public async Task Param_String_RoundTrips(string value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.String, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be(value);
	}

	// ═══════════════════════════════════════════════════════════════
	// DATE parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("2024-01-01")]
	[InlineData("2024-06-15")]
	[InlineData("2024-12-31")]
	[InlineData("2024-02-29")]
	[InlineData("2000-01-01")]
	[InlineData("1970-01-01")]
	public async Task Param_Date_RoundTrips(string dateStr)
	{
		var date = DateTime.Parse(dateStr);
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Date, date);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<DateTime>(0);
		result.Date.Should().Be(date.Date);
	}

	// ═══════════════════════════════════════════════════════════════
	// TIMESTAMP parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("2024-01-01T00:00:00Z")]
	[InlineData("2024-06-15T12:30:45Z")]
	[InlineData("2024-12-31T23:59:59Z")]
	[InlineData("2000-01-01T00:00:00Z")]
	[InlineData("1970-01-01T00:00:00Z")]
	public async Task Param_Timestamp_RoundTrips(string tsStr)
	{
		var ts = DateTime.Parse(tsStr).ToUniversalTime();
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Timestamp, ts);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<DateTime>(0).ToUniversalTime();
		result.Should().BeCloseTo(ts, TimeSpan.FromSeconds(1));
	}

	// ═══════════════════════════════════════════════════════════════
	// NULL parameters
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_NullInt64()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Int64, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NullFloat64()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Float64, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NullString()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.String, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NullBool()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Bool, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NullDate()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Date, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NullTimestamp()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Timestamp, null);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.IsDBNull(0).Should().BeTrue();
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(10L, 3L, 13L)]
	[InlineData(0L, 0L, 0L)]
	[InlineData(-5L, 5L, 0L)]
	[InlineData(100L, -50L, 50L)]
	public async Task Param_Int64_Addition(long a, long b, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @a + @b AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData(10L, 3L, 7L)]
	[InlineData(0L, 0L, 0L)]
	[InlineData(5L, 10L, -5L)]
	public async Task Param_Int64_Subtraction(long a, long b, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @a - @b AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData(3L, 4L, 12L)]
	[InlineData(0L, 100L, 0L)]
	[InlineData(-3L, 4L, -12L)]
	[InlineData(-3L, -4L, 12L)]
	public async Task Param_Int64_Multiplication(long a, long b, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @a * @b AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in string functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("hello", "HELLO")]
	[InlineData("WORLD", "WORLD")]
	[InlineData("", "")]
	[InlineData("abc", "ABC")]
	public async Task Param_StringUpper(string input, string expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT UPPER(@p) AS R");
		cmd.Parameters.Add("p", SpannerDbType.String, input);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData("hello", 5L)]
	[InlineData("", 0L)]
	[InlineData("abc", 3L)]
	public async Task Param_StringLength(string input, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT LENGTH(@p) AS R");
		cmd.Parameters.Add("p", SpannerDbType.String, input);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData("hello", "world", "helloworld")]
	[InlineData("", "", "")]
	[InlineData("a", "b", "ab")]
	public async Task Param_StringConcat(string a, string b, string expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT CONCAT(@a, @b) AS R");
		cmd.Parameters.Add("a", SpannerDbType.String, a);
		cmd.Parameters.Add("b", SpannerDbType.String, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in comparisons
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(1L, 1L, true)]
	[InlineData(1L, 2L, false)]
	[InlineData(0L, 0L, true)]
	public async Task Param_Int64_Equality(long a, long b, bool expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @a = @b AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData(1L, 2L, true)]
	[InlineData(2L, 1L, false)]
	[InlineData(1L, 1L, false)]
	public async Task Param_Int64_LessThan(long a, long b, bool expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @a < @b AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in BETWEEN
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(5L, 1L, 10L, true)]
	[InlineData(1L, 1L, 10L, true)]
	[InlineData(10L, 1L, 10L, true)]
	[InlineData(0L, 1L, 10L, false)]
	[InlineData(11L, 1L, 10L, false)]
	public async Task Param_Int64_Between(long val, long lo, long hi, bool expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @v BETWEEN @lo AND @hi AS R");
		cmd.Parameters.Add("v", SpannerDbType.Int64, val);
		cmd.Parameters.Add("lo", SpannerDbType.Int64, lo);
		cmd.Parameters.Add("hi", SpannerDbType.Int64, hi);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in IN
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_InArray_Int64()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @v IN UNNEST(@arr) AS R");
		cmd.Parameters.Add("v", SpannerDbType.Int64, 2L);
		cmd.Parameters.Add("arr", SpannerDbType.ArrayOf(SpannerDbType.Int64), new long[] { 1L, 2L, 3L });
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().BeTrue();
	}

	[Fact]
	public async Task Param_NotInArray_Int64()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @v IN UNNEST(@arr) AS R");
		cmd.Parameters.Add("v", SpannerDbType.Int64, 4L);
		cmd.Parameters.Add("arr", SpannerDbType.ArrayOf(SpannerDbType.Int64), new long[] { 1L, 2L, 3L });
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<bool>(0).Should().BeFalse();
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in conditional expressions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(true, "yes")]
	[InlineData(false, "no")]
	public async Task Param_Bool_If(bool cond, string expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT IF(@c, 'yes', 'no') AS R");
		cmd.Parameters.Add("c", SpannerDbType.Bool, cond);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in math functions
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(5L, 5L)]
	[InlineData(-5L, 5L)]
	[InlineData(0L, 0L)]
	public async Task Param_Int64_Abs(long input, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT ABS(@p) AS R");
		cmd.Parameters.Add("p", SpannerDbType.Int64, input);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	[Theory]
	[InlineData(10L, 3L, 1L)]
	[InlineData(10L, 5L, 0L)]
	[InlineData(7L, 2L, 1L)]
	public async Task Param_Int64_Mod(long a, long b, long expected)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT MOD(@a, @b) AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, a);
		cmd.Parameters.Add("b", SpannerDbType.Int64, b);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parameters in DML (INSERT + SELECT)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_InsertAndQuery()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE ParamTestTbl (Id INT64 NOT NULL, Name STRING(100), Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		// Insert using parameters
		using var conn = Fixture.CreateConnection();
		using (var tx = await conn.BeginTransactionAsync())
		{
			var dml = conn.CreateDmlCommand("INSERT INTO ParamTestTbl (Id, Name, Val) VALUES (@id, @name, @val)");
			dml.Transaction = tx;
			dml.Parameters.Add("id", SpannerDbType.Int64, 1L);
			dml.Parameters.Add("name", SpannerDbType.String, "test");
			dml.Parameters.Add("val", SpannerDbType.Int64, 42L);
			await dml.ExecuteNonQueryAsync();
			await tx.CommitAsync();
		}

		// Query using parameters
		using var cmd = conn.CreateSelectCommand(
			"SELECT Name, Val FROM ParamTestTbl WHERE Id = @id");
		cmd.Parameters.Add("id", SpannerDbType.Int64, 1L);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be("test");
		reader.GetFieldValue<long>(1).Should().Be(42L);
	}

	[Fact]
	public async Task Param_QueryWithWhereClause()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE ParamWhere (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("ParamWhere",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT Id FROM ParamWhere WHERE Val > @minVal ORDER BY Id");
		cmd.Parameters.Add("minVal", SpannerDbType.Int64, 15L);
		using var reader = await cmd.ExecuteReaderAsync();
		var ids = new List<long>();
		while (await reader.ReadAsync())
			ids.Add(reader.GetFieldValue<long>(0));
		ids.Should().Equal(2L, 3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple parameters in complex expressions
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_ComplexExpression()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT IF(@a > @b, CONCAT(@prefix, CAST(@a AS STRING)), CONCAT(@prefix, CAST(@b AS STRING))) AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, 10L);
		cmd.Parameters.Add("b", SpannerDbType.Int64, 5L);
		cmd.Parameters.Add("prefix", SpannerDbType.String, "max=");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<string>(0).Should().Be("max=10");
	}

	[Fact]
	public async Task Param_CoalesceWithNull()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(
			"SELECT COALESCE(@a, @b, @c) AS R");
		cmd.Parameters.Add("a", SpannerDbType.Int64, null);
		cmd.Parameters.Add("b", SpannerDbType.Int64, null);
		cmd.Parameters.Add("c", SpannerDbType.Int64, 42L);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		reader.GetFieldValue<long>(0).Should().Be(42L);
	}

	// ═══════════════════════════════════════════════════════════════
	// BYTES parameters
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData(new byte[] { })]
	[InlineData(new byte[] { 0 })]
	[InlineData(new byte[] { 1, 2, 3 })]
	[InlineData(new byte[] { 0xFF, 0x00, 0xAB })]
	public async Task Param_Bytes_RoundTrips(byte[] value)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.Bytes, value);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<byte[]>(0);
		result.Should().Equal(value);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY parameters
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_ArrayInt64_RoundTrips()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.ArrayOf(SpannerDbType.Int64), new long[] { 1, 2, 3 });
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<long[]>(0);
		result.Should().Equal(1L, 2L, 3L);
	}

	[Fact]
	public async Task Param_ArrayString_RoundTrips()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.ArrayOf(SpannerDbType.String), new[] { "a", "b", "c" });
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<string[]>(0);
		result.Should().Equal("a", "b", "c");
	}

	[Fact]
	public async Task Param_ArrayFloat64_RoundTrips()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.ArrayOf(SpannerDbType.Float64), new[] { 1.1, 2.2, 3.3 });
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<double[]>(0);
		result.Should().Equal(1.1, 2.2, 3.3);
	}

	[Fact]
	public async Task Param_EmptyArrayInt64()
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT @p AS R");
		cmd.Parameters.Add("p", SpannerDbType.ArrayOf(SpannerDbType.Int64), Array.Empty<long>());
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		var result = reader.GetFieldValue<long[]>(0);
		result.Should().BeEmpty();
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY<INT64> element type deserialization
	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
	//   "INT64 is encoded as string."
	//   Array element types must be preserved during deserialization.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Param_ArrayInt64_OrderByElement()
	{
		// If array elements are deserialized as strings, ordering will be lexicographic:
		// "1", "10", "2", "3" instead of numeric: 1, 2, 3, 10.
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT elem FROM UNNEST(@arr) AS elem ORDER BY elem");
		cmd.Parameters.Add("arr", SpannerDbType.ArrayOf(SpannerDbType.Int64), new long[] { 10L, 3L, 1L, 2L });
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<long>();
		while (await reader.ReadAsync())
			results.Add(reader.GetInt64(0));
		results.Should().Equal(1L, 2L, 3L, 10L);
	}

	[Fact]
	public async Task Param_ArrayInt64_WhereGreaterThan()
	{
		// If array elements are strings, "3" > "10" lexicographically, which is wrong numerically.
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand("SELECT elem FROM UNNEST(@arr) AS elem WHERE elem > 3 ORDER BY elem");
		cmd.Parameters.Add("arr", SpannerDbType.ArrayOf(SpannerDbType.Int64), new long[] { 1L, 2L, 3L, 10L, 20L });
		using var reader = await cmd.ExecuteReaderAsync();
		var results = new List<long>();
		while (await reader.ReadAsync())
			results.Add(reader.GetInt64(0));
		results.Should().Equal(10L, 20L);
	}
}
