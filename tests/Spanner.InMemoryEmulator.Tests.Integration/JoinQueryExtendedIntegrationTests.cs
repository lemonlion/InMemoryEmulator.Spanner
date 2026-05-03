using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class JoinQueryExtendedIntegrationTests : IntegrationTestBase
{
    private const string T1 = "JnExtT1";
    private const string T2 = "JnExtT2";
    private const string T3 = "JnExtT3";

    public JoinQueryExtendedIntegrationTests(EmulatorSession session) : base(session) { }

    private async Task EnsureTables()
    {
        await ExecuteDdlAsync(
            $"CREATE TABLE IF NOT EXISTS {T1} (Id INT64 NOT NULL, Name STRING(MAX), DeptId INT64) PRIMARY KEY (Id)",
            $"CREATE TABLE IF NOT EXISTS {T2} (DeptId INT64 NOT NULL, DeptName STRING(MAX)) PRIMARY KEY (DeptId)",
            $"CREATE TABLE IF NOT EXISTS {T3} (Id INT64 NOT NULL, EmpId INT64, Project STRING(MAX)) PRIMARY KEY (Id)");
        // T1: Employees
        await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice", ["DeptId"] = 10L });
        await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 2L, ["Name"] = "Bob", ["DeptId"] = 20L });
        await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 3L, ["Name"] = "Charlie", ["DeptId"] = 10L });
        await InsertOrUpdateAsync(T1, new Dictionary<string, object?> { ["Id"] = 4L, ["Name"] = "Diana", ["DeptId"] = null });
        // T2: Departments
        await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["DeptId"] = 10L, ["DeptName"] = "Engineering" });
        await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["DeptId"] = 20L, ["DeptName"] = "Marketing" });
        await InsertOrUpdateAsync(T2, new Dictionary<string, object?> { ["DeptId"] = 30L, ["DeptName"] = "Sales" });
        // T3: Projects
        await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 1L, ["EmpId"] = 1L, ["Project"] = "Alpha" });
        await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 2L, ["EmpId"] = 1L, ["Project"] = "Beta" });
        await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 3L, ["EmpId"] = 2L, ["Project"] = "Alpha" });
        await InsertOrUpdateAsync(T3, new Dictionary<string, object?> { ["Id"] = 4L, ["EmpId"] = 5L, ["Project"] = "Gamma" });
    }

    // ═══════════════════════════════════════════════════════════════
    // INNER JOIN (25 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_Basic()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Id, d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(3);
        ((long)rows[0]["Id"]!).Should().Be(1L);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_MultiColumnResult()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Id, e.Name, d.DeptId, d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((long)rows[0]["DeptId"]!).Should().Be(10L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithWhereClause()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'Engineering' ORDER BY e.Name");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[1]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithOrderBy()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Name DESC");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Charlie");
        ((string)rows[2]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_OnComputedExpression()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId + 0 = d.DeptId ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_MultipleConditions()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId AND d.DeptName = 'Engineering' ORDER BY e.Name");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_ReturnsZeroRows_WhenNoMatch()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'NonExistent'");
        rows.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithDistinct()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT DISTINCT d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
        ((string)rows[1]["DeptName"]!).Should().Be("Marketing");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithGroupBy()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((long)rows[0]["cnt"]!).Should().Be(2L); // Engineering: Alice, Charlie
        ((long)rows[1]["cnt"]!).Should().Be(1L); // Marketing: Bob
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithHaving()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId GROUP BY d.DeptName HAVING COUNT(*) > 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Name LIMIT 2");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithTableAliases()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT emp.Name, dept.DeptName FROM {T1} AS emp INNER JOIN {T2} AS dept ON emp.DeptId = dept.DeptId ORDER BY emp.Id");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_SelfJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT a.Name AS Name1, b.Name AS Name2 FROM {T1} a INNER JOIN {T1} b ON a.DeptId = b.DeptId AND a.Id < b.Id ORDER BY a.Name");
        // Engineering: Alice(1) & Charlie(3)
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name1"]!).Should().Be("Alice");
        ((string)rows[0]["Name2"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_AllRowsMatch()
    {
        await EnsureTables();
        // All T2 departments join at least conceptually, but only 10 and 20 have employees
        var rows = await QueryAsync($"SELECT d.DeptId FROM {T2} d INNER JOIN {T1} e ON d.DeptId = e.DeptId");
        rows.Should().HaveCount(3); // Eng(Alice), Eng(Charlie), Mkt(Bob)
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithIsNotNullFilter()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Name IS NOT NULL ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithInFilter()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName IN ('Engineering', 'Sales') ORDER BY e.Name");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[1]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithStringComparison()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName > 'F' ORDER BY e.Name");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Bob"); // Marketing > F
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithInequalityCondition()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId <> d.DeptId WHERE e.Id = 1 ORDER BY d.DeptName");
        // Alice (DeptId=10) joins depts 20 and 30
        rows.Should().HaveCount(2);
        ((string)rows[0]["DeptName"]!).Should().Be("Marketing");
        ((string)rows[1]["DeptName"]!).Should().Be("Sales");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_JoinKeyExpression()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId * 1 = d.DeptId * 1 ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_SelectStar()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.*, d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id = 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WithOffsetLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Name LIMIT 1 OFFSET 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Bob");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_WhereOnBothTables()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id > 1 AND d.DeptName = 'Engineering'");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task InnerJoin_CountResult()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(3L);
    }

    // ═══════════════════════════════════════════════════════════════
    // LEFT JOIN (20 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_Basic()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
        rows[3]["DeptName"].Should().BeNull(); // Diana has no dept
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_NullInRightColumns()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Name = 'Diana'");
        rows.Should().HaveCount(1);
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WhereOnLeftTable()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id <= 2 ORDER BY e.Id");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[1]["Name"]!).Should().Be("Bob");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WhereOnRightTable()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'Engineering' ORDER BY e.Name");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WithAggregate()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName, COUNT(e.Id) AS cnt FROM {T2} d LEFT JOIN {T1} e ON d.DeptId = e.DeptId GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(3);
        ((long)rows[0]["cnt"]!).Should().Be(2L); // Engineering
        ((long)rows[1]["cnt"]!).Should().Be(1L); // Marketing
        ((long)rows[2]["cnt"]!).Should().Be(0L); // Sales
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WithOrderBy()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName, e.Name");
        rows.Should().HaveCount(4);
        // NULL sorts first in Spanner
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_CountingNulls()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId IS NULL");
        ((long)result!).Should().Be(1L); // Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_MultipleConditions()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId AND d.DeptName = 'Engineering' ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering"); // Alice
        rows[1]["DeptName"].Should().BeNull(); // Bob (dept 20, not Engineering)
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_IsNullFindNonMatching()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId IS NULL");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Diana");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_SelfJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT a.Name AS Name1, b.Name AS Name2 FROM {T1} a LEFT JOIN {T1} b ON a.DeptId = b.DeptId AND a.Id <> b.Id ORDER BY a.Id");
        // Alice→Charlie, Bob→null (only one in dept 20), Charlie→Alice, Diana→null
        rows.Should().HaveCount(4);
        ((string)rows[0]["Name2"]!).Should().Be("Charlie");
        rows[1]["Name2"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WithDistinct()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT DISTINCT d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName");
        rows.Should().HaveCount(3); // NULL, Engineering, Marketing
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 2");
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_CountAll()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(4L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_CoalesceNullDeptName()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, COALESCE(d.DeptName, 'Unassigned') AS Dept FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((string)rows[3]["Dept"]!).Should().Be("Unassigned");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WithGroupByHaving()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName, COUNT(e.Id) AS cnt FROM {T2} d LEFT JOIN {T1} e ON d.DeptId = e.DeptId GROUP BY d.DeptName HAVING COUNT(e.Id) > 0 ORDER BY d.DeptName");
        rows.Should().HaveCount(2); // Engineering, Marketing
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_RightTableMultipleMatches()
    {
        await EnsureTables();
        // Alice has 2 projects
        var rows = await QueryAsync($"SELECT e.Name, p.Project FROM {T1} e LEFT JOIN {T3} p ON e.Id = p.EmpId WHERE e.Id = 1 ORDER BY p.Project");
        rows.Should().HaveCount(2);
        ((string)rows[0]["Project"]!).Should().Be("Alpha");
        ((string)rows[1]["Project"]!).Should().Be("Beta");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_AllColumnsFromBothTables()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Id, e.Name, e.DeptId, d.DeptId AS DDeptId, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id = 4");
        rows.Should().HaveCount(1);
        rows[0]["DDeptId"].Should().BeNull();
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_SumWithNulls()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT SUM(d.DeptId) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        // 10 + 20 + 10 = 40 (Diana's NULL doesn't add)
        ((long)result!).Should().Be(40L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task LeftJoin_WhereOrCondition()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'Engineering' OR d.DeptId IS NULL ORDER BY e.Name");
        rows.Should().HaveCount(3); // Alice, Charlie, Diana
    }

    // ═══════════════════════════════════════════════════════════════
    // RIGHT JOIN (10 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_Basic()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName, e.Name");
        rows.Should().HaveCount(4); // Eng(Alice,Charlie), Mkt(Bob), Sales(null)
        ((string)rows[3]["DeptName"]!).Should().Be("Sales");
        rows[3]["Name"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_NullInLeftColumns()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'Sales'");
        rows.Should().HaveCount(1);
        rows[0]["Name"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_Count()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(4L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_WithAggregate()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName, COUNT(e.Id) AS cnt FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(3);
        ((long)rows[2]["cnt"]!).Should().Be(0L); // Sales
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_IsNullFindNonMatching()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id IS NULL");
        rows.Should().HaveCount(1);
        ((string)rows[0]["DeptName"]!).Should().Be("Sales");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_WithOrderBy()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName DESC");
        rows.Should().HaveCount(4);
        ((string)rows[0]["DeptName"]!).Should().Be("Sales");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_WithWhereFilter()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId >= 20 ORDER BY d.DeptName");
        rows.Should().HaveCount(2); // Marketing(Bob), Sales(null)
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_DistinctDeptNames()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT DISTINCT d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_WithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName LIMIT 2");
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task RightJoin_CoalesceLeftColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT COALESCE(e.Name, 'Nobody') AS Emp, d.DeptName FROM {T1} e RIGHT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = 'Sales'");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Emp"]!).Should().Be("Nobody");
    }

    // ═══════════════════════════════════════════════════════════════
    // CROSS JOIN (10 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_Basic()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d ORDER BY e.Id, d.DeptId");
        rows.Should().HaveCount(12); // 4 employees * 3 departments
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_WithWhere()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d WHERE e.DeptId = d.DeptId ORDER BY e.Id");
        // Effectively an inner join via WHERE
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_Count()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e CROSS JOIN {T2} d");
        ((long)result!).Should().Be(12L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_SelfCrossJoin()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T2} a CROSS JOIN {T2} b");
        ((long)result!).Should().Be(9L); // 3 * 3
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_WithFilter()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d WHERE e.Id = 1 ORDER BY d.DeptName");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_WithDistinct()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT DISTINCT d.DeptName FROM {T1} e CROSS JOIN {T2} d ORDER BY d.DeptName");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_WithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d ORDER BY e.Id, d.DeptId LIMIT 5");
        rows.Should().HaveCount(5);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_ThreeTables()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e CROSS JOIN {T2} d CROSS JOIN {T3} p");
        ((long)result!).Should().Be(48L); // 4 * 3 * 4
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_WithGroupBy()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, COUNT(*) AS cnt FROM {T1} e CROSS JOIN {T2} d GROUP BY e.Name ORDER BY e.Name");
        rows.Should().HaveCount(4);
        ((long)rows[0]["cnt"]!).Should().Be(3L); // each emp crosses 3 depts
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task CrossJoin_Comma_Syntax()
    {
        await EnsureTables();
        var rows = await QueryAsync($"SELECT e.Name, d.DeptName FROM {T1} e, {T2} d WHERE e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(3); // same as inner join
    }

    // ═══════════════════════════════════════════════════════════════
    // Multi-table JOIN (15 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_InnerJoinChain()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY e.Name, p.Project");
        // Alice→Eng→Alpha, Alice→Eng→Beta, Bob→Mkt→Alpha
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[0]["Project"]!).Should().Be("Alpha");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_LeftJoinChain()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName, p.Project FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY e.Id, p.Project");
        // Alice(Eng,Alpha), Alice(Eng,Beta), Bob(Mkt,Alpha), Charlie(Eng,null), Diana(null,null)
        rows.Should().HaveCount(5);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        rows[4]["DeptName"].Should().BeNull(); // Diana
        rows[4]["Project"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_MixedInnerLeft()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY e.Name, p.Project");
        // Alice→Eng→Alpha, Alice→Eng→Beta, Bob→Mkt→Alpha, Charlie→Eng→null
        rows.Should().HaveCount(4);
        rows[3]["Project"].Should().BeNull(); // Charlie has no projects
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_WithAggregate()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(DISTINCT p.Project) AS projCount FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((long)rows[0]["projCount"]!).Should().Be(2L); // Engineering: Alpha, Beta
        ((long)rows[1]["projCount"]!).Should().Be(1L); // Marketing: Alpha
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_WithOrderBy()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY p.Project, e.Name");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Project"]!).Should().Be("Alpha");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_WithWhere()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"WHERE d.DeptName = 'Engineering'");
        // Alice→Alpha, Alice→Beta
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_CountStar()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT COUNT(*) FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId");
        ((long)result!).Should().Be(3L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_LeftJoinChain_WithHaving()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COUNT(p.Project) AS cnt FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"GROUP BY e.Name HAVING COUNT(p.Project) > 0 ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice(2), Bob(1)
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_WithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY e.Name, p.Project LIMIT 2");
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_AllLeftJoins_NullPropagation()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName, p.Project FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"WHERE e.Name = 'Diana'");
        rows.Should().HaveCount(1);
        rows[0]["DeptName"].Should().BeNull();
        rows[0]["Project"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_DistinctProjects()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT DISTINCT p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY p.Project");
        rows.Should().HaveCount(2); // Alpha, Beta
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_MixedLeftRight()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"ORDER BY e.Id");
        // Verify the base left join works as expected
        rows.Should().HaveCount(4);
        rows[3]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_SumAcrossTables()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT SUM(e.Id + p.Id) FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId");
        // Alice(1)+Alpha(1)=2, Alice(1)+Beta(2)=3, Bob(2)+Alpha(3)=5 => 10
        ((long)result!).Should().Be(10L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ThreeTableJoin_MaxProject()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT MAX(p.Project) FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId");
        ((string)result!).Should().Be("Beta");
    }

    // ═══════════════════════════════════════════════════════════════
    // JOIN with subquery (15 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_DerivedTable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.DeptName FROM {T1} e " +
            $"INNER JOIN (SELECT DeptId, DeptName FROM {T2}) sub ON e.DeptId = sub.DeptId " +
            $"ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_WithAggregateSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.cnt FROM {T1} e " +
            $"INNER JOIN (SELECT EmpId, COUNT(*) AS cnt FROM {T3} GROUP BY EmpId) sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice(2), Bob(1)
        ((long)rows[0]["cnt"]!).Should().Be(2L);
        ((long)rows[1]["cnt"]!).Should().Be(1L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_FilteredSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.Project FROM {T1} e " +
            $"INNER JOIN (SELECT EmpId, Project FROM {T3} WHERE Project = 'Alpha') sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice, Bob
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_ScalarSubqueryInSelect()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, (SELECT COUNT(*) FROM {T3} p WHERE p.EmpId = e.Id) AS ProjCount " +
            $"FROM {T1} e ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((long)rows[0]["ProjCount"]!).Should().Be(2L); // Alice
        ((long)rows[3]["ProjCount"]!).Should().Be(0L); // Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_LeftJoinDerived()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.cnt FROM {T1} e " +
            $"LEFT JOIN (SELECT EmpId, COUNT(*) AS cnt FROM {T3} GROUP BY EmpId) sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((long)rows[0]["cnt"]!).Should().Be(2L);
        rows[2]["cnt"].Should().BeNull(); // Charlie
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_SubqueryWithOrderBy()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.Project FROM {T1} e " +
            $"INNER JOIN (SELECT EmpId, Project FROM {T3} ORDER BY Project) sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Name, sub.Project");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_SubqueryWithLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, sub.Project FROM {T1} e " +
            $"INNER JOIN (SELECT EmpId, Project FROM {T3} ORDER BY Id LIMIT 2) sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Name");
        // First 2 projects by Id: (1,Alpha), (2,Beta) both for Alice
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_SubqueryDistinct()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT sub.Project FROM (SELECT DISTINCT Project FROM {T3}) sub ORDER BY sub.Project");
        rows.Should().HaveCount(3); // Alpha, Beta, Gamma
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_TwoDerivedTables()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT a.Name, b.DeptName FROM (SELECT Id, Name, DeptId FROM {T1}) a " +
            $"INNER JOIN (SELECT DeptId, DeptName FROM {T2}) b ON a.DeptId = b.DeptId " +
            $"ORDER BY a.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_ExistsCorrelatedSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e WHERE EXISTS (SELECT 1 FROM {T3} p WHERE p.EmpId = e.Id) ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice, Bob
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_NotExistsCorrelatedSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e WHERE NOT EXISTS (SELECT 1 FROM {T3} p WHERE p.EmpId = e.Id) ORDER BY e.Name");
        rows.Should().HaveCount(2); // Charlie, Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_InSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e WHERE e.Id IN (SELECT EmpId FROM {T3}) ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice, Bob
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_NotInSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e WHERE e.Id NOT IN (SELECT EmpId FROM {T3} WHERE EmpId IS NOT NULL) ORDER BY e.Name");
        rows.Should().HaveCount(2); // Charlie, Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_DerivedWithCoalesce()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COALESCE(sub.cnt, 0) AS Projects FROM {T1} e " +
            $"LEFT JOIN (SELECT EmpId, COUNT(*) AS cnt FROM {T3} GROUP BY EmpId) sub ON e.Id = sub.EmpId " +
            $"ORDER BY e.Id");
        rows.Should().HaveCount(4);
        ((long)rows[0]["Projects"]!).Should().Be(2L);
        ((long)rows[2]["Projects"]!).Should().Be(0L); // Charlie
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinSubquery_DerivedTableWithAlias()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT t.Name FROM (SELECT Name FROM {T1} WHERE DeptId IS NOT NULL) AS t ORDER BY t.Name");
        rows.Should().HaveCount(3);
    }

    // ═══════════════════════════════════════════════════════════════
    // JOIN with aggregate (20 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_GroupByAfterJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((long)rows[0]["cnt"]!).Should().Be(2L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_CountPerGroup()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COUNT(p.Id) AS projCount FROM {T1} e " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId GROUP BY e.Name ORDER BY e.Name");
        rows.Should().HaveCount(4);
        ((long)rows[0]["projCount"]!).Should().Be(2L); // Alice
        ((long)rows[2]["projCount"]!).Should().Be(0L); // Charlie
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_SumAcrossJoin()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT SUM(e.Id) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(6L); // 1+2+3
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_AvgWithJoin()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT AVG(e.Id) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId");
        ((double)result!).Should().Be(2.0); // (1+2+3)/3
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_MinWithJoin()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT MIN(e.Name) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId");
        ((string)result!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_MaxWithJoin()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT MAX(e.Name) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId");
        ((string)result!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_HavingAfterJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName HAVING COUNT(*) >= 2");
        rows.Should().HaveCount(1);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_MultipleAggregates()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt, MIN(e.Name) AS minName, MAX(e.Name) AS maxName " +
            $"FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((string)rows[0]["minName"]!).Should().Be("Alice");
        ((string)rows[0]["maxName"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_CountDistinct()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT COUNT(DISTINCT p.Project) FROM {T1} e " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId");
        ((long)result!).Should().Be(2L); // Alpha, Beta
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_SumPerDepartment()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, SUM(e.Id) AS total FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((long)rows[0]["total"]!).Should().Be(4L); // Engineering: 1+3
        ((long)rows[1]["total"]!).Should().Be(2L); // Marketing: 2
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_CountWithLeftJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COUNT(d.DeptId) AS deptCount FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY e.Name ORDER BY e.Name");
        rows.Should().HaveCount(4);
        ((long)rows[0]["deptCount"]!).Should().Be(1L); // Alice
        ((long)rows[3]["deptCount"]!).Should().Be(0L); // Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_MinMaxProject()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, MIN(p.Project) AS minP, MAX(p.Project) AS maxP FROM {T1} e " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"GROUP BY e.Name ORDER BY e.Name");
        rows.Should().HaveCount(2);
        ((string)rows[0]["minP"]!).Should().Be("Alpha");
        ((string)rows[0]["maxP"]!).Should().Be("Beta"); // Alice
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_HavingWithSum()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, SUM(e.Id) AS total FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName HAVING SUM(e.Id) > 3");
        rows.Should().HaveCount(1);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering"); // 1+3=4
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_GroupByMultipleColumns()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, p.Project, COUNT(*) AS cnt FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"GROUP BY d.DeptName, p.Project ORDER BY d.DeptName, p.Project");
        rows.Should().HaveCount(3);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
        ((string)rows[0]["Project"]!).Should().Be("Alpha");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_CountStarVsCountColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT COUNT(*) AS cntAll, COUNT(d.DeptId) AS cntDept " +
            $"FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)rows[0]["cntAll"]!).Should().Be(4L);
        ((long)rows[0]["cntDept"]!).Should().Be(3L); // Diana's is null
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_AvgPerDepartment()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, AVG(e.Id) AS avgId FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(2);
        ((double)rows[0]["avgId"]!).Should().Be(2.0); // Engineering: (1+3)/2
        ((double)rows[1]["avgId"]!).Should().Be(2.0); // Marketing: 2/1
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_NestedAggregateInHaving()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COUNT(p.Id) AS cnt FROM {T1} e " +
            $"LEFT JOIN {T3} p ON e.Id = p.EmpId " +
            $"GROUP BY e.Name HAVING COUNT(p.Id) >= 1 ORDER BY e.Name");
        rows.Should().HaveCount(2); // Alice(2), Bob(1)
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_SumNullSafe()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT SUM(d.DeptId) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        // 10 + 20 + 10 = 40 (null from Diana is ignored)
        ((long)result!).Should().Be(40L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinAggregate_EmptyGroupAfterHaving()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"GROUP BY d.DeptName HAVING COUNT(*) > 100");
        rows.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // JOIN with ORDER/LIMIT (15 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByLeftColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Name");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[2]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByRightColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName, e.Name");
        rows.Should().HaveCount(3);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByExpression()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id + d.DeptId");
        rows.Should().HaveCount(3);
        // 1+10=11(Alice), 3+10=13(Charlie), 2+20=22(Bob)
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[2]["Name"]!).Should().Be("Bob");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_LimitAfterJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OffsetAfterJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 10 OFFSET 2");
        rows.Should().HaveCount(1); // Only Charlie left
        ((string)rows[0]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByDescending()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id DESC");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByMultipleColumns()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName ASC, e.Name DESC");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Charlie"); // Engineering, desc by name
        ((string)rows[1]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_LimitZero()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 0");
        rows.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OffsetBeyondResults()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 10 OFFSET 100");
        rows.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_LeftJoinOrderByNullable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY d.DeptName ASC, e.Name ASC");
        rows.Should().HaveCount(4);
        // NULLs sort first in Spanner
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OrderByAliasedColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name AS EmpName, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY EmpName");
        rows.Should().HaveCount(3);
        ((string)rows[0]["EmpName"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_LimitWithLeftJoin()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 3");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_CrossJoinOrdered()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d ORDER BY e.Name, d.DeptName LIMIT 3");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_ThreeTableOrderByThirdTable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"ORDER BY p.Project DESC, e.Name ASC");
        rows.Should().HaveCount(3);
        ((string)rows[0]["Project"]!).Should().Be("Beta");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task JoinOrderLimit_OffsetOnly()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 100 OFFSET 1");
        rows.Should().HaveCount(2); // Bob, Charlie
    }

    // ═══════════════════════════════════════════════════════════════
    // NULL handling in joins (15 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_InnerJoinExcludesNullKeys()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Name");
        // Diana has DeptId=NULL, should not match any dept
        rows.Should().HaveCount(3);
        rows.Select(r => (string)r["Name"]!).Should().NotContain("Diana");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_LeftJoinPreservesNullKey()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Name = 'Diana'");
        rows.Should().HaveCount(1);
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_IsNullOnJoinedColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName IS NULL ORDER BY e.Name");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Diana");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_CoalesceOnOuterJoinResult()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, COALESCE(d.DeptName, 'None') AS Dept FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        ((string)rows[3]["Dept"]!).Should().Be("None");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_NullEquality_NeverMatches()
    {
        await EnsureTables();
        // NULL = NULL is not true, so inner join on NULL keys should produce nothing from Diana
        var result = await QueryScalarAsync(
            $"SELECT COUNT(*) FROM {T1} a INNER JOIN {T1} b ON a.DeptId = b.DeptId WHERE a.Name = 'Diana'");
        ((long)result!).Should().Be(0L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_LeftJoinCountNulls()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT COUNT(*) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId IS NULL");
        ((long)result!).Should().Be(1L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_IfnullOnJoinResult()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, IFNULL(d.DeptName, 'Unknown') AS Dept FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        ((string)rows[3]["Dept"]!).Should().Be("Unknown");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_NullifOnJoinResult()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, NULLIF(d.DeptName, 'Marketing') AS Dept FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        // Bob's Marketing becomes NULL
        rows[1]["Dept"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_IsNotNullOnJoinedColumn()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId IS NOT NULL ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_AggregateIgnoresNulls()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT COUNT(d.DeptName) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(3L); // Diana contributes NULL, not counted
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_SumIgnoresNulls()
    {
        await EnsureTables();
        var result = await QueryScalarAsync(
            $"SELECT SUM(e.DeptId) FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId");
        ((long)result!).Should().Be(40L); // 10+20+10, Diana's NULL ignored
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_CaseWhenOnNullJoinResult()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, CASE WHEN d.DeptName IS NULL THEN 'No Dept' ELSE d.DeptName END AS Dept " +
            $"FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        ((string)rows[3]["Dept"]!).Should().Be("No Dept");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_NullSafeGroupBy()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e " +
            $"LEFT JOIN {T2} d ON e.DeptId = d.DeptId GROUP BY d.DeptName ORDER BY d.DeptName");
        rows.Should().HaveCount(3); // NULL, Engineering, Marketing
        rows[0]["DeptName"].Should().BeNull();
        ((long)rows[0]["cnt"]!).Should().Be(1L); // Diana
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_NullProjectColumn()
    {
        await EnsureTables();
        // T3 has EmpId=5 with no match in T1
        var rows = await QueryAsync(
            $"SELECT p.Project, e.Name FROM {T3} p LEFT JOIN {T1} e ON p.EmpId = e.Id WHERE p.EmpId = 5");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Project"]!).Should().Be("Gamma");
        rows[0]["Name"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task NullJoin_AllNullJoinColumn()
    {
        await EnsureTables();
        // Diana is the only one with NULL DeptId — she won't match any dept in inner join
        var result = await QueryScalarAsync(
            $"SELECT COUNT(*) FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.DeptId IS NULL");
        ((long)result!).Should().Be(0L);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases (15 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinSameTableTwice()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT a.Name AS Emp1, b.Name AS Emp2 FROM {T1} a " +
            $"INNER JOIN {T1} b ON a.DeptId = b.DeptId WHERE a.Id < b.Id ORDER BY a.Name");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Emp1"]!).Should().Be("Alice");
        ((string)rows[0]["Emp2"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_SelfJoinAllPairs()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT a.Name AS Emp1, b.Name AS Emp2 FROM {T1} a " +
            $"INNER JOIN {T1} b ON a.DeptId = b.DeptId AND a.Id <> b.Id ORDER BY a.Name, b.Name");
        // Engineering: Alice↔Charlie (2 pairs), Marketing: only Bob
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithStringKey()
    {
        await EnsureTables();
        // Join on DeptName (string column) — both from T2 self-join
        var rows = await QueryAsync(
            $"SELECT a.DeptId, b.DeptId AS BDeptId FROM {T2} a INNER JOIN {T2} b ON a.DeptName = b.DeptName ORDER BY a.DeptId");
        rows.Should().HaveCount(3); // Each dept matches itself
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_CartesianProductSize()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e CROSS JOIN {T2} d CROSS JOIN {T3} p");
        ((long)result!).Should().Be(48L); // 4*3*4
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinOnConstantTrue()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e INNER JOIN {T2} d ON TRUE");
        ((long)result!).Should().Be(12L); // Cartesian
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinOnConstantFalse()
    {
        await EnsureTables();
        var result = await QueryScalarAsync($"SELECT COUNT(*) FROM {T1} e INNER JOIN {T2} d ON FALSE");
        ((long)result!).Should().Be(0L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinSelectOnlyRightTable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id = 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["DeptName"]!).Should().Be("Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithConcatenation()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name || ' - ' || d.DeptName AS Combined FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id = 1");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Combined"]!).Should().Be("Alice - Engineering");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithCastInCondition()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON CAST(e.DeptId AS INT64) = d.DeptId ORDER BY e.Name");
        rows.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithBetween()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id BETWEEN 1 AND 2 ORDER BY e.Name");
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithLike()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName LIKE 'Eng%'");
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinProjectFromThirdTable()
    {
        await EnsureTables();
        // T3 has EmpId=5 with no match in T1
        var rows = await QueryAsync(
            $"SELECT p.Project FROM {T3} p LEFT JOIN {T1} e ON p.EmpId = e.Id WHERE e.Id IS NULL");
        rows.Should().HaveCount(1);
        ((string)rows[0]["Project"]!).Should().Be("Gamma");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithArithmetic()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, e.Id * 10 AS Score FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY Score");
        rows.Should().HaveCount(3);
        ((long)rows[0]["Score"]!).Should().Be(10L);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinWithLengthFunction()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, LENGTH(d.DeptName) AS NameLen FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id");
        rows.Should().HaveCount(3);
        ((long)rows[0]["NameLen"]!).Should().Be(11L); // "Engineering"
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task EdgeCase_JoinResultUsedInSubquery()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT * FROM (SELECT e.Name, d.DeptName FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId) sub WHERE sub.DeptName = 'Engineering' ORDER BY sub.Name");
        rows.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // Parameterized joins (10 tests)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_WhereParamOnLeftTable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id = @id",
            ("id", SpannerDbType.Int64, (object?)1L));
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_WhereParamOnRightTable()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptName = @dept ORDER BY e.Name",
            ("dept", SpannerDbType.String, (object?)"Engineering"));
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_MultipleParams()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Id >= @minId AND d.DeptName = @dept",
            ("minId", SpannerDbType.Int64, (object?)2L),
            ("dept", SpannerDbType.String, (object?)"Engineering"));
        rows.Should().HaveCount(1);
        ((string)rows[0]["Name"]!).Should().Be("Charlie");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_ParamInLimit()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT @lim",
            ("lim", SpannerDbType.Int64, (object?)2L));
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_ParamInOffset()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId ORDER BY e.Id LIMIT 10 OFFSET @off",
            ("off", SpannerDbType.Int64, (object?)1L));
        rows.Should().HaveCount(2); // Bob, Charlie
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_ParamForDeptId()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name FROM {T1} e INNER JOIN {T2} d ON e.DeptId = d.DeptId WHERE d.DeptId = @deptId ORDER BY e.Name",
            ("deptId", SpannerDbType.Int64, (object?)10L));
        rows.Should().HaveCount(2);
        ((string)rows[0]["Name"]!).Should().Be("Alice");
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_LeftJoinWithParam()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e LEFT JOIN {T2} d ON e.DeptId = d.DeptId WHERE e.Name = @name",
            ("name", SpannerDbType.String, (object?)"Diana"));
        rows.Should().HaveCount(1);
        rows[0]["DeptName"].Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_ThreeTableWithParam()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, p.Project FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"INNER JOIN {T3} p ON e.Id = p.EmpId " +
            $"WHERE p.Project = @proj ORDER BY e.Name",
            ("proj", SpannerDbType.String, (object?)"Alpha"));
        rows.Should().HaveCount(2); // Alice, Bob
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_AggregateWithParam()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT d.DeptName, COUNT(*) AS cnt FROM {T1} e " +
            $"INNER JOIN {T2} d ON e.DeptId = d.DeptId " +
            $"WHERE e.Id > @minId GROUP BY d.DeptName ORDER BY d.DeptName",
            ("minId", SpannerDbType.Int64, (object?)1L));
        rows.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, "JoinQueryExtended")]
    public async Task ParameterizedJoin_CrossJoinWithParam()
    {
        await EnsureTables();
        var rows = await QueryAsync(
            $"SELECT e.Name, d.DeptName FROM {T1} e CROSS JOIN {T2} d WHERE e.Name = @name ORDER BY d.DeptName",
            ("name", SpannerDbType.String, (object?)"Alice"));
        rows.Should().HaveCount(3);
    }
}
