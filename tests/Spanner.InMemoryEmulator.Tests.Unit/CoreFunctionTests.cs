using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

/// <summary>
/// Unit tests for core SQL functions (Phase 12).
/// </summary>
public class CoreFunctionTests
{
	private static InMemorySpannerDatabase CreateDb()
	{
		var db = new InMemorySpannerDatabase();
		db.ExecuteDdl("CREATE TABLE T (Id INT64 NOT NULL, S STRING(MAX), N INT64, F FLOAT64, B BOOL, J STRING(MAX)) PRIMARY KEY (Id)");
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 1L, ["S"] = "Hello World", ["N"] = 42L, ["F"] = 3.14, ["B"] = true, ["J"] = "{\"name\":\"Alice\",\"age\":30}" });
		db.Insert("T", new Dictionary<string, object?> { ["Id"] = 2L, ["S"] = null, ["N"] = null, ["F"] = null, ["B"] = false, ["J"] = null });
		return db;
	}

	// ─── STRING FUNCTIONS ───

	[Fact] public void Concat() { using var db = CreateDb(); db.ExecuteQuery("SELECT CONCAT('a', 'b', 'c') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("abc"); }
	[Fact] public void Length() { using var db = CreateDb(); db.ExecuteQuery("SELECT LENGTH('hello') AS R FROM T WHERE Id=1")[0]["R"].Should().Be(5L); }
	[Fact] public void Lower() { using var db = CreateDb(); db.ExecuteQuery("SELECT LOWER('HELLO') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hello"); }
	[Fact] public void Upper() { using var db = CreateDb(); db.ExecuteQuery("SELECT UPPER('hello') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("HELLO"); }
	[Fact] public void Trim() { using var db = CreateDb(); db.ExecuteQuery("SELECT TRIM('  hi  ') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hi"); }
	[Fact] public void Ltrim() { using var db = CreateDb(); db.ExecuteQuery("SELECT LTRIM('  hi') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hi"); }
	[Fact] public void Rtrim() { using var db = CreateDb(); db.ExecuteQuery("SELECT RTRIM('hi  ') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hi"); }
	[Fact] public void StartsWith() { using var db = CreateDb(); db.ExecuteQuery("SELECT STARTS_WITH('hello', 'hel') AS R FROM T WHERE Id=1")[0]["R"].Should().Be(true); }
	[Fact] public void EndsWith() { using var db = CreateDb(); db.ExecuteQuery("SELECT ENDS_WITH('hello', 'llo') AS R FROM T WHERE Id=1")[0]["R"].Should().Be(true); }
	[Fact] public void Substr() { using var db = CreateDb(); db.ExecuteQuery("SELECT SUBSTR('hello', 2, 3) AS R FROM T WHERE Id=1")[0]["R"].Should().Be("ell"); }
	[Fact] public void Replace() { using var db = CreateDb(); db.ExecuteQuery("SELECT REPLACE('hello', 'ell', 'ELL') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hELLo"); }
	[Fact] public void Reverse() { using var db = CreateDb(); db.ExecuteQuery("SELECT REVERSE('abc') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("cba"); }
	[Fact] public void Strpos() { using var db = CreateDb(); db.ExecuteQuery("SELECT STRPOS('hello world', 'world') AS R FROM T WHERE Id=1")[0]["R"].Should().Be(7L); }
	[Fact] public void Repeat_() { using var db = CreateDb(); db.ExecuteQuery("SELECT REPEAT('ab', 3) AS R FROM T WHERE Id=1")[0]["R"].Should().Be("ababab"); }
	[Fact] public void Lpad() { using var db = CreateDb(); db.ExecuteQuery("SELECT LPAD('hi', 5, '0') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("000hi"); }
	[Fact] public void Rpad() { using var db = CreateDb(); db.ExecuteQuery("SELECT RPAD('hi', 5, '0') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("hi000"); }
	[Fact] public void RegexpContains() { using var db = CreateDb(); db.ExecuteQuery("SELECT REGEXP_CONTAINS('hello123', '[0-9]+') AS R FROM T WHERE Id=1")[0]["R"].Should().Be(true); }
	[Fact] public void RegexpExtract() { using var db = CreateDb(); db.ExecuteQuery("SELECT REGEXP_EXTRACT('hello123', '([0-9]+)') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("123"); }
	[Fact] public void RegexpExtract_WithPosition_Throws() { using var db = CreateDb(); var act = () => db.ExecuteQuery("SELECT REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 8) AS R FROM T WHERE Id=1"); act.Should().Throw<InvalidOperationException>(); }
	[Fact] public void RegexpExtract_WithPositionAndOccurrence_Throws() { using var db = CreateDb(); var act = () => db.ExecuteQuery("SELECT REGEXP_EXTRACT('abc 123 def 456', '[0-9]+', 1, 2) AS R FROM T WHERE Id=1"); act.Should().Throw<InvalidOperationException>(); }
	[Fact] public void RegexpReplace() { using var db = CreateDb(); db.ExecuteQuery("SELECT REGEXP_REPLACE('hello123', '[0-9]+', 'X') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("helloX"); }

	[Fact]
	public void String_NullPropagation()
	{
		using var db = CreateDb();
		var rows = db.ExecuteQuery("SELECT LENGTH(S) AS R FROM T WHERE Id=2");
		rows[0]["R"].Should().BeNull();
	}

	// ─── MATH FUNCTIONS ───

	[Fact] public void Abs() { using var db = CreateDb(); db.ExecuteQuery("SELECT ABS(-5) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(5L); }
	[Fact] public void Mod() { using var db = CreateDb(); db.ExecuteQuery("SELECT MOD(10, 3) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(1L); }
	[Fact] public void Ceil() { using var db = CreateDb(); db.ExecuteQuery("SELECT CEIL(3.2) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(4.0); }
	[Fact] public void Floor() { using var db = CreateDb(); db.ExecuteQuery("SELECT FLOOR(3.8) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3.0); }
	[Fact] public void Round() { using var db = CreateDb(); db.ExecuteQuery("SELECT ROUND(3.456, 2) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3.46); }
	[Fact] public void Trunc_() { using var db = CreateDb(); db.ExecuteQuery("SELECT TRUNC(3.9) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3.0); }
	[Fact] public void Sign() { using var db = CreateDb(); db.ExecuteQuery("SELECT SIGN(-5) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(-1L); }
	[Fact] public void Greatest() { using var db = CreateDb(); db.ExecuteQuery("SELECT GREATEST(3, 7, 1) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(7L); }
	[Fact] public void Least() { using var db = CreateDb(); db.ExecuteQuery("SELECT LEAST(3, 7, 1) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(1L); }
	[Fact] public void Div() { using var db = CreateDb(); db.ExecuteQuery("SELECT DIV(7, 2) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3L); }
	[Fact] public void IeeeDivide_ByZero() { using var db = CreateDb(); var r = db.ExecuteQuery("SELECT IEEE_DIVIDE(1.0, 0.0) AS R FROM T WHERE Id=1")[0]["R"]; double.IsInfinity(Convert.ToDouble(r)).Should().BeTrue(); }
	[Fact] public void SafeDivide_ByZero() { using var db = CreateDb(); db.ExecuteQuery("SELECT SAFE_DIVIDE(1, 0) AS R FROM T WHERE Id=1")[0]["R"].Should().BeNull(); }
	[Fact] public void Sqrt() { using var db = CreateDb(); db.ExecuteQuery("SELECT SQRT(9.0) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3.0); }
	[Fact] public void Pow() { using var db = CreateDb(); db.ExecuteQuery("SELECT POW(2.0, 3.0) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(8.0); }

	[Fact]
	public void Math_NullPropagation()
	{
		using var db = CreateDb();
		db.ExecuteQuery("SELECT ABS(N) AS R FROM T WHERE Id=2")[0]["R"].Should().BeNull();
	}

	// ─── CONDITIONAL FUNCTIONS ───

	[Fact] public void Coalesce() { using var db = CreateDb(); db.ExecuteQuery("SELECT COALESCE(NULL, NULL, 'a') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("a"); }
	[Fact] public void Ifnull() { using var db = CreateDb(); db.ExecuteQuery("SELECT IFNULL(NULL, 'default') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("default"); }
	[Fact] public void Nullif_Same() { using var db = CreateDb(); db.ExecuteQuery("SELECT NULLIF(1, 1) AS R FROM T WHERE Id=1")[0]["R"].Should().BeNull(); }
	[Fact] public void Nullif_Different() { using var db = CreateDb(); db.ExecuteQuery("SELECT NULLIF(1, 2) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(1L); }
	[Fact] public void If_True() { using var db = CreateDb(); db.ExecuteQuery("SELECT IF(true, 'yes', 'no') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("yes"); }
	[Fact] public void If_False() { using var db = CreateDb(); db.ExecuteQuery("SELECT IF(false, 'yes', 'no') AS R FROM T WHERE Id=1")[0]["R"].Should().Be("no"); }

	// ─── DATE/TIME FUNCTIONS ───

	[Fact]
	public void CurrentTimestamp_ReturnsDateTime()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT CURRENT_TIMESTAMP() AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().BeOfType<DateTime>();
	}

	[Fact]
	public void CurrentDate_ReturnsDate()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT CURRENT_DATE() AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().BeOfType<DateTime>();
		((DateTime)r!).TimeOfDay.Should().Be(TimeSpan.Zero);
	}

	// ─── CAST ───

	[Fact] public void Cast_IntToString() { using var db = CreateDb(); db.ExecuteQuery("SELECT CAST(42 AS STRING) AS R FROM T WHERE Id=1")[0]["R"].Should().Be("42"); }
	[Fact] public void Cast_StringToInt() { using var db = CreateDb(); db.ExecuteQuery("SELECT CAST('123' AS INT64) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(123L); }
	[Fact] public void Cast_StringToFloat() { using var db = CreateDb(); db.ExecuteQuery("SELECT CAST('3.14' AS FLOAT64) AS R FROM T WHERE Id=1")[0]["R"].Should().Be(3.14); }
	[Fact] public void Cast_Null() { using var db = CreateDb(); db.ExecuteQuery("SELECT CAST(NULL AS INT64) AS R FROM T WHERE Id=1")[0]["R"].Should().BeNull(); }

	// ─── ARRAY FUNCTIONS ───

	[Fact]
	public void GenerateArray()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT GENERATE_ARRAY(1, 5) AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().BeOfType<List<object?>>().Which.Should().HaveCount(5);
	}

	[Fact]
	public void ArrayLength()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT ARRAY_LENGTH(GENERATE_ARRAY(1, 3)) AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().Be(3L);
	}

	// ─── JSON FUNCTIONS ───

	[Fact]
	public void JsonValue()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT JSON_VALUE(J, '$.name') AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().Be("Alice");
	}

	[Fact]
	public void JsonType()
	{
		using var db = CreateDb();
		var r = db.ExecuteQuery("SELECT JSON_TYPE(J) AS R FROM T WHERE Id=1")[0]["R"];
		r.Should().Be("object");
	}

	[Fact]
	public void Json_NullPropagation()
	{
		using var db = CreateDb();
		db.ExecuteQuery("SELECT JSON_VALUE(J, '$.name') AS R FROM T WHERE Id=2")[0]["R"].Should().BeNull();
	}
}
