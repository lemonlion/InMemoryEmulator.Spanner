using FluentAssertions;
using Google.Cloud.Spanner.Data;
using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// Integration tests for exact and approximate vector distance functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#euclidean_distance
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#dot_product
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_cosine_distance
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_euclidean_distance
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_dot_product
/// </summary>
[Collection(IntegrationCollection.Name)]
public class VectorDistanceIntegrationTests : IntegrationTestBase
{
	public VectorDistanceIntegrationTests(EmulatorSession session) : base(session) { }

	// ═══════════════════════════════════════════════════════════════
	// COSINE_DISTANCE — exact
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
	// ═══════════════════════════════════════════════════════════════

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
	public async Task CosineDistance_KnownValues()
	{
		// Ref: COSINE_DISTANCE([1.0, 2.0], [3.0, 4.0]) => 0.016130...
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 2.0], [3.0, 4.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.016130, 1e-4);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_OppositeVectors_ReturnsTwo()
	{
		// Opposite vectors: cosine similarity = -1, cosine distance = 1 - (-1) = 2
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 0.0], [-1.0, 0.0])");
		Convert.ToDouble(result).Should().BeApproximately(2.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_HighDimensional()
	{
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 2.0, 3.0, 4.0], [1.0, 2.0, 3.0, 4.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_NullVector_ReturnsNull()
	{
		// Ref: "If a vector is NULL, NULL is returned."
		var result = await QueryScalarAsync(
			"SELECT COSINE_DISTANCE(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0])");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_ZeroVector_ThrowsError()
	{
		// Ref: "A vector can't be a zero vector … If a zero vector is encountered, an error is produced."
		Func<Task> act = () => QueryScalarAsync(
			"SELECT COSINE_DISTANCE([0.0, 0.0], [3.0, 4.0])");
		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_DifferentDimensions_ThrowsError()
	{
		// Ref: "Both vectors must share the same dimensions…an error is produced."
		Func<Task> act = () => QueryScalarAsync(
			"SELECT COSINE_DISTANCE([9.0, 7.0], [8.0, 4.0, 5.0])");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// EUCLIDEAN_DISTANCE — exact
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#euclidean_distance
	// ═══════════════════════════════════════════════════════════════

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
	public async Task EuclideanDistance_KnownValues_345()
	{
		// 3-4-5 right triangle
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([0.0, 0.0], [3.0, 4.0])");
		Convert.ToDouble(result).Should().BeApproximately(5.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_KnownValues_Sqrt8()
	{
		// Ref: EUCLIDEAN_DISTANCE([1.0, 2.0], [3.0, 4.0]) => 2.828...
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([1.0, 2.0], [3.0, 4.0])");
		Convert.ToDouble(result).Should().BeApproximately(2.828, 1e-2);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_ZeroVectors_ReturnsZero()
	{
		// Ref: "A vector can be a zero vector."
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([0.0, 0.0], [0.0, 0.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_NullVector_ReturnsNull()
	{
		var result = await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0])");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_DifferentDimensions_ThrowsError()
	{
		Func<Task> act = () => QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([9.0, 7.0], [8.0, 4.0, 5.0])");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// DOT_PRODUCT — exact
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#dot_product
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_KnownValues()
	{
		// [1,2,3] · [4,5,6] = 4+10+18 = 32
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([1.0, 2.0, 3.0], [4.0, 5.0, 6.0])");
		Convert.ToDouble(result).Should().BeApproximately(32.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_SingleElement()
	{
		// Ref: DOT_PRODUCT([100], [200]) => 20000
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([100.0], [200.0])");
		Convert.ToDouble(result).Should().BeApproximately(20000.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_TwoElements()
	{
		// Ref: DOT_PRODUCT([100, 10], [200, 6]) => 20060
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([100.0, 10.0], [200.0, 6.0])");
		Convert.ToDouble(result).Should().BeApproximately(20060.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_ThreeElements()
	{
		// Ref: DOT_PRODUCT([100, 10, 1], [200, 6, 2]) => 20062
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([100.0, 10.0, 1.0], [200.0, 6.0, 2.0])");
		Convert.ToDouble(result).Should().BeApproximately(20062.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_ZeroVectors_ReturnsZero()
	{
		// Ref: "A vector can be a zero vector." and DOT_PRODUCT([], []) => 0
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([0.0, 0.0], [0.0, 0.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_NullVector_ReturnsNull()
	{
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0])");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_OrthogonalVectors_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT DOT_PRODUCT([1.0, 0.0], [0.0, 1.0])");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task DotProduct_DifferentDimensions_ThrowsError()
	{
		Func<Task> act = () => QueryScalarAsync(
			"SELECT DOT_PRODUCT([1.0, 2.0], [3.0, 4.0, 5.0])");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// APPROX_COSINE_DISTANCE — approximate (exact in emulator)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_cosine_distance
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_IdenticalVectors_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE([1.0, 0.0], [1.0, 0.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_OrthogonalVectors_ReturnsOne()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE([1.0, 0.0], [0.0, 1.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(1.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_KnownValues()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE([1.0, 2.0], [3.0, 4.0], options=>JSON'{\"num_leaves_to_search\": 100}')");
		Convert.ToDouble(result).Should().BeApproximately(0.016130, 1e-4);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_NullVector_ReturnsNull()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_ZeroVector_ThrowsError()
	{
		Func<Task> act = () => QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE([0.0, 0.0], [3.0, 4.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		await act.Should().ThrowAsync<Exception>();
	}

	// ═══════════════════════════════════════════════════════════════
	// APPROX_EUCLIDEAN_DISTANCE — approximate (exact in emulator)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_euclidean_distance
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxEuclideanDistance_SamePoint_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_EUCLIDEAN_DISTANCE([1.0, 2.0], [1.0, 2.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxEuclideanDistance_KnownValues()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_EUCLIDEAN_DISTANCE([0.0, 0.0], [3.0, 4.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(5.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxEuclideanDistance_NullVector_ReturnsNull()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_EUCLIDEAN_DISTANCE(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxEuclideanDistance_ZeroVectors_ReturnsZero()
	{
		// Zero vectors are allowed for Euclidean distance
		var result = await QueryScalarAsync(
			"SELECT APPROX_EUCLIDEAN_DISTANCE([0.0, 0.0], [0.0, 0.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// APPROX_DOT_PRODUCT — approximate (exact in emulator)
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_dot_product
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxDotProduct_KnownValues()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_DOT_PRODUCT([1.0, 2.0, 3.0], [4.0, 5.0, 6.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(32.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxDotProduct_NullVector_ReturnsNull()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_DOT_PRODUCT(CAST(NULL AS ARRAY<FLOAT64>), [1.0, 2.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		result.Should().BeOneOf(null, DBNull.Value);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxDotProduct_ZeroVectors_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_DOT_PRODUCT([0.0, 0.0], [0.0, 0.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxDotProduct_OrthogonalVectors_ReturnsZero()
	{
		var result = await QueryScalarAsync(
			"SELECT APPROX_DOT_PRODUCT([1.0, 0.0], [0.0, 1.0], options=>JSON'{\"num_leaves_to_search\": 10}')");
		Convert.ToDouble(result).Should().BeApproximately(0.0, 1e-10);
	}

	// ═══════════════════════════════════════════════════════════════
	// Cross-function consistency: APPROX_* matches exact
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxCosineDistance_MatchesExact()
	{
		// In the emulator, APPROX_COSINE_DISTANCE should return the same result as COSINE_DISTANCE
		var exact = Convert.ToDouble(await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 2.0, 3.0], [4.0, 5.0, 6.0])"));
		var approx = Convert.ToDouble(await QueryScalarAsync(
			"SELECT APPROX_COSINE_DISTANCE([1.0, 2.0, 3.0], [4.0, 5.0, 6.0], options=>JSON'{\"num_leaves_to_search\": 10}')"));
		approx.Should().BeApproximately(exact, 1e-15);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxEuclideanDistance_MatchesExact()
	{
		var exact = Convert.ToDouble(await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([1.0, 2.0, 3.0], [4.0, 5.0, 6.0])"));
		var approx = Convert.ToDouble(await QueryScalarAsync(
			"SELECT APPROX_EUCLIDEAN_DISTANCE([1.0, 2.0, 3.0], [4.0, 5.0, 6.0], options=>JSON'{\"num_leaves_to_search\": 10}')"));
		approx.Should().BeApproximately(exact, 1e-15);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task ApproxDotProduct_MatchesExact()
	{
		var exact = Convert.ToDouble(await QueryScalarAsync(
			"SELECT DOT_PRODUCT([1.0, 2.0, 3.0], [4.0, 5.0, 6.0])"));
		var approx = Convert.ToDouble(await QueryScalarAsync(
			"SELECT APPROX_DOT_PRODUCT([1.0, 2.0, 3.0], [4.0, 5.0, 6.0], options=>JSON'{\"num_leaves_to_search\": 10}')"));
		approx.Should().BeApproximately(exact, 1e-15);
	}

	// ═══════════════════════════════════════════════════════════════
	// Order-independence: swapped magnitude order produces same result
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
	//   "The ordering of numeric values in a vector doesn't impact the results"
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task CosineDistance_SwappedMagnitudes_SameResult()
	{
		var r1 = Convert.ToDouble(await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([1.0, 2.0], [3.0, 4.0])"));
		var r2 = Convert.ToDouble(await QueryScalarAsync(
			"SELECT COSINE_DISTANCE([2.0, 1.0], [4.0, 3.0])"));
		r1.Should().BeApproximately(r2, 1e-15);
	}

	[Fact]
	[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
	public async Task EuclideanDistance_SwappedMagnitudes_SameResult()
	{
		var r1 = Convert.ToDouble(await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([1.0, 2.0], [3.0, 4.0])"));
		var r2 = Convert.ToDouble(await QueryScalarAsync(
			"SELECT EUCLIDEAN_DISTANCE([2.0, 1.0], [4.0, 3.0])"));
		r1.Should().BeApproximately(r2, 1e-15);
	}
}
