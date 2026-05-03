using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for conditional expressions: CASE, COALESCE, IF, IFNULL, NULLIF, IIF.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ConditionalExpressionIntegrationTests : IntegrationTestBase
{
	public ConditionalExpressionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ─── CASE (simple form) ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case

	[Theory]
	[InlineData("CASE 1 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "one")]
	[InlineData("CASE 2 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "two")]
	[InlineData("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END", "other")]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_SimpleForm(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_SimpleForm_NoElse_ReturnsNull()
	{
		var result = await Eval("CASE 3 WHEN 1 THEN 'one' WHEN 2 THEN 'two' END");
		result.Should().BeNull();
	}

	// ─── CASE (searched form) ───

	[Theory]
	[InlineData("CASE WHEN 1 > 0 THEN 'positive' ELSE 'non-positive' END", "positive")]
	[InlineData("CASE WHEN 1 < 0 THEN 'negative' ELSE 'non-negative' END", "non-negative")]
	[InlineData("CASE WHEN 1 = 1 THEN 'yes' END", "yes")]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_SearchedForm(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_SearchedForm_NoElse_NoMatch_ReturnsNull()
	{
		var result = await Eval("CASE WHEN 1 < 0 THEN 'negative' END");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_SearchedForm_MultipleConditions()
	{
		var result = await Eval("CASE WHEN 5 > 10 THEN 'big' WHEN 5 > 3 THEN 'medium' WHEN 5 > 0 THEN 'small' ELSE 'zero' END");
		result.Should().Be("medium");
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_WithNullComparison()
	{
		// NULL = NULL is not true
		var result = await Eval("CASE WHEN CAST(NULL AS INT64) = CAST(NULL AS INT64) THEN 'match' ELSE 'no-match' END");
		result.Should().Be("no-match");
	}

	// ─── CASE in table queries ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_WithTableData()
	{
		var table = "CaseTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = 90L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Score"] = 75L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Score"] = 50L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Score"] = 30L });

		var rows = await QueryAsync($@"
			SELECT Id, CASE 
				WHEN Score >= 80 THEN 'A'
				WHEN Score >= 60 THEN 'B'
				WHEN Score >= 40 THEN 'C'
				ELSE 'F'
			END AS Grade
			FROM {table} ORDER BY Id");

		rows[0]["Grade"].Should().Be("A");
		rows[1]["Grade"].Should().Be("B");
		rows[2]["Grade"].Should().Be("C");
		rows[3]["Grade"].Should().Be("F");
	}

	// ─── COALESCE ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce

	[Theory]
	[InlineData("COALESCE(1, 2, 3)", 1L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), 2, 3)", 2L)]
	[InlineData("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64), 3)", 3L)]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Coalesce_ReturnsFirstNonNull(string expr, long expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Coalesce_AllNull_ReturnsNull()
	{
		var result = await Eval("COALESCE(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Coalesce_StringValues()
	{
		var result = await Eval("COALESCE(CAST(NULL AS STRING), 'hello')");
		result.Should().Be("hello");
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Coalesce_InWhereClause()
	{
		var table = "CoalTbl1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Nickname STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });  // Nickname is NULL
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Nickname"] = "Bobby" });

		var rows = await QueryAsync($"SELECT COALESCE(Nickname, Name) AS DisplayName FROM {table} ORDER BY Id");
		rows[0]["DisplayName"].Should().Be("Alice");
		rows[1]["DisplayName"].Should().Be("Bobby");
	}

	// ─── IF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if

	[Theory]
	[InlineData("IF(true, 'yes', 'no')", "yes")]
	[InlineData("IF(false, 'yes', 'no')", "no")]
	[InlineData("IF(1 = 1, 'equal', 'not equal')", "equal")]
	[InlineData("IF(1 = 2, 'equal', 'not equal')", "not equal")]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task If_BasicCases(string expr, string expected)
	{
		var result = await Eval(expr);
		result.Should().Be(expected);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task If_NullCondition_ReturnsFalseBranch()
	{
		var result = await Eval("IF(CAST(NULL AS BOOL), 'yes', 'no')");
		result.Should().Be("no");
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task If_WithNumericResult()
	{
		var result = await Eval("IF(5 > 3, 100, 200)");
		result.Should().Be(100L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task If_Nested()
	{
		var result = await Eval("IF(5 > 10, 'big', IF(5 > 3, 'medium', 'small'))");
		result.Should().Be("medium");
	}

	// ─── IFNULL ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task IfNull_NonNull_ReturnsFirstArg()
	{
		var result = await Eval("IFNULL(42, 0)");
		result.Should().Be(42L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task IfNull_Null_ReturnsSecondArg()
	{
		var result = await Eval("IFNULL(CAST(NULL AS INT64), 0)");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task IfNull_BothNull_ReturnsNull()
	{
		var result = await Eval("IFNULL(CAST(NULL AS INT64), CAST(NULL AS INT64))");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task IfNull_StringValues()
	{
		var result = await Eval("IFNULL(CAST(NULL AS STRING), 'default')");
		result.Should().Be("default");
	}

	// ─── NULLIF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task NullIf_Equal_ReturnsNull()
	{
		var result = await Eval("NULLIF(1, 1)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task NullIf_NotEqual_ReturnsFirstArg()
	{
		var result = await Eval("NULLIF(1, 2)");
		result.Should().Be(1L);
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task NullIf_StringValues_Equal()
	{
		var result = await Eval("NULLIF('hello', 'hello')");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task NullIf_StringValues_NotEqual()
	{
		var result = await Eval("NULLIF('hello', 'world')");
		result.Should().Be("hello");
	}

	// ─── Combining conditional expressions ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task If_WithCoalesce()
	{
		var result = await Eval("IF(COALESCE(CAST(NULL AS BOOL), false), 'yes', 'no')");
		result.Should().Be("no");
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_WithIfNull()
	{
		var result = await Eval("CASE WHEN IFNULL(CAST(NULL AS INT64), 0) = 0 THEN 'zero' ELSE 'non-zero' END");
		result.Should().Be("zero");
	}

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task NullIf_InCase()
	{
		var result = await Eval("CASE WHEN NULLIF(1, 1) IS NULL THEN 'null' ELSE 'not null' END");
		result.Should().Be("null");
	}

	// ─── CASE in GROUP BY ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_InGroupBy()
	{
		var table = "CaseGrp1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Score"] = 90L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Score"] = 85L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Score"] = 70L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Score"] = 60L });

		var rows = await QueryAsync($@"
			SELECT 
				CASE WHEN Score >= 80 THEN 'A' ELSE 'B' END AS Grade,
				COUNT(*) AS Cnt
			FROM {table} 
			GROUP BY CASE WHEN Score >= 80 THEN 'A' ELSE 'B' END
			ORDER BY Grade");

		rows.Should().HaveCount(2);
		rows[0]["Grade"].Should().Be("A");
		rows[0]["Cnt"].Should().Be(2L);
		rows[1]["Grade"].Should().Be("B");
		rows[1]["Cnt"].Should().Be(2L);
	}

	// ─── CASE in ORDER BY ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_InOrderBy()
	{
		var table = "CaseOrd1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "B" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "C" });

		// Custom sort: C first, then A, then B
		var rows = await QueryAsync($@"
			SELECT Id FROM {table} ORDER BY 
				CASE Category WHEN 'C' THEN 1 WHEN 'A' THEN 2 WHEN 'B' THEN 3 END");
		rows.Select(r => (long)r["Id"]!).Should().Equal(3L, 2L, 1L);
	}

	// ─── CASE with aggregates ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_InsideAggregate()
	{
		var table = "CaseAgg1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Status STRING(MAX), Amount INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Status"] = "active", ["Amount"] = 100L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Status"] = "inactive", ["Amount"] = 50L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Status"] = "active", ["Amount"] = 200L });

		var rows = await QueryAsync($@"
			SELECT 
				SUM(CASE WHEN Status = 'active' THEN Amount ELSE 0 END) AS ActiveTotal,
				SUM(CASE WHEN Status = 'inactive' THEN Amount ELSE 0 END) AS InactiveTotal
			FROM {table}");

		rows[0]["ActiveTotal"].Should().Be(300L);
		rows[0]["InactiveTotal"].Should().Be(50L);
	}

	// ─── COUNT with CASE for conditional counting ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task CountCase_ConditionalCounting()
	{
		var table = "CaseAgg2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Status STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Status"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Status"] = "B" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Status"] = "A" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 4L, ["Status"] = "A" });

		var rows = await QueryAsync($@"
			SELECT 
				COUNTIF(Status = 'A') AS CountA,
				COUNTIF(Status = 'B') AS CountB
			FROM {table}");

		rows[0]["CountA"].Should().Be(3L);
		rows[0]["CountB"].Should().Be(1L);
	}

	// ─── Nested CASE ───

	[Fact]
	[Trait(TestTraits.Category, "ConditionalExpression")]
	public async Task Case_Nested()
	{
		var result = await Eval(@"
			CASE 
				WHEN 5 > 10 THEN 'big' 
				WHEN 5 > 3 THEN 
					CASE WHEN 5 > 4 THEN 'medium-high' ELSE 'medium-low' END
				ELSE 'small' 
			END");
		result.Should().Be("medium-high");
	}
}
