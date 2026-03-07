using FlowCLI.Services;
using FlowCLI.Tests.Fixtures;
using FluentAssertions;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Hybrid search scoring tests (HS-01 ~ HS-08).
/// Verifies CalculateHybridScore, OR tag semantics, and result ranking.
/// </summary>
[Collection("DatabaseTests")]
public class HybridSearchTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HybridSearchTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// HS-01: CalculateHybridScore with distance=0 (perfect match).
    /// Expected: semanticScore=1.0, hybridScore = 1.0*0.5 + tagScore*0.1
    /// </summary>
    [Fact]
    public void HS01_CalculateHybridScore_PerfectMatch_HighScore()
    {
        float score = DatabaseService.CalculateHybridScore(
            cosineDistance: 0f, matchedTags: 2, totalQueryTags: 2);

        score.Should().BeApproximately(0.6f, 0.001f,
            because: "1.0*0.5 + 1.0*0.1 = 0.6");
    }

    /// <summary>
    /// HS-02: CalculateHybridScore with distance=1 (orthogonal).
    /// semanticScore = max(0, 1-1) = 0.
    /// </summary>
    [Fact]
    public void HS02_CalculateHybridScore_ZeroSimilarity()
    {
        float score = DatabaseService.CalculateHybridScore(
            cosineDistance: 1f, matchedTags: 1, totalQueryTags: 1);

        score.Should().BeApproximately(0.1f, 0.001f,
            because: "0*0.5 + 1.0*0.1 = 0.1 (tag only)");
    }

    /// <summary>
    /// HS-03: CalculateHybridScore with no tags.
    /// </summary>
    [Fact]
    public void HS03_CalculateHybridScore_NoTags()
    {
        float score = DatabaseService.CalculateHybridScore(
            cosineDistance: 0.4f, matchedTags: 0, totalQueryTags: 0);

        score.Should().BeApproximately(0.3f, 0.001f,
            because: "(1-0.4)*0.5 + 0 = 0.3");
    }

    /// <summary>
    /// HS-04: CalculateHybridScore with partial tag match.
    /// </summary>
    [Fact]
    public void HS04_CalculateHybridScore_PartialTagMatch()
    {
        float score = DatabaseService.CalculateHybridScore(
            cosineDistance: 0.2f, matchedTags: 1, totalQueryTags: 3);

        float expected = (1f - 0.2f) * 0.5f + (1f / 3f) * 0.1f;
        score.Should().BeApproximately(expected, 0.001f);
    }

    /// <summary>
    /// HS-05: Query with text match gives higher score than tag-only match.
    /// "CLI" matches record 1 text (0.5), "cli" tag also matches (0.1).
    /// "cli" tag-only search: record 1 tag match (0.1).
    /// </summary>
    [Fact]
    public void HS05_TextMatch_RankedHigher_ThanTagOnly()
    {
        using var service = _fixture.CreateService();

        var textResults = service.Query(query: "CLI", tags: "cli");
        var tagResults = service.Query(tags: "cli");

        // Both should return record 1
        textResults.Should().NotBeEmpty();
        tagResults.Should().NotBeEmpty();
        textResults[0].Id.Should().Be(tagResults[0].Id);
    }

    /// <summary>
    /// HS-06: Multiple tag OR condition — "cli,database" matches records 1 and 2.
    /// </summary>
    [Fact]
    public void HS06_MultipleTagsOR_MatchesMultipleRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: "cli,database");

        results.Should().HaveCount(2, because: "record 1 has 'cli', record 2 has 'database'");
        results.Should().Contain(r => r.CanonicalTags.Contains("cli"));
        results.Should().Contain(r => r.CanonicalTags.Contains("database"));
    }

    /// <summary>
    /// HS-07: Record with more matching tags ranks higher.
    /// "cli,command,interface" → record 1 matches all 3 tags.
    /// </summary>
    [Fact]
    public void HS07_MoreMatchingTags_RankedHigher()
    {
        using var service = _fixture.CreateService();

        // "cli,command,interface,database" → record 1 matches cli+command+interface (3/4),
        // record 2 matches database (1/4)
        var results = service.Query(tags: "cli,command,interface,database");

        results.Should().HaveCount(2);
        // Record 1 should rank first (3/4 match > 1/4 match)
        results[0].CanonicalTags.Should().Contain("cli");
    }

    /// <summary>
    /// HS-08: Combined text + tag search — text provides primary score, tag is bonus.
    /// </summary>
    [Fact]
    public void HS08_HybridSearch_TextAndTag_BothContribute()
    {
        using var service = _fixture.CreateService();

        // "implementation" matches record 1 text; "cli" tag also matches record 1
        var results = service.Query(query: "implementation", tags: "cli");

        results.Should().HaveCount(1);
        results[0].Content.Should().Contain("implementation");
        results[0].CanonicalTags.Should().Contain("cli");
    }
}
