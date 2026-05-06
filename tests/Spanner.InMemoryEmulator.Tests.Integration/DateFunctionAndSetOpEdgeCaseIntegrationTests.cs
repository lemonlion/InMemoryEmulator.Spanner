using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for DATE_ADD/DATE_SUB with NULL amount propagation, and bare UNION (without ALL/DISTINCT).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DateFunctionAndSetOpEdgeCaseIntegrationTests : IntegrationTestBase
{
	public DateFunctionAndSetOpEdgeCaseIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	private async Task<List<Dictionary<string, object?>>> Q(string sql)
		=> await QueryAsync(sql);

	// ─── DATE_ADD with NULL amount → NULL ───
	// Ref: Standard SQL NULL propagation.

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateAdd_NullAmount_ReturnsNull()
	{
		var result = await Eval("DATE_ADD(DATE '2024-01-01', INTERVAL CAST(NULL AS INT64) DAY)");
		result.Should().BeNull();
	}

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateSub_NullAmount_ReturnsNull()
	{
		var result = await Eval("DATE_SUB(DATE '2024-01-01', INTERVAL CAST(NULL AS INT64) DAY)");
		result.Should().BeNull();
	}

	// ─── Bare UNION (without ALL/DISTINCT) defaults to UNION DISTINCT ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#set_operators
	//   "UNION: The result is the set of rows that are in either of the two query expressions.
	//    The default behavior is DISTINCT."

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task Union_Bare_DefaultsToDistinct()
	{
		var rows = await Q("SELECT 1 AS x UNION SELECT 1 AS x");
		rows.Should().HaveCount(1);
		Convert.ToInt64(rows[0]["x"]).Should().Be(1);
	}

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task Union_Bare_CombinesDifferentRows()
	{
		var rows = await Q("SELECT 1 AS x UNION SELECT 2 AS x");
		rows.Should().HaveCount(2);
	}

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task Except_Bare_DefaultsToDistinct()
	{
		var rows = await Q("SELECT 1 AS x UNION ALL SELECT 1 AS x EXCEPT SELECT 1 AS x");
		rows.Should().HaveCount(0);
	}

	[Fact]
	[Trait(TestTraits.Category, "Query")]
	public async Task Intersect_Bare_DefaultsToDistinct()
	{
		var rows = await Q("SELECT 1 AS x UNION ALL SELECT 2 AS x INTERSECT SELECT 1 AS x");
		// INTERSECT has higher precedence: (SELECT 2) INTERSECT (SELECT 1) = empty
		// Then UNION ALL empty = SELECT 1
		rows.Should().HaveCount(1);
		Convert.ToInt64(rows[0]["x"]).Should().Be(1);
	}

	// ─── DATE_ADD/DATE_SUB with WEEK and QUARTER intervals ───
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateAdd_Week_Adds7Days()
	{
		var result = await Eval("DATE_ADD(DATE '2024-01-01', INTERVAL 2 WEEK)");
		result.Should().Be(new DateTime(2024, 1, 15));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateSub_Week_Subtracts7Days()
	{
		var result = await Eval("DATE_SUB(DATE '2024-01-15', INTERVAL 1 WEEK)");
		result.Should().Be(new DateTime(2024, 1, 8));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateAdd_Quarter_Adds3Months()
	{
		var result = await Eval("DATE_ADD(DATE '2024-01-01', INTERVAL 1 QUARTER)");
		result.Should().Be(new DateTime(2024, 4, 1));
	}

	[Fact]
	[Trait(TestTraits.Category, "DateFunction")]
	public async Task DateSub_Quarter_Subtracts3Months()
	{
		var result = await Eval("DATE_SUB(DATE '2024-07-01', INTERVAL 2 QUARTER)");
		result.Should().Be(new DateTime(2024, 1, 1));
	}
}
