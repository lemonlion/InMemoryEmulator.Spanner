using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for the TABLESAMPLE operator.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
///   TABLESAMPLE samples a percentage or fixed number of rows from the input.
///   BERNOULLI: each row independently selected with given probability.
///   RESERVOIR: exactly K rows chosen uniformly at random.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TableSampleIntegrationTests : IntegrationTestBase
{
	public TableSampleIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateAndSeedTable()
	{
		var table = $"TS_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V STRING(10)) PRIMARY KEY (K)");
		var rows = Enumerable.Range(1, 100).Select(i =>
			new Dictionary<string, object?> { { "K", (long)i }, { "V", $"row{i}" } }).ToArray();
		await InsertAsync(table, rows);
		return table;
	}

	// ═══════════════════════════════════════════════════════════════
	// BERNOULLI
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
	//   "BERNOULLI — Each row is independently selected with the given
	//    probability. The actual number of rows returned can vary."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Bernoulli_100Percent_ReturnsAllRows()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE BERNOULLI (100 PERCENT)");
		rows.Should().HaveCount(100);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Bernoulli_0Percent_ReturnsNoRows()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE BERNOULLI (0 PERCENT)");
		rows.Should().HaveCount(0);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Bernoulli_50Percent_ReturnsSomeRows()
	{
		// With 100 rows and 50% probability, the result should be between 10 and 90 rows
		// with overwhelming probability (> 99.99%).
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE BERNOULLI (50 PERCENT)");
		rows.Count.Should().BeInRange(10, 90);
	}

	// ═══════════════════════════════════════════════════════════════
	// RESERVOIR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
	//   "RESERVOIR — Selects a fixed number of rows from the input."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Reservoir_10Rows_ReturnsExactly10()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE RESERVOIR (10 ROWS)");
		rows.Should().HaveCount(10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Reservoir_MoreThanAvailable_ReturnsAllRows()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE RESERVOIR (200 ROWS)");
		rows.Should().HaveCount(100);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Reservoir_0Rows_ReturnsEmpty()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE RESERVOIR (0 ROWS)");
		rows.Should().HaveCount(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// WHERE clause with TABLESAMPLE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#tablesample_operator
	//   "TABLESAMPLE is applied before any WHERE clauses."
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Reservoir_WithWhereClause_FilterAppliedAfterSample()
	{
		var table = await CreateAndSeedTable();
		// Sample 100 rows (all), then filter; result should match the WHERE condition
		var rows = await QueryAsync(
			$"SELECT K FROM {table} TABLESAMPLE RESERVOIR (100 ROWS) WHERE K <= 50");
		rows.Should().HaveCount(50);
	}
}
