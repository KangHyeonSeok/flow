using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Pagination tests (PG-01 ~ PG-05).
/// Verifies the top parameter and result ordering of DatabaseService.Query().
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_Pagination : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_Pagination(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// PG-01: top=0 — SQLite LIMIT 0 returns 0 rows.
    /// </summary>
    [Fact]
    public void Query_WithTopZero_ReturnsEmpty()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 0);

        results.Should().BeEmpty(because: "LIMIT 0 returns no rows");
    }

    /// <summary>
    /// PG-02: top=1 — returns exactly 1 record.
    /// </summary>
    [Fact]
    public void Query_WithTopOne_ReturnsSingleRecord()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 1);

        results.Should().HaveCount(1, because: "LIMIT 1 returns at most 1 row");
    }

    /// <summary>
    /// PG-03: top=1000 — more than available data, returns all 5 records.
    /// </summary>
    [Fact]
    public void Query_WithTopExceedingData_ReturnsAllRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 1000);

        results.Should().HaveCount(5, because: "only 5 sample records exist");
    }

    /// <summary>
    /// PG-04: top=-1 — In SQLite, LIMIT -1 means no limit (returns all rows).
    /// Documents actual SQLite behavior.
    /// </summary>
    [Fact]
    public void Query_WithNegativeTop_DocumentsBehavior()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: -1);

        // SQLite: LIMIT -1 means "no limit", returning all rows.
        results.Should().HaveCount(5,
            because: "SQLite LIMIT -1 returns all rows (no limit)");
    }

    /// <summary>
    /// PG-05: Results should be ordered by created_at DESC.
    /// Note: Sample records may have identical created_at (inserted in same second),
    /// so we verify non-ascending order (ties allowed).
    /// </summary>
    [Fact]
    public void Query_OrderedByCreatedAtDesc()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 10);

        results.Should().HaveCount(5);

        // Verify created_at is in non-ascending order (DESC, ties allowed)
        for (int i = 0; i < results.Count - 1; i++)
        {
            var current = DateTime.Parse(results[i].CreatedAt);
            var next = DateTime.Parse(results[i + 1].CreatedAt);
            current.Should().BeOnOrAfter(next,
                because: "results should be ordered by created_at DESC at index {0}", i);
        }
    }
}
