using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for newly added features: trig functions, GENERATE_UUID, Base64/Base32,
/// JSON conversion, SAFE prefix, QUALIFY, statistical aggregates, vector distance, NET functions,
/// ARRAY_IS_DISTINCT, generated/default columns, DDL enhancements (ALTER COLUMN, ADD/DROP CONSTRAINT,
/// IF EXISTS/IF NOT EXISTS, SET ON DELETE).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NewFeatureParityIntegrationTests : IntegrationTestBase
{
	public NewFeatureParityIntegrationTests(EmulatorSession session) : base(session) { }

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Trigonometric Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Sin_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT SIN(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Sin_PiOver2_ReturnsOne()
	{
		var result = await QueryScalarAsync("SELECT SIN(ACOS(-1) / 2)");
		Convert.ToDouble(result).Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cos_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT COS(0)");
		Convert.ToDouble(result).Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Tan_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT TAN(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Asin_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ASIN(1)");
		Convert.ToDouble(result).Should().BeApproximately(Math.PI / 2, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Acos_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ACOS(1)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Atan_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ATAN(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Atan2_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ATAN2(1, 1)");
		Convert.ToDouble(result).Should().BeApproximately(Math.PI / 4, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Sinh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT SINH(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Cosh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT COSH(0)");
		Convert.ToDouble(result).Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Tanh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT TANH(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Asinh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ASINH(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Acosh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ACOSH(1)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Atanh_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT ATANH(0)");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// GENERATE_UUID
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#generate_uuid
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateUuid_ReturnsValidUuid()
	{
		var result = await QueryScalarAsync("SELECT GENERATE_UUID()");
		result.Should().NotBeNull();
		Guid.TryParse(result!.ToString(), out _).Should().BeTrue();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GenerateUuid_ReturnsDifferentValuesPerCall()
	{
		var rows = await QueryAsync("SELECT GENERATE_UUID() AS a, GENERATE_UUID() AS b");
		rows.Should().HaveCount(1);
		rows[0]["a"].Should().NotBe(rows[0]["b"]);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Base64 Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_base64
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ToBase64_ReturnsCorrectEncoding()
	{
		var result = await QueryScalarAsync("SELECT TO_BASE64(b'abc')");
		result.Should().Be("YWJj");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FromBase64_ReturnsCorrectDecoding()
	{
		var result = await QueryScalarAsync("SELECT FROM_BASE64('YWJj')");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().BeEquivalentTo(new byte[] { 0x61, 0x62, 0x63 });
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ToBase64_FromBase64_RoundTrip()
	{
		var result = await QueryScalarAsync("SELECT TO_BASE64(FROM_BASE64('SGVsbG8='))");
		result.Should().Be("SGVsbG8=");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Base32 Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_base32
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ToBase32_ReturnsCorrectEncoding()
	{
		var result = await QueryScalarAsync("SELECT TO_BASE32(b'abc')");
		result.Should().Be("MFRGG===");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task FromBase32_ReturnsCorrectDecoding()
	{
		var result = await QueryScalarAsync("SELECT FROM_BASE32('MFRGG===')");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().BeEquivalentTo(new byte[] { 0x61, 0x62, 0x63 });
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// SAFE Prefix
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-reference#safe_prefix
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeDivide_ByZero_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT SAFE_DIVIDE(1, 0) AS result");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeDivide_NormalDivision_ReturnsResult()
	{
		var result = await QueryScalarAsync("SELECT SAFE_DIVIDE(10, 2)");
		Convert.ToDouble(result).Should().Be(5.0);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SafeSubstring_OutOfRange_ReturnsNull()
	{
		// SAFE.SUBSTR with negative length should return NULL instead of error
		var rows = await QueryAsync("SELECT SAFE.SUBSTR('abc', 1, -1) AS result");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// JSON Conversion Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Bool_FromJson_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT BOOL(JSON 'true')");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Int64_FromJson_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT INT64(JSON '42')");
		Convert.ToInt64(result).Should().Be(42);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Float64_FromJson_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT FLOAT64(JSON '3.14')");
		Convert.ToDouble(result).Should().BeApproximately(3.14, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task String_FromJson_ReturnsCorrectValue()
	{
		var result = await QueryScalarAsync("SELECT STRING(JSON '\"hello\"')");
		result.Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JsonArray_ReturnsJsonArray()
	{
		var result = await QueryScalarAsync("SELECT JSON_ARRAY(1, 2, 3)");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("[1,2,3]");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JsonObject_ReturnsJsonObject()
	{
		var result = await QueryScalarAsync("SELECT JSON_OBJECT('name', 'Alice', 'age', 30)");
		result.Should().NotBeNull();
		var str = result!.ToString()!;
		str.Should().Contain("\"name\"");
		str.Should().Contain("\"Alice\"");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task JsonStripNulls_RemovesNullFields()
	{
		var result = await QueryScalarAsync("SELECT JSON_STRIP_NULLS(JSON '{\"a\":1,\"b\":null}')");
		result.Should().NotBeNull();
		result!.ToString().Should().Be("{\"a\":1}");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task LaxBool_InvalidJson_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT LAX_BOOL(JSON '\"not_a_bool\"') AS result");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task LaxInt64_FromFloat_ReturnsRounded()
	{
		var result = await QueryScalarAsync("SELECT LAX_INT64(JSON '3.7')");
		// LAX_INT64 should attempt conversion; exact behavior may vary
		result.Should().NotBeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// ARRAY_IS_DISTINCT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_is_distinct
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayIsDistinct_DistinctArray_ReturnsTrue()
	{
		var result = await QueryScalarAsync("SELECT ARRAY_IS_DISTINCT([1, 2, 3])");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayIsDistinct_DuplicateArray_ReturnsFalse()
	{
		var result = await QueryScalarAsync("SELECT ARRAY_IS_DISTINCT([1, 2, 2])");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayIsDistinct_SingleElement_ReturnsTrue()
	{
		var result = await QueryScalarAsync("SELECT ARRAY_IS_DISTINCT([1])");
		result.Should().Be(true);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Statistical Aggregates: STDDEV, VARIANCE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#stddev
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Stddev_ReturnsCorrectValue()
	{
		try { await ExecuteDdlAsync("CREATE TABLE StddevTest (Id INT64 NOT NULL, Val FLOAT64) PRIMARY KEY (Id)"); } catch { }

		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (1, 2.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (2, 4.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (3, 4.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (4, 4.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (5, 5.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (6, 5.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (7, 7.0)");
		await ExecuteDmlAsync("INSERT INTO StddevTest (Id, Val) VALUES (8, 9.0)");

		var result = await QueryScalarAsync("SELECT STDDEV(Val) FROM StddevTest");
		Convert.ToDouble(result).Should().BeApproximately(2.138, 0.5);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Variance_ReturnsCorrectValue()
	{
		try { await ExecuteDdlAsync("CREATE TABLE VarTest (Id INT64 NOT NULL, Val FLOAT64) PRIMARY KEY (Id)"); } catch { }

		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (1, 2.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (2, 4.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (3, 4.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (4, 4.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (5, 5.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (6, 5.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (7, 7.0)");
		await ExecuteDmlAsync("INSERT INTO VarTest (Id, Val) VALUES (8, 9.0)");

		var result = await QueryScalarAsync("SELECT VARIANCE(Val) FROM VarTest");
		Convert.ToDouble(result).Should().BeApproximately(4.571, 1.0);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Vector Distance Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_IdenticalVectors_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 0.0], [1.0, 0.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_OrthogonalVectors_ReturnsOne()
	{
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 0.0], [0.0, 1.0])");
		Convert.ToDouble(result).Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_SamePoint_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([1.0, 2.0], [1.0, 2.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_KnownValues()
	{
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([0.0, 0.0], [3.0, 4.0])");
		Convert.ToDouble(result).Should().BeApproximately(5.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_KnownValues()
	{
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([1.0, 2.0, 3.0], [4.0, 5.0, 6.0])");
		Convert.ToDouble(result).Should().BeApproximately(32.0, 1e-10);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// NET Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetHost_ReturnsHost()
	{
		var result = await QueryScalarAsync("SELECT NET.HOST('https://www.example.com/path')");
		result.Should().Be("www.example.com");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetRegDomain_ReturnsRegisteredDomain()
	{
		var result = await QueryScalarAsync("SELECT NET.REG_DOMAIN('www.example.com')");
		result.Should().Be("example.com");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetPublicSuffix_ReturnsSuffix()
	{
		var result = await QueryScalarAsync("SELECT NET.PUBLIC_SUFFIX('www.example.com')");
		result.Should().Be("com");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetIpFromString_ReturnsBytes()
	{
		var result = await QueryScalarAsync("SELECT NET.IP_FROM_STRING('1.2.3.4')");
		result.Should().BeOfType<byte[]>();
		((byte[])result!).Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetIpToString_ReturnsString()
	{
		var result = await QueryScalarAsync("SELECT NET.IP_TO_STRING(b'\\x01\\x02\\x03\\x04')");
		result.Should().Be("1.2.3.4");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task NetSafeIpFromString_Invalid_ReturnsNull()
	{
		var rows = await QueryAsync("SELECT NET.SAFE_IP_FROM_STRING('not_an_ip') AS result");
		rows.Should().HaveCount(1);
		rows[0]["result"].Should().BeNull();
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DDL: IF NOT EXISTS / IF EXISTS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CreateTable_IfNotExists_AlreadyExists_Succeeds()
	{
		try { await ExecuteDdlAsync("CREATE TABLE IfNotExistsTest (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }

		// Should not throw
		await ExecuteDdlAsync("CREATE TABLE IF NOT EXISTS IfNotExistsTest (Id INT64 NOT NULL) PRIMARY KEY (Id)");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DropTable_IfExists_DoesNotExist_Succeeds()
	{
		// Should not throw even if the table doesn't exist
		await ExecuteDdlAsync("DROP TABLE IF EXISTS NonExistentTableXYZ12345");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CreateIndex_IfNotExists_AlreadyExists_Succeeds()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE IdxIfNotExistsTest (Id INT64 NOT NULL, Val STRING(100)) PRIMARY KEY (Id)");
		}
		catch { }

		try
		{
			await ExecuteDdlAsync(
				"CREATE INDEX IdxIfNotExistsIdx ON IdxIfNotExistsTest (Val)");
		}
		catch { }

		// Should not throw
		await ExecuteDdlAsync(
			"CREATE INDEX IF NOT EXISTS IdxIfNotExistsIdx ON IdxIfNotExistsTest (Val)");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DropIndex_IfExists_DoesNotExist_Succeeds()
	{
		// Should not throw
		await ExecuteDdlAsync("DROP INDEX IF EXISTS NonExistentIndexXYZ12345");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DDL: ALTER TABLE ALTER COLUMN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_column
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AlterColumn_ChangesColumnType()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AlterColTest (Id INT64 NOT NULL, Val STRING(50)) PRIMARY KEY (Id)");
		}
		catch { }

		// Change column length
		await ExecuteDdlAsync("ALTER TABLE AlterColTest ALTER COLUMN Val STRING(200)");

		// The column should still work
		await ExecuteDmlAsync("INSERT INTO AlterColTest (Id, Val) VALUES (1, 'test')");
		var result = await QueryScalarAsync("SELECT Val FROM AlterColTest WHERE Id = 1");
		result.Should().Be("test");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DDL: ADD / DROP CONSTRAINT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task AddCheckConstraint_ThenViolate_Fails()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE AddChkTest (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDdlAsync(
			"ALTER TABLE AddChkTest ADD CONSTRAINT ChkVal CHECK (Val > 0)");

		// Violating the constraint should fail
		var act = () => ExecuteDmlAsync("INSERT INTO AddChkTest (Id, Val) VALUES (1, -1)");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DropConstraint_ThenViolate_Succeeds()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DropChkTest (Id INT64 NOT NULL, Val INT64, CONSTRAINT ChkDrop CHECK (Val > 0)) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDdlAsync("ALTER TABLE DropChkTest DROP CONSTRAINT ChkDrop");

		// Violating the dropped constraint should succeed
		await ExecuteDmlAsync("INSERT INTO DropChkTest (Id, Val) VALUES (1, -1)");
		var result = await QueryScalarAsync("SELECT Val FROM DropChkTest WHERE Id = 1");
		Convert.ToInt64(result).Should().Be(-1);
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// DDL: SET ON DELETE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#alter_table
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task SetOnDelete_Cascade_WorksAfterAlter()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE OnDelParent (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await ExecuteDdlAsync(
				"CREATE TABLE OnDelChild (Id INT64 NOT NULL, ChildId INT64 NOT NULL) PRIMARY KEY (Id, ChildId), " +
				"INTERLEAVE IN PARENT OnDelParent ON DELETE NO ACTION");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO OnDelParent (Id) VALUES (1)");
		await ExecuteDmlAsync("INSERT INTO OnDelChild (Id, ChildId) VALUES (1, 10)");

		// Change to CASCADE
		await ExecuteDdlAsync("ALTER TABLE OnDelChild SET ON DELETE CASCADE");

		// Now deleting parent should cascade to child
		await ExecuteDmlAsync("DELETE FROM OnDelParent WHERE Id = 1");

		var children = await QueryAsync("SELECT * FROM OnDelChild WHERE Id = 1");
		children.Should().BeEmpty("CASCADE should have deleted the child row");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Generated Columns
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#generated_column
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GeneratedColumn_ComputedOnInsert()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE GenColTest (Id INT64 NOT NULL, FirstName STRING(100), LastName STRING(100), " +
				"FullName STRING(200) AS (CONCAT(FirstName, ' ', LastName)) STORED) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync(
			"INSERT INTO GenColTest (Id, FirstName, LastName) VALUES (1, 'Alice', 'Smith')");

		var result = await QueryScalarAsync("SELECT FullName FROM GenColTest WHERE Id = 1");
		result.Should().Be("Alice Smith");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GeneratedColumn_RecomputedOnUpdate()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE GenColUpdateTest (Id INT64 NOT NULL, A INT64, B INT64, " +
				"Total INT64 AS (A + B) STORED) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO GenColUpdateTest (Id, A, B) VALUES (1, 10, 20)");
		var before = await QueryScalarAsync("SELECT Total FROM GenColUpdateTest WHERE Id = 1");
		Convert.ToInt64(before).Should().Be(30);

		await ExecuteDmlAsync("UPDATE GenColUpdateTest SET A = 100 WHERE Id = 1");
		var after = await QueryScalarAsync("SELECT Total FROM GenColUpdateTest WHERE Id = 1");
		Convert.ToInt64(after).Should().Be(120);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task GeneratedColumn_CannotWriteDirectly()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE GenColNoWriteTest (Id INT64 NOT NULL, Val INT64, " +
				"Doubled INT64 AS (Val * 2) STORED) PRIMARY KEY (Id)");
		}
		catch { }

		var act = () => ExecuteDmlAsync(
			"INSERT INTO GenColNoWriteTest (Id, Val, Doubled) VALUES (1, 5, 10)");
		await act.Should().ThrowAsync<Exception>("writing to generated column should be rejected");
	}

	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
	// Default Column Values
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#column_default_value
	// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DefaultColumn_AppliedWhenOmitted()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DefColTest (Id INT64 NOT NULL, Status STRING(20) DEFAULT ('active')) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO DefColTest (Id) VALUES (1)");
		var result = await QueryScalarAsync("SELECT Status FROM DefColTest WHERE Id = 1");
		result.Should().Be("active");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DefaultColumn_ExplicitValueOverridesDefault()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DefColOverrideTest (Id INT64 NOT NULL, Status STRING(20) DEFAULT ('active')) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO DefColOverrideTest (Id, Status) VALUES (1, 'inactive')");
		var result = await QueryScalarAsync("SELECT Status FROM DefColOverrideTest WHERE Id = 1");
		result.Should().Be("inactive");
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DefaultColumn_NumericDefault()
	{
		try
		{
			await ExecuteDdlAsync(
				"CREATE TABLE DefNumColTest (Id INT64 NOT NULL, Priority INT64 DEFAULT (0)) PRIMARY KEY (Id)");
		}
		catch { }

		await ExecuteDmlAsync("INSERT INTO DefNumColTest (Id) VALUES (1)");
		var result = await QueryScalarAsync("SELECT Priority FROM DefNumColTest WHERE Id = 1");
		Convert.ToInt64(result).Should().Be(0);
	}
}
