using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive NUMERIC type tests: arithmetic, CAST, comparison, functions, aggregates.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NumericExhaustiveIntegrationTests : IntegrationTestBase
{
	public NumericExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		var rows = await QueryAsync($"SELECT {expr} AS R");
		return rows[0]["R"];
	}

	// ─── NUMERIC literals via CAST ───
	[Theory]
	[InlineData("CAST(0 AS NUMERIC)", "0")]
	[InlineData("CAST(1 AS NUMERIC)", "1")]
	[InlineData("CAST(-1 AS NUMERIC)", "-1")]
	[InlineData("CAST(123456789 AS NUMERIC)", "123456789")]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastFromInt(string expr, string expected)
	{
		var result = await Eval(expr);
		result!.ToString().Should().StartWith(expected);
	}

	[Theory]
	[InlineData("CAST(3.14 AS NUMERIC)")]
	[InlineData("CAST(0.001 AS NUMERIC)")]
	[InlineData("CAST(-99.99 AS NUMERIC)")]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastFromFloat(string expr)
	{
		var result = await Eval(expr);
		result.Should().NotBeNull();
	}

	[Theory]
	[InlineData("CAST('3.14' AS NUMERIC)")]
	[InlineData("CAST('0' AS NUMERIC)")]
	[InlineData("CAST('-100.5' AS NUMERIC)")]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastFromString(string expr)
	{
		var result = await Eval(expr);
		result.Should().NotBeNull();
	}

	// ─── NUMERIC arithmetic ───
	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Addition()
	{
		var result = await Eval("CAST(1.5 AS NUMERIC) + CAST(2.5 AS NUMERIC)");
		result!.ToString().Should().StartWith("4");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Subtraction()
	{
		var result = await Eval("CAST(10 AS NUMERIC) - CAST(3.5 AS NUMERIC)");
		result!.ToString().Should().StartWith("6.5");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Multiplication()
	{
		var result = await Eval("CAST(3 AS NUMERIC) * CAST(4 AS NUMERIC)");
		result!.ToString().Should().StartWith("12");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Division()
	{
		var result = await Eval("CAST(10 AS NUMERIC) / CAST(4 AS NUMERIC)");
		result!.ToString().Should().StartWith("2.5");
	}

	// ─── NUMERIC in table ───
	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Numeric_InsertAndQuery()
	{
		var t = $"NumT_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val NUMERIC) PRIMARY KEY (Id)");
		await InsertAsync(t, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = SpannerNumeric.Parse("3.14") });
		var rows = await QueryAsync($"SELECT Val FROM {t}");
		rows[0]["Val"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_SumAggregate()
	{
		var t = $"NumAgg_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val NUMERIC) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, CAST(10.5 AS NUMERIC)), (2, CAST(20.5 AS NUMERIC))");
		var rows = await QueryAsync($"SELECT CAST(SUM(Val) AS STRING) AS R FROM {t}");
		rows[0]["R"]!.ToString().Should().StartWith("31");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_AvgAggregate()
	{
		var t = $"NumAvg_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val NUMERIC) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, CAST(10 AS NUMERIC)), (2, CAST(20 AS NUMERIC))");
		var rows = await QueryAsync($"SELECT CAST(AVG(Val) AS STRING) AS R FROM {t}");
		rows[0]["R"]!.ToString().Should().StartWith("15");
	}

	// ─── NUMERIC comparison ───
	[Theory]
	[InlineData("CAST(1.5 AS NUMERIC) > CAST(1.0 AS NUMERIC)", true)]
	[InlineData("CAST(1.0 AS NUMERIC) < CAST(1.5 AS NUMERIC)", true)]
	[InlineData("CAST(1.0 AS NUMERIC) = CAST(1.0 AS NUMERIC)", true)]
	[InlineData("CAST(1.0 AS NUMERIC) != CAST(1.5 AS NUMERIC)", true)]
	[InlineData("CAST(1.5 AS NUMERIC) >= CAST(1.5 AS NUMERIC)", true)]
	[InlineData("CAST(1.5 AS NUMERIC) <= CAST(1.5 AS NUMERIC)", true)]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Comparison(string expr, bool expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	// ─── NUMERIC with functions ───
	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Abs()
	{
		var result = await Eval("ABS(CAST(-5.5 AS NUMERIC))");
		result!.ToString().Should().StartWith("5.5");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Round()
	{
		var result = await Eval("ROUND(CAST(3.456 AS NUMERIC), 2)");
		result!.ToString().Should().Contain("3.46");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Trunc()
	{
		var result = await Eval("TRUNC(CAST(3.789 AS NUMERIC), 1)");
		result!.ToString().Should().Contain("3.7");
	}

	// ─── NULL NUMERIC ───
	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_Null()
	{
		var result = await Eval("CAST(NULL AS NUMERIC)");
		result.Should().BeNull();
	}

	// ─── NUMERIC CAST to other types ───
	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastToString()
	{
		var result = await Eval("CAST(CAST(42 AS NUMERIC) AS STRING)");
		result!.ToString().Should().Contain("42");
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastToFloat64()
	{
		var result = await Eval("CAST(CAST(3.14 AS NUMERIC) AS FLOAT64)");
		((double)result!).Should().BeApproximately(3.14, 0.01);
	}

	[Fact]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_CastToInt64()
	{
		var result = await Eval("CAST(CAST(42 AS NUMERIC) AS INT64)");
		result.Should().Be(42L);
	}

	// ─── NUMERIC literal syntax (NUMERIC 'value') ───
	[Theory]
	[InlineData("NUMERIC '42'", "42")]
	[InlineData("NUMERIC '3.14'", "3.14")]
	[InlineData("NUMERIC '0'", "0")]
	[Trait(TestTraits.Category, "NumericExhaustive")]
	public async Task Numeric_LiteralSyntax(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().NotBeNull();
		result!.ToString().Should().StartWith(expected);
	}
}
