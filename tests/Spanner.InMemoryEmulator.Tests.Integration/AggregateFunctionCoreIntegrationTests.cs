using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for aggregate functions: COUNT, SUM, AVG, MIN, MAX, STRING_AGG, ARRAY_AGG,
/// LOGICAL_AND, LOGICAL_OR, and their interactions with NULL, DISTINCT, and GROUP BY.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AggregateFunctionCoreIntegrationTests : IntegrationTestBase
{
	public AggregateFunctionCoreIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string sql)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand(sql);
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task SetupNumbersTable(string table)
	{
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64, FVal FLOAT64, Name STRING(MAX), Active BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L, ["FVal"] = 10.5, ["Name"] = "Alice", ["Active"] = true },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L, ["FVal"] = 20.5, ["Name"] = "Bob", ["Active"] = true },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L, ["FVal"] = 30.5, ["Name"] = "Charlie", ["Active"] = false },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Val"] = 20L, ["FVal"] = 20.5, ["Name"] = "Diana", ["Active"] = true },
			new Dictionary<string, object?> { ["Id"] = 5L });  // All nullable cols are NULL
	}

	// ─── COUNT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#count

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Count_Star_CountsAllRows()
	{
		var table = "AggCnt1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT COUNT(*) FROM {table}");
		result.Should().Be(5L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Count_Column_ExcludesNull()
	{
		var table = "AggCnt2";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT COUNT(Val) FROM {table}");
		result.Should().Be(4L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Count_Distinct()
	{
		var table = "AggCnt3";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT COUNT(DISTINCT Val) FROM {table}");
		result.Should().Be(3L); // 10, 20, 30
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Count_EmptyTable()
	{
		var table = "AggCntEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT COUNT(*) FROM {table}");
		result.Should().Be(0L);
	}

	// ─── SUM ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#sum

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Sum_IntColumn()
	{
		var table = "AggSum1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT SUM(Val) FROM {table}");
		result.Should().Be(80L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Sum_FloatColumn()
	{
		var table = "AggSum2";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT SUM(FVal) FROM {table}");
		((double)result!).Should().BeApproximately(82.0, 0.01);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Sum_EmptyTable_ReturnsNull()
	{
		var table = "AggSumEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT SUM(Val) FROM {table}");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Sum_AllNull_ReturnsNull()
	{
		var table = "AggSumNull";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L });
		var result = await Eval($"SELECT SUM(Val) FROM {table}");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Sum_Distinct()
	{
		var table = "AggSumD";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT SUM(DISTINCT Val) FROM {table}");
		result.Should().Be(60L); // 10 + 20 + 30
	}

	// ─── AVG ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#avg

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Avg_IntColumn()
	{
		var table = "AggAvg1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT AVG(Val) FROM {table}");
		((double)result!).Should().BeApproximately(20.0, 0.01);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Avg_EmptyTable_ReturnsNull()
	{
		var table = "AggAvgEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT AVG(Val) FROM {table}");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Avg_Distinct()
	{
		var table = "AggAvgD";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT AVG(DISTINCT Val) FROM {table}");
		((double)result!).Should().BeApproximately(20.0, 0.01); // (10+20+30)/3
	}

	// ─── MIN / MAX ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#min

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Min_IntColumn()
	{
		var table = "AggMin1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT MIN(Val) FROM {table}");
		result.Should().Be(10L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Max_IntColumn()
	{
		var table = "AggMax1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT MAX(Val) FROM {table}");
		result.Should().Be(30L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Min_StringColumn()
	{
		var table = "AggMinS";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT MIN(Name) FROM {table}");
		result.Should().Be("Alice");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Max_StringColumn()
	{
		var table = "AggMaxS";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT MAX(Name) FROM {table}");
		result.Should().Be("Diana");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Min_EmptyTable_ReturnsNull()
	{
		var table = "AggMinEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT MIN(Val) FROM {table}");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Max_EmptyTable_ReturnsNull()
	{
		var table = "AggMaxEmpty";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT MAX(Val) FROM {table}");
		result.Should().BeNull();
	}

	// ─── STRING_AGG ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#string_agg

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task StringAgg_Default()
	{
		var table = "AggStrAgg1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "b" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "c" });

		var result = await Eval($"SELECT STRING_AGG(Name, ',' ORDER BY Id) FROM {table}");
		result.Should().Be("a,b,c");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task StringAgg_WithSeparator()
	{
		var table = "AggStrAgg2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "x" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "y" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "z" });

		var result = await Eval($"SELECT STRING_AGG(Name, ' - ' ORDER BY Id) FROM {table}");
		result.Should().Be("x - y - z");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task StringAgg_SkipsNull()
	{
		var table = "AggStrAgg3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "a" });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L });  // NULL
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "c" });

		var result = await Eval($"SELECT STRING_AGG(Name, ',' ORDER BY Id) FROM {table}");
		result.Should().Be("a,c");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task StringAgg_EmptyTable_ReturnsNull()
	{
		var table = "AggStrAggE";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
		var result = await Eval($"SELECT STRING_AGG(Name, ',') FROM {table}");
		result.Should().BeNull();
	}

	// ─── ARRAY_AGG ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#array_agg

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ArrayAgg_BasicCase()
	{
		var table = "AggArr1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 10L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 20L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 30L });

		var rows = await QueryAsync($"SELECT v FROM UNNEST(ARRAY(SELECT Val FROM {table} ORDER BY Id)) AS v");
		rows.Select(r => (long)r["v"]!).Should().Equal(10L, 20L, 30L);
	}

	// ─── LOGICAL_AND / LOGICAL_OR ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#logical_and

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task LogicalAnd_AllTrue()
	{
		var table = "AggLogAnd1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Flag"] = true });

		var result = await Eval($"SELECT LOGICAL_AND(Flag) FROM {table}");
		result.Should().Be(true);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task LogicalAnd_SomeFalse()
	{
		var table = "AggLogAnd2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = true });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = false });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 3L, ["Flag"] = true });

		var result = await Eval($"SELECT LOGICAL_AND(Flag) FROM {table}");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task LogicalOr_AllFalse()
	{
		var table = "AggLogOr1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = false });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = false });

		var result = await Eval($"SELECT LOGICAL_OR(Flag) FROM {table}");
		result.Should().Be(false);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task LogicalOr_SomeTrue()
	{
		var table = "AggLogOr2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Flag BOOL) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L, ["Flag"] = false });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L, ["Flag"] = true });

		var result = await Eval($"SELECT LOGICAL_OR(Flag) FROM {table}");
		result.Should().Be(true);
	}

	// ─── COUNTIF ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#countif

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task CountIf_BasicCase()
	{
		var table = "AggCntIf1";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT COUNTIF(Active) FROM {table}");
		result.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task CountIf_WithExpression()
	{
		var table = "AggCntIf2";
		await SetupNumbersTable(table);
		var result = await Eval($"SELECT COUNTIF(Val > 15) FROM {table}");
		result.Should().Be(3L); // 20, 30, 20
	}

	// ─── GROUP BY with aggregates ───

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task GroupBy_WithMultipleAggregates()
	{
		var table = "AggGrp1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX), Amount INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "A", ["Amount"] = 100L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "B", ["Amount"] = 200L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "A", ["Amount"] = 150L },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Category"] = "B", ["Amount"] = 250L },
			new Dictionary<string, object?> { ["Id"] = 5L, ["Category"] = "A", ["Amount"] = 50L });

		var rows = await QueryAsync($@"
			SELECT Category, COUNT(*) AS Cnt, SUM(Amount) AS Total, AVG(Amount) AS Avg, MIN(Amount) AS Min, MAX(Amount) AS Max
			FROM {table} 
			GROUP BY Category 
			ORDER BY Category");

		rows.Should().HaveCount(2);
		rows[0]["Category"].Should().Be("A");
		rows[0]["Cnt"].Should().Be(3L);
		rows[0]["Total"].Should().Be(300L);
		rows[0]["Min"].Should().Be(50L);
		rows[0]["Max"].Should().Be(150L);
		rows[1]["Category"].Should().Be("B");
		rows[1]["Cnt"].Should().Be(2L);
		rows[1]["Total"].Should().Be(450L);
	}

	// ─── HAVING ───

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Having_FiltersGroups()
	{
		var table = "AggHav1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX), Amount INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "A", ["Amount"] = 100L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "B", ["Amount"] = 200L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "A", ["Amount"] = 150L },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Category"] = "C", ["Amount"] = 50L });

		var rows = await QueryAsync($@"
			SELECT Category, SUM(Amount) AS Total
			FROM {table} 
			GROUP BY Category 
			HAVING SUM(Amount) > 100
			ORDER BY Category");

		rows.Should().HaveCount(2);
		rows[0]["Category"].Should().Be("A");
		rows[1]["Category"].Should().Be("B");
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Having_WithCount()
	{
		var table = "AggHav2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX)) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "A" },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "B" },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "A" },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Category"] = "A" },
			new Dictionary<string, object?> { ["Id"] = 5L, ["Category"] = "B" });

		var rows = await QueryAsync($@"
			SELECT Category, COUNT(*) AS Cnt
			FROM {table} 
			GROUP BY Category 
			HAVING COUNT(*) >= 3");

		rows.Should().HaveCount(1);
		rows[0]["Category"].Should().Be("A");
	}

	// ─── Aggregate with NULL in all values ───

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task Aggregate_AllNullValues()
	{
		var table = "AggAllNull";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 1L });
		await InsertAsync(table, new Dictionary<string, object?> { ["Id"] = 2L });

		var rows = await QueryAsync($@"
			SELECT COUNT(*) AS C, COUNT(Val) AS CV, SUM(Val) AS S, MIN(Val) AS MN, MAX(Val) AS MX
			FROM {table}");

		rows[0]["C"].Should().Be(2L);
		rows[0]["CV"].Should().Be(0L);
		rows[0]["S"].Should().BeNull();
		rows[0]["MN"].Should().BeNull();
		rows[0]["MX"].Should().BeNull();
	}

	// ─── Multiple GROUP BY columns ───

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	public async Task GroupBy_MultipleColumns()
	{
		var table = "AggGrpM";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Dept STRING(MAX), Role STRING(MAX), Salary INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Dept"] = "Eng", ["Role"] = "Dev", ["Salary"] = 100L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Dept"] = "Eng", ["Role"] = "Dev", ["Salary"] = 120L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Dept"] = "Eng", ["Role"] = "QA", ["Salary"] = 90L },
			new Dictionary<string, object?> { ["Id"] = 4L, ["Dept"] = "Sales", ["Role"] = "Rep", ["Salary"] = 80L });

		var rows = await QueryAsync($@"
			SELECT Dept, Role, COUNT(*) AS Cnt, SUM(Salary) AS Total
			FROM {table} 
			GROUP BY Dept, Role 
			ORDER BY Dept, Role");

		rows.Should().HaveCount(3);
		rows[0]["Dept"].Should().Be("Eng");
		rows[0]["Role"].Should().Be("Dev");
		rows[0]["Cnt"].Should().Be(2L);
		rows[0]["Total"].Should().Be(220L);
	}

	// ─── APPROX_COUNT_DISTINCT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/approximate_aggregate_functions#approx_count_distinct

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCountDistinct_ReturnsDistinctCount()
	{
		var table = "AggApprox1";
		await SetupNumbersTable(table);
		// Val has values: 10, 20, 30, 20, NULL → 3 distinct non-null values
		var result = await Eval($"SELECT APPROX_COUNT_DISTINCT(Val) FROM {table}");
		result.Should().Be(3L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCountDistinct_AllNull_ReturnsZero()
	{
		var table = "AggApprox2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L });
		var result = await Eval($"SELECT APPROX_COUNT_DISTINCT(Val) FROM {table}");
		result.Should().Be(0L);
	}

	[Fact]
	[Trait(TestTraits.Category, "AggregateFunction")]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCountDistinct_AllSameValue_ReturnsOne()
	{
		var table = "AggApprox3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Val INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Val"] = 42L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Val"] = 42L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Val"] = 42L });
		var result = await Eval($"SELECT APPROX_COUNT_DISTINCT(Val) FROM {table}");
		result.Should().Be(1L);
	}

	// ═══════════════════════════════════════════════════════════════
	// HAVING MAX / MIN
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions#having_clause
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AnyValue_HavingMax_ReturnsValueFromMaxRow()
	{
		var table = "AggHavMax1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Score"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Score"] = 30L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Carol", ["Score"] = 20L });
		var result = await Eval($"SELECT ANY_VALUE(Name HAVING MAX Score) FROM {table}");
		result.Should().Be("Bob");
	}

	[Fact]
	public async Task AnyValue_HavingMin_ReturnsValueFromMinRow()
	{
		var table = "AggHavMin1";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Score"] = 10L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Score"] = 30L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Carol", ["Score"] = 20L });
		var result = await Eval($"SELECT ANY_VALUE(Name HAVING MIN Score) FROM {table}");
		result.Should().Be("Alice");
	}

	[Fact]
	public async Task Max_HavingMax_WorksWithGroups()
	{
		var table = "AggHavMax2";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Category STRING(MAX), Val INT64, Weight INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Category"] = "A", ["Val"] = 5L, ["Weight"] = 1L },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Category"] = "A", ["Val"] = 10L, ["Weight"] = 3L },
			new Dictionary<string, object?> { ["Id"] = 3L, ["Category"] = "A", ["Val"] = 7L, ["Weight"] = 2L });
		// MAX(Val HAVING MAX Weight) = Val from the row where Weight=3, which is 10
		var result = await Eval($"SELECT MAX(Val HAVING MAX Weight) FROM {table}");
		result.Should().Be(10L);
	}

	[Fact]
	public async Task AnyValue_HavingMax_AllNullHavingExpr_ReturnsNull()
	{
		var table = "AggHavMax3";
		await ExecuteDdlAsync($"CREATE TABLE {table} (Id INT64 NOT NULL, Name STRING(MAX), Score INT64) PRIMARY KEY (Id)");
		await InsertAsync(table,
			new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["Score"] = null },
			new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["Score"] = null });
		var result = await Eval($"SELECT ANY_VALUE(Name HAVING MAX Score) FROM {table}");
		result.Should().BeNull();
	}
}
