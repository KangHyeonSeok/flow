using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Combined query + tags tests (CB-01 ~ CB-03).
/// Verifies hybrid scoring when both query and tags are specified.
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_Combined : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_Combined(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// CB-01: Both query and tags match the same record.
    /// Expected: Returns the matching record.
    /// </summary>
    [Fact]
    public void Query_WithQueryAndTags_BothMatch_ReturnsRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "implementation", tags: "cli");

        results.Should().HaveCount(1);
        results[0].Content.Should().Contain("implementation");
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>
    /// CB-02: Query matches but tags don't.
    /// Hybrid scoring: text match contributes score even when tags miss.
    /// </summary>
    [Fact]
    public void Query_WithQueryAndTags_OnlyQueryMatches_ReturnsTextMatch()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "command", tags: "nonexistent");

        results.Should().NotBeEmpty("because hybrid scoring: text match provides score");
        results[0].Content.Should().ContainEquivalentOf("command");
    }

    /// <summary>
    /// CB-03: Tags match but query doesn't.
    /// Hybrid scoring: tag match contributes score even when query misses.
    /// </summary>
    [Fact]
    public void Query_WithQueryAndTags_OnlyTagsMatch_ReturnsTagMatch()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "nonexistent", tags: "cli");

        results.Should().NotBeEmpty("because hybrid scoring: tag match provides score");
        results[0].CanonicalTags.Should().Contain("cli");
    }
}
