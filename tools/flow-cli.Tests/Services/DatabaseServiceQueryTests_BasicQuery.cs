using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Basic query text search tests (BQ-01 ~ BQ-08).
/// Verifies the query parameter behavior of DatabaseService.Query().
/// 
/// Sample data (from TestDatabaseFixture):
///   1: "CLI command implementation"
///   2: "Database schema migration"
///   3: "Special chars: &lt;tag&gt; &amp; \"quotes\" 'apostrophe'"
///   4: "유니코드 테스트 문서"
///   5: "UPPERCASE CONTENT TEST"
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_BasicQuery : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_BasicQuery(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// BQ-01: Exact word match — "command" matches record 1.
    /// </summary>
    [Fact]
    public void Query_WithExactWord_ReturnsMatchingRecord()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "command");

        results.Should().HaveCount(1, because: "only record 1 contains 'command'");
        results[0].Content.Should().Contain("command");
    }

    /// <summary>
    /// BQ-02: Partial match — "comm" matches record 1 via LIKE '%comm%'.
    /// </summary>
    [Fact]
    public void Query_WithPartialMatch_ReturnsMatchingRecord()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "comm");

        results.Should().HaveCount(1, because: "'comm' is a substring of 'command' in record 1");
        results[0].Content.Should().Contain("comm");
    }

    /// <summary>
    /// BQ-03: Multiple words — "schema migration" matches record 2.
    /// </summary>
    [Fact]
    public void Query_WithMultipleWords_ReturnsMatchingRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "schema migration");

        results.Should().HaveCount(1, because: "record 2 contains 'schema migration'");
        results[0].Content.Should().Contain("schema");
        results[0].Content.Should().Contain("migration");
    }

    /// <summary>
    /// BQ-04: Case-insensitive — "COMMAND" should match "command" in record 1.
    /// SQLite LIKE is case-insensitive for ASCII characters by default.
    /// </summary>
    [Fact]
    public void Query_WithUpperCase_ReturnsCaseInsensitiveMatch()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "COMMAND");

        results.Should().HaveCount(1, because: "SQLite LIKE is case-insensitive for ASCII");
        results[0].Content.Should().ContainEquivalentOf("command");
    }

    /// <summary>
    /// BQ-05: No match — "nonexistent" returns empty.
    /// </summary>
    [Fact]
    public void Query_WithNonexistentText_ReturnsEmpty()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "nonexistent");

        results.Should().BeEmpty(because: "no sample record contains 'nonexistent'");
    }

    /// <summary>
    /// BQ-06: Empty string — should not be treated as LIKE condition.
    /// string.IsNullOrEmpty("") returns true, so no WHERE clause is added.
    /// </summary>
    [Fact]
    public void Query_WithEmptyString_ReturnsAllRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "", top: 10);

        results.Should().HaveCount(5, because: "empty string is treated as no filter");
    }

    /// <summary>
    /// BQ-07: null query — same as empty, returns all records.
    /// </summary>
    [Fact]
    public void Query_WithNull_ReturnsAllRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: null, top: 10);

        results.Should().HaveCount(5, because: "null query means no filter");
    }

    /// <summary>
    /// BQ-08: Whitespace-only query.
    /// Current behavior: "   " is NOT null/empty, so LIKE '%   %' is applied.
    /// This searches for content containing 3 consecutive spaces — unlikely match.
    /// Documents actual behavior (no trim logic in DatabaseService).
    /// </summary>
    [Fact]
    public void Query_WithWhitespace_DocumentsBehavior()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "   ");

        // Current behavior: whitespace is NOT trimmed, LIKE '%   %' matches nothing.
        // Note: If trim behavior is desired, DatabaseService.Query needs modification.
        results.Should().BeEmpty(
            because: "whitespace is not trimmed; LIKE '%   %' matches no sample records. " +
                     "Consider adding query.Trim() in DatabaseService if this is undesirable.");
    }
}
