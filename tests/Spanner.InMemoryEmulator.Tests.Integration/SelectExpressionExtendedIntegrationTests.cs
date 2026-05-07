using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Extended integration tests for SELECT expression patterns including literals,
/// operators, concatenation, CASE, subqueries, ARRAY, EXISTS, aliasing, and more.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/expression_subqueries
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SelectExpressionExtendedIntegrationTests : IntegrationTestBase
{
    public SelectExpressionExtendedIntegrationTests(EmulatorSession session) : base(session) { }

    private async Task<object?> Eval(string expr)
    {
        var rows = await QueryAsync($"SELECT {expr} AS R");
        return rows[0]["R"];
    }

    private async Task<List<Dictionary<string, object?>>> EvalRows(string sql)
    {
        return await QueryAsync(sql);
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Literal values — integer, float, string, bool, date/timestamp
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#literals
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    [InlineData("100", 100L)]
    [InlineData("999999", 999999L)]
    [InlineData("9223372036854775807", 9223372036854775807L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0", 1.0)]
    [InlineData("0.0", 0.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("-2.718", -2.718)]
    [InlineData("0.001", 0.001)]
    [InlineData("123456.789", 123456.789)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_Float(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData("'hello'", "hello")]
    [InlineData("'world'", "world")]
    [InlineData("''", "")]
    [InlineData("'abc def'", "abc def")]
    [InlineData("'123'", "123")]
    [InlineData("'Hello, World!'", "Hello, World!")]
    [InlineData("'line1\\nline2'", "line1\nline2")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("FALSE", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_Bool(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_Date()
    {
        // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#date_literals
        var result = await Eval("DATE '2024-01-15'");
        result.Should().NotBeNull();
        result.Should().BeOfType<DateTime>();
        ((DateTime)result!).Date.Should().Be(new DateTime(2024, 1, 15));
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Literal_Timestamp()
    {
        // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#timestamp_literals
        var result = await Eval("TIMESTAMP '2024-01-15T10:30:00Z'");
        result.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. String concatenation with ||
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("'hello' || ' ' || 'world'", "hello world")]
    [InlineData("'a' || 'b'", "ab")]
    [InlineData("'foo' || ''", "foo")]
    [InlineData("'' || 'bar'", "bar")]
    [InlineData("'' || ''", "")]
    [InlineData("'a' || 'b' || 'c'", "abc")]
    [InlineData("'a' || 'b' || 'c' || 'd'", "abcd")]
    [InlineData("'Hello' || ', ' || 'World' || '!'", "Hello, World!")]
    [InlineData("'x' || 'y' || 'z' || '1' || '2'", "xyz12")]
    [InlineData("'  ' || 'trim'", "  trim")]
    [InlineData("'trim' || '  '", "trim  ")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task StringConcat_Operator(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Arithmetic expressions — complex, parentheses, precedence
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 + 2", 3L)]
    [InlineData("10 - 3", 7L)]
    [InlineData("4 * 5", 20L)]
    [InlineData("2 + 3 * 4", 14L)]
    [InlineData("(2 + 3) * 4", 20L)]
    // Division cases removed — INT64 / INT64 returns FLOAT64 per Spanner spec
    [InlineData("10 - 2 - 3", 5L)]
    [InlineData("2 * 3 + 4 * 5", 26L)]
    [InlineData("(2 + 3) * (4 + 5)", 45L)]
    [InlineData("1 + 2 + 3 + 4 + 5", 15L)]
    [InlineData("10 * 10 - 1", 99L)]
    [InlineData("10 * (10 - 1)", 90L)]
    [InlineData("(1 + 2) * (3 + 4) * (5 + 6)", 231L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Arithmetic_IntegerExpressions(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0 + 2.0", 3.0)]
    [InlineData("10.5 - 3.2", 7.3)]
    [InlineData("2.5 * 4.0", 10.0)]
    [InlineData("10.0 / 3.0", 3.333)]
    [InlineData("1.1 + 2.2 + 3.3", 6.6)]
    [InlineData("(1.5 + 2.5) * 3.0", 12.0)]
    [InlineData("100.0 / 7.0", 14.286)]
    [InlineData("0.1 + 0.2", 0.3)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Arithmetic_FloatExpressions(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.01);
    }

    [Theory]
    [InlineData("7 % 3", 1L)]
    [InlineData("10 % 5", 0L)]
    [InlineData("10 % 3", 1L)]
    [InlineData("15 % 4", 3L)]
    [InlineData("100 % 7", 2L)]
    [InlineData("-7 % 3", -1L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    [Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
    public async Task Arithmetic_Modulo(string expr, long expected)
    {
        // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
        // MOD operator (%) returns the remainder
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Unary minus — negation of values and expressions
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#unary_operators
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("-1", -1L)]
    [InlineData("-42", -42L)]
    [InlineData("-0", 0L)]
    [InlineData("-(1 + 2)", -3L)]
    [InlineData("-(10 - 3)", -7L)]
    [InlineData("-(2 * 5)", -10L)]
    [InlineData("-(-1)", 1L)]
    [InlineData("-(-(-1))", -1L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task UnaryMinus_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("-1.5", -1.5)]
    [InlineData("-3.14", -3.14)]
    [InlineData("-0.0", 0.0)]
    [InlineData("-(1.5 + 2.5)", -4.0)]
    [InlineData("-(-3.14)", 3.14)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task UnaryMinus_Float(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.001);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Nested function calls
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("UPPER(LOWER('HELLO'))", "HELLO")]
    [InlineData("LOWER(UPPER('hello'))", "hello")]
    [InlineData("UPPER(SUBSTR('hello world', 1, 5))", "HELLO")]
    [InlineData("LENGTH(UPPER('abc'))", 3L)]
    [InlineData("CONCAT(UPPER('a'), LOWER('B'))", "Ab")]
    [InlineData("CONCAT(LOWER('ABC'), UPPER('def'))", "abcDEF")]
    [InlineData("REVERSE(UPPER('abc'))", "CBA")]
    [InlineData("TRIM(CONCAT('  ', 'hello', '  '))", "hello")]
    [InlineData("UPPER(TRIM('  abc  '))", "ABC")]
    [InlineData("LENGTH(CONCAT('ab', 'cd', 'ef'))", 6L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NestedFunctions_String(string expr, object expected)
    {
        var result = await Eval(expr);
        if (expected is long l)
            ((long)result!).Should().Be(l);
        else
            result!.ToString().Should().Be((string)expected);
    }

    [Theory]
    [InlineData("ABS(SIGN(-5))", 1L)]
    [InlineData("ABS(-ABS(-10))", 10L)]
    [InlineData("SIGN(ABS(-42))", 1L)]
    [InlineData("MOD(ABS(-10), 3)", 1L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NestedFunctions_Math(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("CEIL(SQRT(2.0))", 2.0)]
    [InlineData("FLOOR(SQRT(10.0))", 3.0)]
    [InlineData("ROUND(LOG(EXP(1.0)), 2)", 1.0)]
    [InlineData("ABS(FLOOR(-3.7))", 4.0)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NestedFunctions_MathFloat(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.01);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. CASE expressions
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#case
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END", "yes")]
    [InlineData("CASE WHEN 1 = 2 THEN 'yes' ELSE 'no' END", "no")]
    [InlineData("CASE WHEN TRUE THEN 'a' ELSE 'b' END", "a")]
    [InlineData("CASE WHEN FALSE THEN 'a' ELSE 'b' END", "b")]
    [InlineData("CASE WHEN 2 > 1 THEN 'greater' ELSE 'not' END", "greater")]
    [InlineData("CASE WHEN 1 > 2 THEN 'greater' ELSE 'not' END", "not")]
    [InlineData("CASE WHEN 'a' = 'a' THEN 'match' ELSE 'no' END", "match")]
    [InlineData("CASE WHEN 'a' = 'b' THEN 'match' ELSE 'no' END", "no")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Case_WhenString(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("CASE WHEN 1 = 1 THEN 10 ELSE 20 END", 10L)]
    [InlineData("CASE WHEN 1 = 2 THEN 10 ELSE 20 END", 20L)]
    [InlineData("CASE WHEN 5 > 3 THEN 5 + 3 ELSE 5 - 3 END", 8L)]
    [InlineData("CASE WHEN 3 > 5 THEN 5 + 3 ELSE 5 - 3 END", 2L)]
    [InlineData("CASE WHEN 1 + 1 = 2 THEN 100 ELSE 0 END", 100L)]
    [InlineData("CASE WHEN 1 + 1 = 3 THEN 100 ELSE 0 END", 0L)]
    [InlineData("CASE 1 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 10L)]
    [InlineData("CASE 2 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 20L)]
    [InlineData("CASE 3 WHEN 1 THEN 10 WHEN 2 THEN 20 ELSE 30 END", 30L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Case_WhenInteger(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("CASE WHEN TRUE THEN CASE WHEN TRUE THEN 'inner' ELSE 'x' END ELSE 'outer' END", "inner")]
    [InlineData("CASE WHEN TRUE THEN CASE WHEN FALSE THEN 'inner' ELSE 'x' END ELSE 'outer' END", "x")]
    [InlineData("CASE WHEN FALSE THEN 'a' ELSE CASE WHEN TRUE THEN 'nested' ELSE 'b' END END", "nested")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Case_Nested(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("CASE WHEN 1 = 1 THEN 'first' WHEN 2 = 2 THEN 'second' ELSE 'none' END", "first")]
    [InlineData("CASE WHEN 1 = 2 THEN 'first' WHEN 2 = 2 THEN 'second' ELSE 'none' END", "second")]
    [InlineData("CASE WHEN 1 = 2 THEN 'first' WHEN 2 = 3 THEN 'second' ELSE 'none' END", "none")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Case_MultipleWhen(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Column aliasing
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_list
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Alias_SimpleExpression()
    {
        var rows = await EvalRows("SELECT 1 + 2 AS sum_result");
        ((long)rows[0]["sum_result"]!).Should().Be(3L);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Alias_StringExpression()
    {
        var rows = await EvalRows("SELECT 'hello' AS greeting");
        rows[0]["greeting"]!.ToString().Should().Be("hello");
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Alias_MultipleColumns()
    {
        var rows = await EvalRows("SELECT 1 AS a, 2 AS b, 3 AS c");
        ((long)rows[0]["a"]!).Should().Be(1L);
        ((long)rows[0]["b"]!).Should().Be(2L);
        ((long)rows[0]["c"]!).Should().Be(3L);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Alias_MixedTypes()
    {
        var rows = await EvalRows("SELECT 42 AS num, 'text' AS str, TRUE AS flag, 3.14 AS flt");
        ((long)rows[0]["num"]!).Should().Be(42L);
        rows[0]["str"]!.ToString().Should().Be("text");
        ((bool)rows[0]["flag"]!).Should().Be(true);
        ((double)rows[0]["flt"]!).Should().BeApproximately(3.14, 0.001);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Alias_ExpressionColumns()
    {
        var rows = await EvalRows("SELECT 1 + 1 AS two, 2 * 3 AS six, 'a' || 'b' AS ab");
        ((long)rows[0]["two"]!).Should().Be(2L);
        ((long)rows[0]["six"]!).Should().Be(6L);
        rows[0]["ab"]!.ToString().Should().Be("ab");
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Subquery expressions — scalar subqueries
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/expression_subqueries#scalar_subquery_concepts
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("(SELECT 1)", 1L)]
    [InlineData("(SELECT 42)", 42L)]
    [InlineData("(SELECT 1 + 2)", 3L)]
    [InlineData("(SELECT 10 * 5)", 50L)]
    [InlineData("(SELECT (SELECT 1))", 1L)]
    [InlineData("(SELECT (SELECT 99))", 99L)]
    [InlineData("(SELECT 1) + (SELECT 2)", 3L)]
    [InlineData("(SELECT 10) * (SELECT 5)", 50L)]
    [InlineData("(SELECT 100) - (SELECT 1)", 99L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ScalarSubquery_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("(SELECT 'hello')", "hello")]
    [InlineData("(SELECT 'a' || 'b')", "ab")]
    [InlineData("(SELECT UPPER('test'))", "TEST")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ScalarSubquery_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("(SELECT TRUE)", true)]
    [InlineData("(SELECT FALSE)", false)]
    [InlineData("(SELECT 1 = 1)", true)]
    [InlineData("(SELECT 1 > 2)", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ScalarSubquery_Bool(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. ARRAY literals
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#array_literals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLiteral_IntegerArray()
    {
        var result = await Eval("[1, 2, 3]");
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLiteral_StringArray()
    {
        var result = await Eval("['a', 'b', 'c']");
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLiteral_SingleElement()
    {
        var result = await Eval("[42]");
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLiteral_BoolArray()
    {
        var result = await Eval("[TRUE, FALSE, TRUE]");
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLiteral_FloatArray()
    {
        var result = await Eval("[1.1, 2.2, 3.3]");
        result.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. ARRAY subscript — OFFSET and ORDINAL
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_subscript_operator
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[10, 20, 30][OFFSET(0)]", 10L)]
    [InlineData("[10, 20, 30][OFFSET(1)]", 20L)]
    [InlineData("[10, 20, 30][OFFSET(2)]", 30L)]
    [InlineData("[10, 20, 30][ORDINAL(1)]", 10L)]
    [InlineData("[10, 20, 30][ORDINAL(2)]", 20L)]
    [InlineData("[10, 20, 30][ORDINAL(3)]", 30L)]
    [InlineData("[100][OFFSET(0)]", 100L)]
    [InlineData("[100][ORDINAL(1)]", 100L)]
    [InlineData("[5, 10, 15, 20][OFFSET(3)]", 20L)]
    [InlineData("[5, 10, 15, 20][ORDINAL(4)]", 20L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArraySubscript_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("['a', 'b', 'c'][OFFSET(0)]", "a")]
    [InlineData("['a', 'b', 'c'][OFFSET(1)]", "b")]
    [InlineData("['a', 'b', 'c'][OFFSET(2)]", "c")]
    [InlineData("['x', 'y'][ORDINAL(1)]", "x")]
    [InlineData("['x', 'y'][ORDINAL(2)]", "y")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArraySubscript_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. EXISTS subquery
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/expression_subqueries#exists_subquery_concepts
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("EXISTS(SELECT 1)", true)]
    [InlineData("EXISTS(SELECT 42)", true)]
    [InlineData("EXISTS(SELECT 'abc')", true)]
    [InlineData("EXISTS(SELECT 1 FROM UNNEST(ARRAY<INT64>[]) AS x)", false)]
    [InlineData("EXISTS(SELECT 1 FROM (SELECT 1) AS t WHERE 1 = 2)", false)]
    [InlineData("NOT EXISTS(SELECT 1 FROM UNNEST(ARRAY<INT64>[]) AS x)", true)]
    [InlineData("NOT EXISTS(SELECT 1)", false)]
    [InlineData("EXISTS(SELECT 1 FROM (SELECT 1) AS t WHERE TRUE)", true)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    [Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
    public async Task Exists_Subquery(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. IN with subquery
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/expression_subqueries#in_subquery_concepts
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 IN (SELECT 1)", true)]
    [InlineData("1 IN (SELECT 2)", false)]
    [InlineData("'a' IN (SELECT 'a')", true)]
    [InlineData("'a' IN (SELECT 'b')", false)]
    [InlineData("1 IN UNNEST([1, 2, 3])", true)]
    [InlineData("4 IN UNNEST([1, 2, 3])", false)]
    [InlineData("'x' IN UNNEST(['x', 'y', 'z'])", true)]
    [InlineData("'w' IN UNNEST(['x', 'y', 'z'])", false)]
    [InlineData("1 NOT IN UNNEST([2, 3, 4])", true)]
    [InlineData("1 NOT IN UNNEST([1, 2, 3])", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task In_Subquery(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Concatenation operator precedence — mixing || with arithmetic
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#operator_precedence
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CAST(1 + 2 AS STRING) || ' items'", "3 items")]
    [InlineData("'total: ' || CAST(10 * 5 AS STRING)", "total: 50")]
    [InlineData("'a' || 'b' || 'c' || 'd' || 'e'", "abcde")]
    [InlineData("CONCAT('x', 'y') || 'z'", "xyz")]
    [InlineData("'z' || CONCAT('x', 'y')", "zxy")]
    [InlineData("UPPER('a') || LOWER('B')", "Ab")]
    [InlineData("CAST(1 AS STRING) || CAST(2 AS STRING) || CAST(3 AS STRING)", "123")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ConcatPrecedence(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Type inference from literals
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#integer_literals
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#floating_point_literals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_IntegerIsInt64()
    {
        var result = await Eval("1");
        result.Should().BeOfType<long>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_FloatIsFloat64()
    {
        var result = await Eval("1.0");
        result.Should().BeOfType<double>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_StringIsString()
    {
        var result = await Eval("'hello'");
        result.Should().BeOfType<string>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_BoolIsBool()
    {
        var result = await Eval("TRUE");
        result.Should().BeOfType<bool>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_ArithmeticInt64()
    {
        var result = await Eval("1 + 2");
        result.Should().BeOfType<long>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_ArithmeticFloat64()
    {
        var result = await Eval("1.0 + 2.0");
        result.Should().BeOfType<double>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_ConcatIsString()
    {
        var result = await Eval("'a' || 'b'");
        result.Should().BeOfType<string>();
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task TypeInference_ComparisonIsBool()
    {
        var result = await Eval("1 = 1");
        result.Should().BeOfType<bool>();
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Multiple columns with various expressions
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax#select_list
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task MultipleColumns_Integers()
    {
        var rows = await EvalRows("SELECT 1 AS a, 2 AS b, 3 AS c, 4 AS d, 5 AS e");
        ((long)rows[0]["a"]!).Should().Be(1L);
        ((long)rows[0]["b"]!).Should().Be(2L);
        ((long)rows[0]["c"]!).Should().Be(3L);
        ((long)rows[0]["d"]!).Should().Be(4L);
        ((long)rows[0]["e"]!).Should().Be(5L);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task MultipleColumns_Expressions()
    {
        var rows = await EvalRows("SELECT 1 + 1 AS sum, 3 * 4 AS product, 10 / 2 AS quotient");
        ((long)rows[0]["sum"]!).Should().Be(2L);
        ((long)rows[0]["product"]!).Should().Be(12L);
        // Ref: INT64 / INT64 → FLOAT64 per Spanner spec
        ((double)rows[0]["quotient"]!).Should().Be(5.0);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task MultipleColumns_FunctionCalls()
    {
        var rows = await EvalRows("SELECT UPPER('a') AS u, LOWER('B') AS l, LENGTH('abc') AS len");
        rows[0]["u"]!.ToString().Should().Be("A");
        rows[0]["l"]!.ToString().Should().Be("b");
        ((long)rows[0]["len"]!).Should().Be(3L);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task MultipleColumns_CaseExpressions()
    {
        var rows = await EvalRows("SELECT CASE WHEN TRUE THEN 1 ELSE 0 END AS a, CASE WHEN FALSE THEN 1 ELSE 0 END AS b");
        ((long)rows[0]["a"]!).Should().Be(1L);
        ((long)rows[0]["b"]!).Should().Be(0L);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task MultipleColumns_Booleans()
    {
        var rows = await EvalRows("SELECT TRUE AS t, FALSE AS f, 1 = 1 AS eq, 1 > 2 AS gt");
        ((bool)rows[0]["t"]!).Should().BeTrue();
        ((bool)rows[0]["f"]!).Should().BeFalse();
        ((bool)rows[0]["eq"]!).Should().BeTrue();
        ((bool)rows[0]["gt"]!).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. String escape sequences — single quotes in strings
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("'it''s'", "it's")]
    [InlineData("'can''t'", "can't")]
    [InlineData("'she said ''hi'''", "she said 'hi'")]
    [InlineData("''''", "'")]
    [InlineData("''''''", "''")]
    [InlineData("'a''b''c'", "a'b'c")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    [Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
    public async Task StringEscape_SingleQuotes(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("'tab\\there'", "tab\there")]
    [InlineData("'back\\\\slash'", "back\\slash")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task StringEscape_Backslash(string expr, string expected)
    {
        // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#string_and_bytes_literals
        //   Standard strings interpret backslash escapes: \t→tab, \\→backslash
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // 17. Scientific notation
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#floating_point_literals
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1e3", 1000.0)]
    [InlineData("1e0", 1.0)]
    [InlineData("1e1", 10.0)]
    [InlineData("1e2", 100.0)]
    [InlineData("2.5e2", 250.0)]
    [InlineData("2.5e-1", 0.25)]
    [InlineData("1.5e3", 1500.0)]
    [InlineData("3e-2", 0.03)]
    [InlineData("5e-1", 0.5)]
    [InlineData("1.23e4", 12300.0)]
    [InlineData("-1e3", -1000.0)]
    [InlineData("-2.5e-1", -0.25)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ScientificNotation(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.001);
    }

    // ═══════════════════════════════════════════════════════════════
    // 19. Negative literals
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/lexical#integer_literals
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("-42", -42L)]
    [InlineData("-1", -1L)]
    [InlineData("-100", -100L)]
    [InlineData("-999999", -999999L)]
    [InlineData("-0", 0L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NegativeLiterals_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("-3.14", -3.14)]
    [InlineData("-0.001", -0.001)]
    [InlineData("-123.456", -123.456)]
    [InlineData("-0.0", 0.0)]
    [InlineData("-99.99", -99.99)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NegativeLiterals_Float(string expr, double expected)
    {
        ((double)(await Eval(expr))!).Should().BeApproximately(expected, 0.001);
    }

    // ═══════════════════════════════════════════════════════════════
    // 20. Boolean expressions as values
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 = 1", true)]
    [InlineData("1 = 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("1 != 1", false)]
    [InlineData("1 < 2", true)]
    [InlineData("2 < 1", false)]
    [InlineData("2 > 1", true)]
    [InlineData("1 > 2", false)]
    [InlineData("1 <= 1", true)]
    [InlineData("1 <= 2", true)]
    [InlineData("2 <= 1", false)]
    [InlineData("1 >= 1", true)]
    [InlineData("2 >= 1", true)]
    [InlineData("1 >= 2", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_Comparison(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("TRUE AND TRUE", true)]
    [InlineData("TRUE AND FALSE", false)]
    [InlineData("FALSE AND TRUE", false)]
    [InlineData("FALSE AND FALSE", false)]
    [InlineData("TRUE OR TRUE", true)]
    [InlineData("TRUE OR FALSE", true)]
    [InlineData("FALSE OR TRUE", true)]
    [InlineData("FALSE OR FALSE", false)]
    [InlineData("NOT TRUE", false)]
    [InlineData("NOT FALSE", true)]
    [InlineData("NOT NOT TRUE", true)]
    [InlineData("TRUE AND TRUE AND TRUE", true)]
    [InlineData("TRUE AND TRUE AND FALSE", false)]
    [InlineData("FALSE OR FALSE OR TRUE", true)]
    [InlineData("FALSE OR FALSE OR FALSE", false)]
    [InlineData("(TRUE OR FALSE) AND TRUE", true)]
    [InlineData("TRUE OR (FALSE AND FALSE)", true)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_Logical(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("'abc' = 'abc'", true)]
    [InlineData("'abc' = 'def'", false)]
    [InlineData("'abc' != 'def'", true)]
    [InlineData("'abc' < 'def'", true)]
    [InlineData("'def' < 'abc'", false)]
    [InlineData("'abc' > 'ABC'", true)]
    [InlineData("'a' < 'b'", true)]
    [InlineData("'z' > 'a'", true)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_StringComparison(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("1 BETWEEN 0 AND 2", true)]
    [InlineData("1 BETWEEN 1 AND 1", true)]
    [InlineData("5 BETWEEN 1 AND 10", true)]
    [InlineData("0 BETWEEN 1 AND 10", false)]
    [InlineData("11 BETWEEN 1 AND 10", false)]
    [InlineData("1 NOT BETWEEN 5 AND 10", true)]
    [InlineData("7 NOT BETWEEN 5 AND 10", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_Between(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("1 IN (1, 2, 3)", true)]
    [InlineData("4 IN (1, 2, 3)", false)]
    [InlineData("'a' IN ('a', 'b', 'c')", true)]
    [InlineData("'d' IN ('a', 'b', 'c')", false)]
    [InlineData("1 NOT IN (2, 3, 4)", true)]
    [InlineData("1 NOT IN (1, 2, 3)", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_In(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("'hello' LIKE 'hello'", true)]
    [InlineData("'hello' LIKE 'world'", false)]
    [InlineData("'hello' LIKE 'h%'", true)]
    [InlineData("'hello' LIKE '%lo'", true)]
    [InlineData("'hello' LIKE '%ell%'", true)]
    [InlineData("'hello' LIKE 'H%'", false)]
    [InlineData("'hello' LIKE '_ello'", true)]
    [InlineData("'hello' LIKE '____o'", true)]
    [InlineData("'hello' LIKE '_____'", true)]
    [InlineData("'hello' LIKE '______'", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_Like(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("1 IS NOT NULL", true)]
    [InlineData("'a' IS NOT NULL", true)]
    [InlineData("TRUE IS NOT NULL", true)]
    [InlineData("NULL IS NULL", true)]
    [InlineData("NULL IS NOT NULL", false)]
    [InlineData("1 IS NULL", false)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task BooleanExpr_IsNull(string expr, bool expected)
    {
        ((bool)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // Additional: IF expression
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("IF(TRUE, 'yes', 'no')", "yes")]
    [InlineData("IF(FALSE, 'yes', 'no')", "no")]
    [InlineData("IF(1 = 1, 'match', 'nope')", "match")]
    [InlineData("IF(1 = 2, 'match', 'nope')", "nope")]
    [InlineData("IF(LENGTH('abc') = 3, 'ok', 'fail')", "ok")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task If_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("IF(TRUE, 1, 0)", 1L)]
    [InlineData("IF(FALSE, 1, 0)", 0L)]
    [InlineData("IF(5 > 3, 10, 20)", 10L)]
    [InlineData("IF(3 > 5, 10, 20)", 20L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task If_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // Additional: IFNULL and NULLIF
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#nullif
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("IFNULL(1, 2)", 1L)]
    [InlineData("IFNULL(NULL, 42)", 42L)]
    [InlineData("IFNULL(10, 20)", 10L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task IfNull_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("IFNULL('a', 'b')", "a")]
    [InlineData("IFNULL(NULL, 'default')", "default")]
    [InlineData("IFNULL('hello', 'world')", "hello")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task IfNull_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("NULLIF(1, 1)")]
    [InlineData("NULLIF('a', 'a')")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NullIf_ReturnsNull(string expr)
    {
        var result = await Eval(expr);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("NULLIF(1, 2)", 1L)]
    [InlineData("NULLIF(10, 20)", 10L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NullIf_ReturnFirst_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("NULLIF('a', 'b')", "a")]
    [InlineData("NULLIF('hello', 'world')", "hello")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task NullIf_ReturnFirst_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // Additional: COALESCE
    // Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("COALESCE(1, 2, 3)", 1L)]
    [InlineData("COALESCE(NULL, 2, 3)", 2L)]
    [InlineData("COALESCE(NULL, NULL, 3)", 3L)]
    [InlineData("COALESCE(10, NULL, NULL)", 10L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Coalesce_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("COALESCE('a', 'b')", "a")]
    [InlineData("COALESCE(NULL, 'b')", "b")]
    [InlineData("COALESCE(NULL, NULL, 'c')", "c")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Coalesce_String(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Fact]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Coalesce_AllNull()
    {
        var result = await Eval("COALESCE(NULL, NULL, NULL)");
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // Additional: Mixed complex expressions
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CASE WHEN 1 + 1 = 2 THEN UPPER('yes') ELSE LOWER('NO') END", "YES")]
    [InlineData("IF(LENGTH('abc') > 2, 'long', 'short')", "long")]
    [InlineData("IF(LENGTH('ab') > 2, 'long', 'short')", "short")]
    [InlineData("CONCAT(IF(TRUE, 'A', 'B'), IF(FALSE, 'C', 'D'))", "AD")]
    [InlineData("UPPER(IF(1 < 2, 'yes', 'no'))", "YES")]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Complex_MixedExpressions(string expr, string expected)
    {
        (await Eval(expr))!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("ABS(-5) + LENGTH('abc')", 8L)]
    [InlineData("IF(TRUE, 10, 20) + IF(FALSE, 100, 200)", 210L)]
    [InlineData("CASE WHEN TRUE THEN 5 ELSE 0 END * 2", 10L)]
    [InlineData("(SELECT 5) + (SELECT 10)", 15L)]
    [InlineData("COALESCE(NULL, 3) * 4", 12L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task Complex_MixedExpressions_Integer(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

    [Theory]
    [InlineData("ARRAY_LENGTH([1, 2, 3])", 3L)]
    [InlineData("ARRAY_LENGTH(['a', 'b'])", 2L)]
    [InlineData("ARRAY_LENGTH([10])", 1L)]
    [InlineData("ARRAY_LENGTH([1, 2, 3, 4, 5])", 5L)]
    [Trait(TestTraits.Category, "SelectExpressionExtended")]
    public async Task ArrayLength(string expr, long expected)
    {
        ((long)(await Eval(expr))!).Should().Be(expected);
    }

}
