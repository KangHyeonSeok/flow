using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Tag search tests (TG-01 ~ TG-07).
/// Verifies the tags parameter behavior of DatabaseService.Query().
/// 
/// Sample data tags (from TestDatabaseFixture):
///   1: "cli,command,interface"
///   2: "database,schema,migration"
///   3: "special,chars,edge-case"
///   4: "unicode,한글,テスト"
///   5: "UPPER,CASE,TAGS"
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_Tags : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_Tags(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// TG-01: Single tag match — "cli" matches record 1.
    /// </summary>
    [Fact]
    public void Query_WithSingleTag_ReturnsMatchingRecord()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "cli");

        results.Should().HaveCount(1, because: "only record 1 has 'cli' tag");
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>
    /// TG-02: Multiple tags (OR) — "cli,command" matches record 1 (has both).
    /// </summary>
    [Fact]
    public void Query_WithMultipleTags_OrCondition()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "cli,command");

        results.Should().HaveCount(1, because: "only record 1 matches 'cli' or 'command' tags");
        results[0].CanonicalTags.Should().Contain("cli");
        results[0].CanonicalTags.Should().Contain("command");
    }

    /// <summary>
    /// TG-03: OR condition — "cli,nonexistent": 'cli' matches record 1, 'nonexistent' matches none.
    /// Result: record 1 returned with partial tag score.
    /// </summary>
    [Fact]
    public void Query_WithPartialNonexistentTag_ReturnsPartialMatch()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "cli,nonexistent");

        results.Should().HaveCount(1, because: "OR condition: record 1 matches 'cli'");
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>
    /// TG-04: Partial tag match — "com" matches substring of "command" via LIKE.
    /// </summary>
    [Fact]
    public void Query_WithPartialTagMatch_ReturnsRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "com");

        results.Should().HaveCountGreaterThan(0, because: "'com' is substring of 'command' in record 1");
        results.Should().Contain(r => r.CanonicalTags.Contains("command"));
    }

    /// <summary>
    /// TG-05: Case-insensitive tag — "CLI" matches "cli" in record 1.
    /// SQLite LIKE is case-insensitive for ASCII by default.
    /// </summary>
    [Fact]
    public void Query_WithUpperCaseTag_ReturnsCaseInsensitiveMatch()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "CLI");

        results.Should().HaveCount(1, because: "SQLite LIKE is case-insensitive for ASCII");
        results[0].CanonicalTags.Should().ContainEquivalentOf("cli");
    }

    /// <summary>
    /// TG-06: Tags with spaces — "cli, command" should be trimmed per Split options.
    /// DatabaseService uses StringSplitOptions.TrimEntries. OR condition applies.
    /// </summary>
    [Fact]
    public void Query_WithSpacesInTags_TrimsAndMatches()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "cli, command");

        results.Should().HaveCount(1, because: "spaces are trimmed; only record 1 matches 'cli' or 'command'");
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>
    /// TG-07: Korean tag — "한글" matches record 4 tags "unicode,한글,テスト".
    /// </summary>
    [Fact]
    public void Query_WithKoreanTags_ReturnsMatchingRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "한글");

        results.Should().HaveCount(1, because: "record 4 has '한글' tag");
        results[0].CanonicalTags.Should().Contain("한글");
    }
}
