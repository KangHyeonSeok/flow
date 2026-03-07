using FlowCLI.Services;
using FlowCLI.Tests.Fixtures;
using FluentAssertions;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Tag OR condition edge case tests (TO-01 ~ TO-10).
/// Verifies OR semantics, deduplication, edge cases, and CountMatchedTags.
/// </summary>
[Collection("DatabaseTests")]
public class TagOrConditionTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TagOrConditionTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // --- CountMatchedTags unit tests ---

    /// <summary>TO-01: CountMatchedTags basic — exact tag matches.</summary>
    [Theory]
    [InlineData("cli,command,interface", new[] { "cli" }, 1)]
    [InlineData("cli,command,interface", new[] { "cli", "command" }, 2)]
    [InlineData("cli,command,interface", new[] { "cli", "command", "interface" }, 3)]
    [InlineData("cli,command,interface", new[] { "nonexistent" }, 0)]
    public void TO01_CountMatchedTags_BasicMatching(string tags, string[] queryTags, int expected)
    {
        var count = DatabaseService.CountMatchedTags(tags, queryTags);
        count.Should().Be(expected);
    }

    /// <summary>TO-02: CountMatchedTags — case insensitive.</summary>
    [Theory]
    [InlineData("cli,command", new[] { "CLI" }, 1)]
    [InlineData("CLI,COMMAND", new[] { "cli" }, 1)]
    [InlineData("Cli,Command", new[] { "CLI", "COMMAND" }, 2)]
    public void TO02_CountMatchedTags_CaseInsensitive(string tags, string[] queryTags, int expected)
    {
        var count = DatabaseService.CountMatchedTags(tags, queryTags);
        count.Should().Be(expected);
    }

    /// <summary>TO-03: CountMatchedTags — partial/substring matching.</summary>
    [Theory]
    [InlineData("cli,command,interface", new[] { "com" }, 1)]     // "com" in "command"
    [InlineData("cli,command,interface", new[] { "face" }, 1)]    // "face" in "interface"
    [InlineData("cli,command,interface", new[] { "xyz" }, 0)]
    public void TO03_CountMatchedTags_SubstringMatch(string tags, string[] queryTags, int expected)
    {
        var count = DatabaseService.CountMatchedTags(tags, queryTags);
        count.Should().Be(expected);
    }

    /// <summary>TO-04: CountMatchedTags with null/empty queryTags.</summary>
    [Fact]
    public void TO04_CountMatchedTags_NullOrEmpty_ReturnsZero()
    {
        DatabaseService.CountMatchedTags("cli,command", null!).Should().Be(0);
        DatabaseService.CountMatchedTags("cli,command", []).Should().Be(0);
    }

    /// <summary>TO-05: CountMatchedTags with empty canonical tags.</summary>
    [Fact]
    public void TO05_CountMatchedTags_EmptyCanonicalTags_ReturnsZero()
    {
        DatabaseService.CountMatchedTags("", new[] { "cli" }).Should().Be(0);
    }

    // --- Integration tests ---

    /// <summary>TO-06: Duplicate tags in query — deduplicated before matching.</summary>
    [Fact]
    public void TO06_Query_DuplicateTags_Deduplicated()
    {
        using var service = _fixture.CreateService();
        // "cli,cli,CLI" should be deduplicated to ["cli"] (case-insensitive distinct)
        var results = service.Query(tags: "cli,cli,CLI");

        results.Should().HaveCount(1, because: "deduplication: only unique tags counted");
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>TO-07: Empty tags between commas — filtered by RemoveEmptyEntries.</summary>
    [Fact]
    public void TO07_Query_EmptyTagsBetweenCommas_Filtered()
    {
        using var service = _fixture.CreateService();
        // "cli,,command" → ["cli", "command"] after RemoveEmptyEntries
        var results = service.Query(tags: "cli,,command");

        results.Should().HaveCount(1);
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>TO-08: Tags with only commas/spaces — treated as no tags.</summary>
    [Fact]
    public void TO08_Query_OnlyCommasAndSpaces_ReturnsAll()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: ", , ,");

        // After split + RemoveEmptyEntries + TrimEntries → empty array → no tags → all records
        results.Should().HaveCount(5, because: "empty tags are filtered out, no tag filter applied");
    }

    /// <summary>TO-09: Korean tags with OR condition.</summary>
    [Fact]
    public void TO09_Query_KoreanTagsOR_MatchesCorrectly()
    {
        using var service = _fixture.CreateService();
        // "한글,cli" → record 4 (한글) OR record 1 (cli) → 2 results
        var results = service.Query(tags: "한글,cli");

        results.Should().HaveCount(2, because: "record 4 matches '한글', record 1 matches 'cli'");
    }

    /// <summary>TO-10: All tags match no records — returns empty.</summary>
    [Fact]
    public void TO10_Query_NoMatchingTags_ReturnsEmpty()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "nonexistent,nope,nada");

        results.Should().BeEmpty(because: "no records match any of the tags");
    }
}
