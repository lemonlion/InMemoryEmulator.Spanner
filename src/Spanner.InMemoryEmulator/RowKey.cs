namespace Spanner.InMemoryEmulator;

/// <summary>
/// Composite primary key for a Spanner row. Supports structural equality and lexicographic comparison
/// matching Spanner's sort order.
/// </summary>
public class RowKey : IEquatable<RowKey>, IComparable<RowKey>
{
	public object?[] Values { get; }

	public RowKey(object?[] values)
	{
		Values = values ?? throw new ArgumentNullException(nameof(values));
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#comparison_operators
	//   "NULL sorts first (smallest)."
	//   Key comparison is lexicographic on PK column values in declaration order.
	public int CompareTo(RowKey? other)
	{
		if (other is null) return 1;

		var len = Math.Min(Values.Length, other.Values.Length);
		for (var i = 0; i < len; i++)
		{
			var cmp = CompareValues(Values[i], other.Values[i]);
			if (cmp != 0) return cmp;
		}

		return Values.Length.CompareTo(other.Values.Length);
	}

	public bool Equals(RowKey? other)
	{
		if (other is null) return false;
		if (Values.Length != other.Values.Length) return false;

		for (var i = 0; i < Values.Length; i++)
		{
			if (!Equals(Values[i], other.Values[i])) return false;
		}

		return true;
	}

	public override bool Equals(object? obj) => Equals(obj as RowKey);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var value in Values)
		{
			hash.Add(value);
		}
		return hash.ToHashCode();
	}

	private static int CompareValues(object? a, object? b)
	{
		if (a is null && b is null) return 0;
		if (a is null) return -1;
		if (b is null) return 1;

		return a switch
		{
			bool ba when b is bool bb => ba.CompareTo(bb),
			long la when b is long lb => la.CompareTo(lb),
			double da when b is double db => da.CompareTo(db),
			float fa when b is float fb => fa.CompareTo(fb),
			string sa when b is string sb => string.Compare(sa, sb, StringComparison.Ordinal),
			DateTime dta when b is DateTime dtb => dta.CompareTo(dtb),
			DateTimeOffset dtoa when b is DateTimeOffset dtob => dtoa.CompareTo(dtob),
			byte[] bya when b is byte[] byb => CompareBytes(bya, byb),
			IComparable ca => ca.CompareTo(b),
			_ => throw new InvalidOperationException($"Cannot compare values of type {a.GetType().Name}")
		};
	}

	private static int CompareBytes(byte[] a, byte[] b)
	{
		var len = Math.Min(a.Length, b.Length);
		for (var i = 0; i < len; i++)
		{
			var cmp = a[i].CompareTo(b[i]);
			if (cmp != 0) return cmp;
		}
		return a.Length.CompareTo(b.Length);
	}
}
