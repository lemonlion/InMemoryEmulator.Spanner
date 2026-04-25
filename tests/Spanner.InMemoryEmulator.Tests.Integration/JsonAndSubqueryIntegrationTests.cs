using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// JSON function tests and subquery combinations.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries
/// </summary>
[Collection(IntegrationCollection.Name)]
public class JsonAndSubqueryIntegrationTests : IntegrationTestBase
{
	public JsonAndSubqueryIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// TO_JSON_STRING
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#to_json_string
	// ═══════════════════════════════════════════════════════════════

	// Go emulator: TO_JSON_STRING is not supported on non-JSON types (StatusCode=Unimplemented).
	[Theory]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	[InlineData("TO_JSON_STRING(1)", "1")]
	[InlineData("TO_JSON_STRING(0)", "0")]
	[InlineData("TO_JSON_STRING(-1)", "-1")]
	[InlineData("TO_JSON_STRING(TRUE)", "true")]
	[InlineData("TO_JSON_STRING(FALSE)", "false")]
	[InlineData("TO_JSON_STRING('hello')", "\"hello\"")]
	[InlineData("TO_JSON_STRING('')", "\"\"")]
	public async Task ToJsonString_Scalars(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// Go emulator: TO_JSON_STRING is not supported on non-JSON types (StatusCode=Unimplemented).
	[Theory]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	[InlineData("TO_JSON_STRING(CAST(NULL AS INT64))", "null")]
	[InlineData("TO_JSON_STRING(CAST(NULL AS STRING))", "null")]
	[InlineData("TO_JSON_STRING(CAST(NULL AS BOOL))", "null")]
	public async Task ToJsonString_Null(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE (requires JSON type support)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE(JSON '{\"a\": \"hello\"}', '$.a')", "hello")]
	[InlineData("JSON_VALUE(JSON '{\"a\": 1}', '$.a')", "1")]
	[InlineData("JSON_VALUE(JSON '{\"a\": true}', '$.a')", "true")]
	[InlineData("JSON_VALUE(JSON '{\"a\": {\"b\": \"nested\"}}', '$.a.b')", "nested")]
	[InlineData("JSON_VALUE(JSON '{\"x\": \"y\"}', '$.z')", null)]
	public async Task JsonValue_Combinations(string expr, string? expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// JSON_QUERY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_QUERY(JSON '{\"a\": [1, 2, 3]}', '$.a')", "[1,2,3]")]
	[InlineData("JSON_QUERY(JSON '{\"a\": {\"b\": 1}}', '$.a')", "{\"b\":1}")]
	public async Task JsonQuery_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// PARSE_JSON
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#parse_json
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"a\": \"b\"}'), '$.a')", "b")]
	[InlineData("JSON_VALUE(PARSE_JSON('{\"x\": 42}'), '$.x')", "42")]
	public async Task ParseJson_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// JSON_TYPE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_type
	// ═══════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("JSON_TYPE(JSON '\"hello\"')", "string")]
	[InlineData("JSON_TYPE(JSON '42')", "number")]
	[InlineData("JSON_TYPE(JSON 'true')", "boolean")]
	[InlineData("JSON_TYPE(JSON 'null')", "null")]
	[InlineData("JSON_TYPE(JSON '[1,2]')", "array")]
	[InlineData("JSON_TYPE(JSON '{\"a\":1}')", "object")]
	public async Task JsonType_Combinations(string expr, string expected) =>
		(await Eval(expr)).Should().Be(expected);

	// ═══════════════════════════════════════════════════════════════
	// Scalar subqueries
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery_concepts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ScalarSubquery_InSelect()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubQ1 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("SubQ1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync(
			"SELECT Id, Val, (SELECT MAX(Val) FROM SubQ1) AS MaxVal FROM SubQ1 ORDER BY Id");
		rows.Should().HaveCount(3);
		rows.Should().AllSatisfy(r => r["MaxVal"].Should().Be(30L));
	}

	[Fact]
	public async Task ScalarSubquery_InWhere()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubQ2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("SubQ2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync(
			"SELECT Id FROM SubQ2 WHERE Val > (SELECT AVG(Val) FROM SubQ2) ORDER BY Id");
		rows.Should().ContainSingle().Which["Id"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// EXISTS subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subquery_concepts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Exists_True()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubExist1 (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("SubExist1", new Dictionary<string, object?> { ["Id"] = 1L });

		var result = await Eval("EXISTS(SELECT 1 FROM SubExist1)");
		result.Should().Be(true);
	}

	[Fact]
	public async Task Exists_False()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubExist2 (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		var result = await Eval("EXISTS(SELECT 1 FROM SubExist2 WHERE Id = 999)");
		result.Should().Be(false);
	}

	[Fact]
	public async Task NotExists()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubNotExist (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		var result = await Eval("NOT EXISTS(SELECT 1 FROM SubNotExist)");
		result.Should().Be(true);
	}

	// ═══════════════════════════════════════════════════════════════
	// IN subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#in_subquery_concepts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task In_Subquery()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE SubIn1 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync("CREATE TABLE SubIn2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("SubIn1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });
		await InsertAsync("SubIn2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 30L });

		var rows = await QueryAsync(
			"SELECT Id FROM SubIn1 WHERE Val IN (SELECT Val FROM SubIn2) ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(2L);
		rows[1]["Id"].Should().Be(3L);
	}

	[Fact]
	public async Task NotIn_Subquery()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE SubNotIn1 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
			await ExecuteDdlAsync("CREATE TABLE SubNotIn2 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("SubNotIn1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });
		await InsertAsync("SubNotIn2",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 20L });

		var rows = await QueryAsync(
			"SELECT Id FROM SubNotIn1 WHERE Val NOT IN (SELECT Val FROM SubNotIn2) ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(1L);
		rows[1]["Id"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// FROM subquery (derived table)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task From_Subquery()
	{
		try { await ExecuteDdlAsync("CREATE TABLE SubFrom1 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("SubFrom1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync(
			"SELECT S.Id, S.Doubled FROM (SELECT Id, Val * 2 AS Doubled FROM SubFrom1) S ORDER BY S.Id");
		rows[0]["Doubled"].Should().Be(20L);
		rows[1]["Doubled"].Should().Be(40L);
		rows[2]["Doubled"].Should().Be(60L);
	}

	// ═══════════════════════════════════════════════════════════════
	// CTEs (WITH clause)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#with_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CTE_Basic()
	{
		try { await ExecuteDdlAsync("CREATE TABLE CteTbl (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("CteTbl",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L });

		var rows = await QueryAsync(@"
			WITH cte AS (SELECT Id, Val * 2 AS Doubled FROM CteTbl)
			SELECT Id, Doubled FROM cte ORDER BY Id");
		rows[0]["Doubled"].Should().Be(20L);
		rows[1]["Doubled"].Should().Be(40L);
	}

	[Fact]
	public async Task CTE_MultipleCTEs()
	{
		try { await ExecuteDdlAsync("CREATE TABLE CteMulti (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("CteMulti",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync(@"
			WITH
				high AS (SELECT Id, Val FROM CteMulti WHERE Val >= 20),
				doubled AS (SELECT Id, Val * 2 AS V FROM high)
			SELECT Id, V FROM doubled ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["V"].Should().Be(40L);
		rows[1]["V"].Should().Be(60L);
	}

	[Fact]
	public async Task CTE_WithAggregate()
	{
		try { await ExecuteDdlAsync("CREATE TABLE CteAgg (Id INT64 NOT NULL, Grp STRING(10), Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("CteAgg",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Grp"] = "A", ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Grp"] = "A", ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Grp"] = "B", ["Val"] = 30L });

		var rows = await QueryAsync(@"
			WITH sums AS (SELECT Grp, SUM(Val) AS Total FROM CteAgg GROUP BY Grp)
			SELECT Grp, Total FROM sums ORDER BY Grp");
		rows[0]["Total"].Should().Be(30L);
		rows[1]["Total"].Should().Be(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// UNION ALL, EXCEPT DISTINCT, INTERSECT DISTINCT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UnionAll()
	{
		var rows = await QueryAsync("SELECT 1 AS V UNION ALL SELECT 2 UNION ALL SELECT 3");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task UnionAll_WithDuplicates()
	{
		var rows = await QueryAsync("SELECT 1 AS V UNION ALL SELECT 1 UNION ALL SELECT 1");
		rows.Should().HaveCount(3);
	}

	[Fact]
	public async Task UnionDistinct()
	{
		var rows = await QueryAsync("SELECT 1 AS V UNION DISTINCT SELECT 1 UNION DISTINCT SELECT 2");
		rows.Should().HaveCount(2);
	}

	[Fact]
	public async Task ExceptDistinct()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE ExceptA (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await ExecuteDdlAsync("CREATE TABLE ExceptB (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("ExceptA",
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L });
		await InsertAsync("ExceptB",
			new Dictionary<string, object?> { ["Id"] = 2L });

		var rows = await QueryAsync(
			"SELECT Id FROM ExceptA EXCEPT DISTINCT SELECT Id FROM ExceptB ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(1L);
		rows[1]["Id"].Should().Be(3L);
	}

	[Fact]
	public async Task IntersectDistinct()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE InterA (Id INT64 NOT NULL) PRIMARY KEY (Id)");
			await ExecuteDdlAsync("CREATE TABLE InterB (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("InterA",
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L });
		await InsertAsync("InterB",
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L },
			new Dictionary<string, object?> { ["Id"] = 4L });

		var rows = await QueryAsync(
			"SELECT Id FROM InterA INTERSECT DISTINCT SELECT Id FROM InterB ORDER BY Id");
		rows.Should().HaveCount(2);
		rows[0]["Id"].Should().Be(2L);
		rows[1]["Id"].Should().Be(3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// ARRAY subquery
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery_concepts
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ArraySubquery()
	{
		try { await ExecuteDdlAsync("CREATE TABLE ArrSubQ (Id INT64 NOT NULL) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("ArrSubQ",
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L },
			new Dictionary<string, object?> { ["Id"] = 3L });

		var rows = await QueryAsync("SELECT ARRAY(SELECT Id FROM ArrSubQ ORDER BY Id) AS Arr");
		var arr = (List<long>)rows[0]["Arr"]!;
		arr.Should().Equal(1L, 2L, 3L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Correlated subquery
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task CorrelatedSubquery()
	{
		try
		{
			await ExecuteDdlAsync("CREATE TABLE CorrMain (Id INT64 NOT NULL, Grp STRING(10), Val INT64) PRIMARY KEY (Id)");
		}
		catch { }

		await InsertAsync("CorrMain",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Grp"] = "A", ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Grp"] = "A", ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Grp"] = "B", ["Val"] = 30L });

		var rows = await QueryAsync(@"
			SELECT m.Id, m.Val,
				(SELECT MAX(m2.Val) FROM CorrMain m2 WHERE m2.Grp = m.Grp) AS GrpMax
			FROM CorrMain m ORDER BY m.Id");
		rows[0]["GrpMax"].Should().Be(20L);
		rows[1]["GrpMax"].Should().Be(20L);
		rows[2]["GrpMax"].Should().Be(30L);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested subqueries
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NestedSubqueries()
	{
		try { await ExecuteDdlAsync("CREATE TABLE Nested1 (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)"); } catch { }
		await InsertAsync("Nested1",
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync(@"
			SELECT Id FROM Nested1
			WHERE Val > (SELECT AVG(Val) FROM (SELECT Val FROM Nested1 WHERE Val > 0) sub)
			ORDER BY Id");
		rows.Should().ContainSingle().Which["Id"].Should().Be(3L);
	}
}
