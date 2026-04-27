using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for SQL window/numbering functions.
/// These functions (ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST)
/// are standard SQL analytic functions supported by the Go emulator (via ZetaSQL)
/// but NOT by real Google Cloud Spanner (which only supports IS_FIRST).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WindowFunctionIntegrationTests : IntegrationTestBase
{
	public WindowFunctionIntegrationTests(EmulatorSession session) : base(session) { }

	private async Task<string> CreateAndSeedTable()
	{
		var table = $"Win_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, Cat STRING(10), V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "Cat", "A" }, { "V", 30L } },
			new Dictionary<string, object?> { { "K", 2L }, { "Cat", "A" }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 3L }, { "Cat", "A" }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 4L }, { "Cat", "B" }, { "V", 5L } },
			new Dictionary<string, object?> { { "K", 5L }, { "Cat", "B" }, { "V", 15L } },
			new Dictionary<string, object?> { { "K", 6L }, { "Cat", "B" }, { "V", 25L } });
		return table;
	}

	// ═══════════════════════════════════════════════════════════════
	// ROW_NUMBER
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#row_number
	//   ROW_NUMBER() returns a sequential integer starting at 1 for each row
	//   within its partition. The order is determined by the ORDER BY clause.
	//   Note: GCP Spanner does not support ROW_NUMBER — this is for Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RowNumber_WithOrderBy_ReturnsSequentialNumbers()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K, ROW_NUMBER() OVER (ORDER BY V ASC) AS RN FROM {table} ORDER BY K");
		rows.Should().HaveCount(6);
		// V=5(K=4), V=10(K=2), V=15(K=5), V=20(K=3), V=25(K=6), V=30(K=1)
		rows[0]["RN"].Should().Be(6L); // K=1, V=30, row 6
		rows[1]["RN"].Should().Be(2L); // K=2, V=10, row 2
		rows[2]["RN"].Should().Be(4L); // K=3, V=20, row 4
		rows[3]["RN"].Should().Be(1L); // K=4, V=5, row 1
		rows[4]["RN"].Should().Be(3L); // K=5, V=15, row 3
		rows[5]["RN"].Should().Be(5L); // K=6, V=25, row 5
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task RowNumber_WithPartitionBy_RestartsPerPartition()
	{
		var table = await CreateAndSeedTable();
		var rows = await QueryAsync(
			$"SELECT K, ROW_NUMBER() OVER (PARTITION BY Cat ORDER BY V ASC) AS RN FROM {table} ORDER BY K");
		rows.Should().HaveCount(6);
		// Partition A: V=10(K=2)→1, V=20(K=3)→2, V=30(K=1)→3
		// Partition B: V=5(K=4)→1, V=15(K=5)→2, V=25(K=6)→3
		rows[0]["RN"].Should().Be(3L); // K=1, Cat=A, V=30, row 3
		rows[1]["RN"].Should().Be(1L); // K=2, Cat=A, V=10, row 1
		rows[2]["RN"].Should().Be(2L); // K=3, Cat=A, V=20, row 2
		rows[3]["RN"].Should().Be(1L); // K=4, Cat=B, V=5, row 1
		rows[4]["RN"].Should().Be(2L); // K=5, Cat=B, V=15, row 2
		rows[5]["RN"].Should().Be(3L); // K=6, Cat=B, V=25, row 3
	}

	// ═══════════════════════════════════════════════════════════════
	// RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#rank
	//   RANK() returns the rank of the current row. Rows with equal ORDER BY
	//   values receive the same rank. The next rank skips (has gaps).
	//   Note: GCP Spanner does not support RANK — this is for Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Rank_WithTies_ProducesGaps()
	{
		var table = $"Rank_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 2L }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 3L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 4L }, { "V", 30L } });

		var rows = await QueryAsync(
			$"SELECT K, RANK() OVER (ORDER BY V ASC) AS R FROM {table} ORDER BY K");
		rows.Should().HaveCount(4);
		// V=10(K=1,K=3)→rank 1, V=20(K=2)→rank 3, V=30(K=4)→rank 4
		rows[0]["R"].Should().Be(1L); // K=1, V=10, rank 1
		rows[1]["R"].Should().Be(3L); // K=2, V=20, rank 3 (gap because two rows had rank 1)
		rows[2]["R"].Should().Be(1L); // K=3, V=10, rank 1
		rows[3]["R"].Should().Be(4L); // K=4, V=30, rank 4
	}

	// ═══════════════════════════════════════════════════════════════
	// DENSE_RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#dense_rank
	//   DENSE_RANK() is similar to RANK() but without gaps. Tied rows get the
	//   same rank, but the next distinct value gets rank+1 (no gap).
	//   Note: GCP Spanner does not support DENSE_RANK — this is for Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DenseRank_WithTies_NoGaps()
	{
		var table = $"DenseR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 2L }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 3L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 4L }, { "V", 30L } });

		var rows = await QueryAsync(
			$"SELECT K, DENSE_RANK() OVER (ORDER BY V ASC) AS DR FROM {table} ORDER BY K");
		rows.Should().HaveCount(4);
		// V=10(K=1,K=3)→rank 1, V=20(K=2)→rank 2, V=30(K=4)→rank 3
		rows[0]["DR"].Should().Be(1L); // K=1, V=10, dense_rank 1
		rows[1]["DR"].Should().Be(2L); // K=2, V=20, dense_rank 2
		rows[2]["DR"].Should().Be(1L); // K=3, V=10, dense_rank 1
		rows[3]["DR"].Should().Be(3L); // K=4, V=30, dense_rank 3
	}

	// ═══════════════════════════════════════════════════════════════
	// NTILE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#ntile
	//   NTILE(num_buckets) divides the rows in each partition into num_buckets
	//   roughly-equal groups and assigns a bucket number (1-based) to each row.
	//   Note: GCP Spanner does not support NTILE — this is for Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Ntile_DividesPartitionIntoBuckets()
	{
		var table = await CreateAndSeedTable();
		// 6 rows, NTILE(3) → 2 rows per bucket
		var rows = await QueryAsync(
			$"SELECT K, NTILE(3) OVER (ORDER BY V ASC) AS Bucket FROM {table} ORDER BY V ASC");
		rows.Should().HaveCount(6);
		rows[0]["Bucket"].Should().Be(1L); // V=5, bucket 1
		rows[1]["Bucket"].Should().Be(1L); // V=10, bucket 1
		rows[2]["Bucket"].Should().Be(2L); // V=15, bucket 2
		rows[3]["Bucket"].Should().Be(2L); // V=20, bucket 2
		rows[4]["Bucket"].Should().Be(3L); // V=25, bucket 3
		rows[5]["Bucket"].Should().Be(3L); // V=30, bucket 3
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task Ntile_UnevenDistribution_ExtraRowsGoToEarlierBuckets()
	{
		// 5 rows, NTILE(3) → buckets get 2, 2, 1 rows
		var table = $"Ntile2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 2L }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 3L }, { "V", 30L } },
			new Dictionary<string, object?> { { "K", 4L }, { "V", 40L } },
			new Dictionary<string, object?> { { "K", 5L }, { "V", 50L } });

		var rows = await QueryAsync(
			$"SELECT K, NTILE(3) OVER (ORDER BY V ASC) AS Bucket FROM {table} ORDER BY V ASC");
		rows.Should().HaveCount(5);
		rows[0]["Bucket"].Should().Be(1L); // V=10, bucket 1
		rows[1]["Bucket"].Should().Be(1L); // V=20, bucket 1
		rows[2]["Bucket"].Should().Be(2L); // V=30, bucket 2
		rows[3]["Bucket"].Should().Be(2L); // V=40, bucket 2
		rows[4]["Bucket"].Should().Be(3L); // V=50, bucket 3
	}

	// ═══════════════════════════════════════════════════════════════
	// PERCENT_RANK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#percent_rank
	//   PERCENT_RANK() = (rank - 1) / (partition_size - 1).
	//   Returns 0 for the first row and for single-row partitions.
	//   Note: GCP Spanner does not support PERCENT_RANK — Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task PercentRank_ReturnsExpectedValues()
	{
		var table = $"PCR_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 2L }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 3L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 4L }, { "V", 30L } });

		var rows = await QueryAsync(
			$"SELECT K, PERCENT_RANK() OVER (ORDER BY V ASC) AS PR FROM {table} ORDER BY K");
		rows.Should().HaveCount(4);
		// rank of V=10 is 1, rank of V=20 is 3, rank of V=30 is 4
		// partition_size = 4
		// PERCENT_RANK = (rank - 1) / (4 - 1) = (rank - 1) / 3
		((double)rows[0]["PR"]!).Should().BeApproximately(0.0, 0.0001);          // K=1, rank=1 → 0/3
		((double)rows[1]["PR"]!).Should().BeApproximately(2.0 / 3.0, 0.0001);    // K=2, rank=3 → 2/3
		((double)rows[2]["PR"]!).Should().BeApproximately(0.0, 0.0001);          // K=3, rank=1 → 0/3
		((double)rows[3]["PR"]!).Should().BeApproximately(1.0, 0.0001);          // K=4, rank=4 → 3/3
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task PercentRank_SingleRow_ReturnsZero()
	{
		var table = $"PCR2_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } });

		var rows = await QueryAsync(
			$"SELECT K, PERCENT_RANK() OVER (ORDER BY V ASC) AS PR FROM {table}");
		rows.Should().HaveCount(1);
		((double)rows[0]["PR"]!).Should().Be(0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CUME_DIST
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions#cume_dist
	//   CUME_DIST() = count(rows with value <= current) / partition_size.
	//   Note: GCP Spanner does not support CUME_DIST — Go emulator parity.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CumeDist_ReturnsExpectedValues()
	{
		var table = $"CD_{Guid.NewGuid():N}";
		await ExecuteDdlAsync(
			$"CREATE TABLE {table} (K INT64 NOT NULL, V INT64) PRIMARY KEY (K)");
		await InsertAsync(table,
			new Dictionary<string, object?> { { "K", 1L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 2L }, { "V", 20L } },
			new Dictionary<string, object?> { { "K", 3L }, { "V", 10L } },
			new Dictionary<string, object?> { { "K", 4L }, { "V", 30L } });

		var rows = await QueryAsync(
			$"SELECT K, CUME_DIST() OVER (ORDER BY V ASC) AS CD FROM {table} ORDER BY K");
		rows.Should().HaveCount(4);
		// V=10 appears at positions 1,2 → cume_dist = 2/4 = 0.5
		// V=20 appears at position 3 → cume_dist = 3/4 = 0.75
		// V=30 appears at position 4 → cume_dist = 4/4 = 1.0
		((double)rows[0]["CD"]!).Should().BeApproximately(0.5, 0.0001);   // K=1, V=10 → 2/4
		((double)rows[1]["CD"]!).Should().BeApproximately(0.75, 0.0001);  // K=2, V=20 → 3/4
		((double)rows[2]["CD"]!).Should().BeApproximately(0.5, 0.0001);   // K=3, V=10 → 2/4
		((double)rows[3]["CD"]!).Should().BeApproximately(1.0, 0.0001);   // K=4, V=30 → 4/4
	}
}
