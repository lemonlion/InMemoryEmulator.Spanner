using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Exhaustive parameter binding tests: all Spanner types bound via parameters.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParameterBindingExhaustiveIntegrationTests : IntegrationTestBase
{
	public ParameterBindingExhaustiveIntegrationTests(EmulatorSession session) : base(session) { }

	// ─── INT64 parameter ───
	[Theory]
	[InlineData(0L)]
	[InlineData(1L)]
	[InlineData(-1L)]
	[InlineData(long.MaxValue)]
	[InlineData(long.MinValue)]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Int64(long value)
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Int64, (object?)value));
		rows[0]["R"].Should().Be(value);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Int64_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Int64, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── STRING parameter ───
	[Theory]
	[InlineData("")]
	[InlineData("hello")]
	[InlineData("Hello, World!")]
	[InlineData("unicode: ÄÖÜ ♠♣♥♦")]
	[InlineData("with 'quotes'")]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_String(string value)
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.String, (object?)value));
		rows[0]["R"].Should().Be(value);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_String_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.String, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── BOOL parameter ───
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Bool(bool value)
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Bool, (object?)value));
		rows[0]["R"].Should().Be(value);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Bool_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Bool, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── FLOAT64 parameter ───
	[Theory]
	[InlineData(0.0)]
	[InlineData(3.14)]
	[InlineData(-1.5)]
	[InlineData(double.MaxValue)]
	[InlineData(double.MinValue)]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Float64(double value)
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Float64, (object?)value));
		((double)rows[0]["R"]!).Should().Be(value);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Float64_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Float64, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── DATE parameter ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Date()
	{
		var d = new DateTime(2024, 6, 15);
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Date, (object?)d));
		((DateTime)rows[0]["R"]!).Date.Should().Be(d.Date);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Date_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Date, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── TIMESTAMP parameter ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Timestamp()
	{
		var ts = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Timestamp, (object?)ts));
		rows[0]["R"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Timestamp_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Timestamp, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── BYTES parameter ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Bytes()
	{
		var data = new byte[] { 1, 2, 3, 4, 5 };
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Bytes, (object?)data));
		rows[0]["R"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Bytes_Empty()
	{
		var data = Array.Empty<byte>();
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Bytes, (object?)data));
		rows[0]["R"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Bytes_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Bytes, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── NUMERIC parameter ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Numeric()
	{
		var n = SpannerNumeric.Parse("3.14");
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Numeric, (object?)n));
		rows[0]["R"].Should().NotBeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_Numeric_Null()
	{
		var rows = await QueryAsync("SELECT @p AS R", ("p", SpannerDbType.Numeric, (object?)null));
		rows[0]["R"].Should().BeNull();
	}

	// ─── Parameters in WHERE ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InWhere()
	{
		var t = $"ParW_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'Alice'), (2, 'Bob')");
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Id = @id", ("id", SpannerDbType.Int64, (object?)1L));
		rows[0]["Name"].Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InWhere_String()
	{
		var t = $"ParWS_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'Alice'), (2, 'Bob')");
		var rows = await QueryAsync($"SELECT Id FROM {t} WHERE Name = @name", ("name", SpannerDbType.String, (object?)"Bob"));
		rows[0]["Id"].Should().Be(2L);
	}

	// ─── Multiple parameters ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task MultipleParams()
	{
		var rows = await QueryAsync("SELECT @a + @b AS R",
			("a", SpannerDbType.Int64, (object?)10L),
			("b", SpannerDbType.Int64, 20L));
		rows[0]["R"].Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task MultipleParams_DifferentTypes()
	{
		var rows = await QueryAsync("SELECT CONCAT(@s, CAST(@n AS STRING)) AS R",
			("s", SpannerDbType.String, (object?)"val="),
			("n", SpannerDbType.Int64, 42L));
		rows[0]["R"].Should().Be("val=42");
	}

	// ─── Parameter in INSERT DML ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InInsertDml()
	{
		var t = $"ParIns_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (@id, @name)",
			("id", SpannerDbType.Int64, (object?)1L),
			("name", SpannerDbType.String, "Alice"));
		var rows = await QueryAsync($"SELECT Name FROM {t}");
		rows[0]["Name"].Should().Be("Alice");
	}

	// ─── Parameter in UPDATE DML ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InUpdateDml()
	{
		var t = $"ParUpd_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 100)");
		await ExecuteDmlAsync($"UPDATE {t} SET Val = @newVal WHERE Id = @id",
			("newVal", SpannerDbType.Int64, (object?)200L),
			("id", SpannerDbType.Int64, 1L));
		var rows = await QueryAsync($"SELECT Val FROM {t}");
		rows[0]["Val"].Should().Be(200L);
	}

	// ─── Parameter in expression ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InExpression()
	{
		var rows = await QueryAsync("SELECT @x * 2 + @y AS R",
			("x", SpannerDbType.Int64, (object?)5L),
			("y", SpannerDbType.Int64, 3L));
		rows[0]["R"].Should().Be(13L);
	}

	// ─── Parameter in LIKE ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InLike()
	{
		var t = $"ParLike_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Name) VALUES (1, 'hello_world'), (2, 'goodbye')");
		var rows = await QueryAsync($"SELECT Name FROM {t} WHERE Name LIKE @pattern",
			("pattern", SpannerDbType.String, (object?)"%world%"));
		rows.Should().HaveCount(1);
		rows[0]["Name"].Should().Be("hello_world");
	}

	// ─── Parameter in BETWEEN ───
	[Fact]
	[Trait(TestTraits.Category, "ParameterBindingExhaustive")]
	public async Task Param_InBetween()
	{
		var t = $"ParBtw_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		await ExecuteDdlAsync($"CREATE TABLE {t} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await ExecuteDmlAsync($"INSERT INTO {t} (Id, Val) VALUES (1, 5), (2, 15), (3, 25)");
		var rows = await QueryAsync($"SELECT Val FROM {t} WHERE Val BETWEEN @lo AND @hi",
			("lo", SpannerDbType.Int64, (object?)10L),
			("hi", SpannerDbType.Int64, 20L));
		rows.Should().HaveCount(1);
		rows[0]["Val"].Should().Be(15L);
	}
}
