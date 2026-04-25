using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for Phase 12: Core SQL Functions.
/// Tests flow through the full gRPC pipeline: SpannerConnection → gRPC → FakeSpannerService.
/// Each test uses a dummy table with one row to satisfy the FROM clause requirement.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FunctionIntegrationTests
{
	private readonly ITestDatabaseFixture _fixture;
	private bool _initialized;

	public FunctionIntegrationTests(EmulatorSession session)
	{
		_fixture = TestFixtureFactory.Create(session);
		EnsureTable();
	}

	private void EnsureTable()
	{
		if (_initialized) return;
		var db = _fixture.Database!;
		try
		{
			db.ExecuteDdl("CREATE TABLE FnDummy (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			db.Insert("FnDummy", new Dictionary<string, object?> { ["Id"] = 1L });
		}
		catch { /* table may already exist from another test run */ }
		_initialized = true;
	}

	private async Task<object?> ScalarAsync(string selectExpr)
	{
		using var conn = _fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {selectExpr} AS R FROM FnDummy WHERE Id=1");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── STRING FUNCTIONS ───

	[Fact] public async Task Concat() => (await ScalarAsync("CONCAT('a','b','c')")).Should().Be("abc");

	[Fact] public async Task Length() => (await ScalarAsync("LENGTH('hello')")).Should().Be(5L);

	[Fact] public async Task Lower() => (await ScalarAsync("LOWER('HELLO')")).Should().Be("hello");

	[Fact] public async Task Upper() => (await ScalarAsync("UPPER('hello')")).Should().Be("HELLO");

	[Fact] public async Task Trim() => (await ScalarAsync("TRIM('  hi  ')")).Should().Be("hi");

	[Fact] public async Task Ltrim() => (await ScalarAsync("LTRIM('  hi')")).Should().Be("hi");

	[Fact] public async Task Rtrim() => (await ScalarAsync("RTRIM('hi  ')")).Should().Be("hi");

	[Fact] public async Task Substr() => (await ScalarAsync("SUBSTR('hello', 2, 3)")).Should().Be("ell");

	[Fact] public async Task StartsWith() => (await ScalarAsync("STARTS_WITH('hello', 'hel')")).Should().Be(true);

	[Fact] public async Task EndsWith() => (await ScalarAsync("ENDS_WITH('hello', 'llo')")).Should().Be(true);

	[Fact] public async Task Replace_Fn() => (await ScalarAsync("REPLACE('hello', 'ell', 'ELL')")).Should().Be("hELLo");

	[Fact] public async Task Reverse() => (await ScalarAsync("REVERSE('abc')")).Should().Be("cba");

	[Fact] public async Task Strpos() => (await ScalarAsync("STRPOS('hello', 'llo')")).Should().Be(3L);

	[Fact] public async Task Split() => (await ScalarAsync("ARRAY_TO_STRING(SPLIT('a,b,c', ','), '|')")).Should().Be("a|b|c");

	[Fact] public async Task Lpad() => (await ScalarAsync("LPAD('hi', 5, '-')")).Should().Be("---hi");

	[Fact] public async Task Rpad() => (await ScalarAsync("RPAD('hi', 5, '-')")).Should().Be("hi---");

	[Fact] public async Task Repeat() => (await ScalarAsync("REPEAT('ab', 3)")).Should().Be("ababab");

	[Fact] public async Task RegexpContains() => (await ScalarAsync("REGEXP_CONTAINS('hello123', '[0-9]+')")).Should().Be(true);

	[Fact] public async Task RegexpExtract() => (await ScalarAsync("REGEXP_EXTRACT('hello123', '[0-9]+')")).Should().Be("123");

	[Fact] public async Task RegexpReplace() => (await ScalarAsync("REGEXP_REPLACE('abc123', '[0-9]+', 'X')")).Should().Be("abcX");

	[Fact] public async Task Left() => (await ScalarAsync("LEFT('hello', 3)")).Should().Be("hel");

	[Fact] public async Task Right() => (await ScalarAsync("RIGHT('hello', 3)")).Should().Be("llo");

	[Fact] public async Task Initcap() => (await ScalarAsync("INITCAP('hello world')")).Should().Be("Hello World");

	// ─── MATH FUNCTIONS ───

	[Fact] public async Task Abs() => (await ScalarAsync("ABS(-5)")).Should().Be(5L);

	[Fact] public async Task Mod() => (await ScalarAsync("MOD(10, 3)")).Should().Be(1L);

	[Fact] public async Task Ceil() => (await ScalarAsync("CEIL(3.2)")).Should().Be(4.0);

	[Fact] public async Task Floor() => (await ScalarAsync("FLOOR(3.8)")).Should().Be(3.0);

	[Fact] public async Task Round() => (await ScalarAsync("ROUND(3.456, 2)")).Should().Be(3.46);

	[Fact] public async Task Trunc() => (await ScalarAsync("TRUNC(3.456, 1)")).Should().Be(3.4);

	[Fact] public async Task Sign_Negative() => (await ScalarAsync("SIGN(-5)")).Should().Be(-1L);

	[Fact] public async Task Sign_Zero() => (await ScalarAsync("SIGN(0)")).Should().Be(0L);

	[Fact] public async Task Greatest() => (await ScalarAsync("GREATEST(1, 5, 3)")).Should().Be(5L);

	[Fact] public async Task Least() => (await ScalarAsync("LEAST(1, 5, 3)")).Should().Be(1L);

	[Fact] public async Task Div() => (await ScalarAsync("DIV(10, 3)")).Should().Be(3L);

	[Fact] public async Task Sqrt() => (await ScalarAsync("SQRT(9.0)")).Should().Be(3.0);

	[Fact] public async Task Pow() => (await ScalarAsync("POW(2.0, 3.0)")).Should().Be(8.0);

	[Fact] public async Task Exp() => ((double)(await ScalarAsync("EXP(1.0)"))!).Should().BeApproximately(Math.E, 0.0001);

	[Fact] public async Task Ln() => ((double)(await ScalarAsync("LN(2.718281828)")!)!).Should().BeApproximately(1.0, 0.001);

	[Fact] public async Task Log10() => (await ScalarAsync("LOG10(100.0)")).Should().Be(2.0);

	[Fact] public async Task IsNan() => (await ScalarAsync("IS_NAN(IEEE_DIVIDE(0.0, 0.0))")).Should().Be(true);

	[Fact] public async Task IsInf() => (await ScalarAsync("IS_INF(IEEE_DIVIDE(1.0, 0.0))")).Should().Be(true);

	[Fact] public async Task IeeeDivideByZero() => (await ScalarAsync("IEEE_DIVIDE(1.0, 0.0)")).Should().Be(double.PositiveInfinity);

	// ─── CONDITIONAL FUNCTIONS ───

	[Fact] public async Task If_True() => (await ScalarAsync("IF(true, 'yes', 'no')")).Should().Be("yes");

	[Fact] public async Task If_False() => (await ScalarAsync("IF(false, 'yes', 'no')")).Should().Be("no");

	[Fact] public async Task Ifnull() => (await ScalarAsync("IFNULL(NULL, 'default')")).Should().Be("default");

	[Fact] public async Task Nullif_Same() => (await ScalarAsync("NULLIF(1, 1)")).Should().BeNull();

	[Fact] public async Task Nullif_Different() => (await ScalarAsync("NULLIF(1, 2)")).Should().Be(1L);

	[Fact] public async Task Coalesce() => (await ScalarAsync("COALESCE(NULL, NULL, 'found')")).Should().Be("found");

	// ─── DATE/TIME FUNCTIONS ───

	[Fact] public async Task CurrentTimestamp() => (await ScalarAsync("CURRENT_TIMESTAMP()")).Should().BeOfType<DateTime>();

	[Fact] public async Task CurrentDate()
	{
		var result = await ScalarAsync("CURRENT_DATE()");
		result.Should().BeOfType<DateTime>();
	}

	// ─── CAST ───

	[Fact] public async Task CastIntToString() => (await ScalarAsync("CAST(42 AS STRING)")).Should().Be("42");

	[Fact] public async Task CastStringToInt() => (await ScalarAsync("CAST('42' AS INT64)")).Should().Be(42L);

	[Fact] public async Task CastFloatToInt() => (await ScalarAsync("CAST(3.9 AS INT64)")).Should().Be(3L);

	[Fact] public async Task SafeCastInvalid() => (await ScalarAsync("SAFE_CAST('abc' AS INT64)")).Should().BeNull();

	// ─── ARRAY FUNCTIONS ───

	[Fact] public async Task ArrayLength() => (await ScalarAsync("ARRAY_LENGTH([1,2,3])")).Should().Be(3L);

	[Fact] public async Task ArrayToString() => (await ScalarAsync("ARRAY_TO_STRING([1,2,3], ',')")).Should().Be("1,2,3");

	// ─── JSON FUNCTIONS ───

	[Fact] public async Task JsonValue() => (await ScalarAsync("JSON_VALUE('{\"a\":1}', '$.a')")).Should().Be("1");

	[Fact] public async Task JsonQuery() => (await ScalarAsync("JSON_QUERY('{\"a\":{\"b\":2}}', '$.a')")).Should().Be("{\"b\":2}");

	[Fact] public async Task JsonType() => (await ScalarAsync("JSON_TYPE(PARSE_JSON('{\"a\":1}'))")).Should().Be("object");

	// ─── NULL PROPAGATION ───

	[Fact] public async Task NullPropagation_Length() => (await ScalarAsync("LENGTH(NULL)")).Should().BeNull();

	[Fact] public async Task NullPropagation_Abs() => (await ScalarAsync("ABS(NULL)")).Should().BeNull();

	[Fact] public async Task NullPropagation_Lower() => (await ScalarAsync("LOWER(NULL)")).Should().BeNull();
}
