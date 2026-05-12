using FluentAssertions;

namespace InMemoryEmulator.Spanner.Tests.Unit;

public class RowKeyTests
{
	[Fact]
	public void Equals_SameValues_ReturnsTrue()
	{
		// Arrange
		var key1 = new RowKey(new object?[] { 1L, "abc" });
		var key2 = new RowKey(new object?[] { 1L, "abc" });

		// Assert
		key1.Equals(key2).Should().BeTrue();
		key1.GetHashCode().Should().Be(key2.GetHashCode());
	}

	[Fact]
	public void Equals_DifferentValues_ReturnsFalse()
	{
		// Arrange
		var key1 = new RowKey(new object?[] { 1L, "abc" });
		var key2 = new RowKey(new object?[] { 2L, "abc" });

		// Assert
		key1.Equals(key2).Should().BeFalse();
	}

	[Fact]
	public void CompareTo_NullSortsFirst()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#comparison_operators
		//   "NULL sorts first (smallest)."
		var key1 = new RowKey(new object?[] { null });
		var key2 = new RowKey(new object?[] { 1L });

		key1.CompareTo(key2).Should().BeNegative();
	}

	[Fact]
	public void CompareTo_LexicographicOrder()
	{
		var key1 = new RowKey(new object?[] { 1L, "a" });
		var key2 = new RowKey(new object?[] { 1L, "b" });

		key1.CompareTo(key2).Should().BeNegative();
	}
}
