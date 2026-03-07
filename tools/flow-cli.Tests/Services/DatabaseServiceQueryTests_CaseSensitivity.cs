using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Case sensitivity tests (CS-01 ~ CS-03).
/// Verifies SQLite LIKE case-insensitive behavior for ASCII characters.
/// 
/// Sample data:
///   1: Content="CLI command implementation", Tags="cli,command,interface"
///   5: Content="UPPERCASE CONTENT TEST", Tags="UPPER,CASE,TAGS"
///   4: Content="유니코드 테스트 문서", Tags="unicode,한글,テスト"
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_CaseSensitivity : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_CaseSensitivity(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// CS-01: ASCII content search should be case-insensitive.
    /// SQLite LIKE is case-insensitive for ASCII letters by default.
    /// </summary>
    [Theory]
    [InlineData("command", 1)]
    [InlineData("COMMAND", 1)]
    [InlineData("Command", 1)]
    [InlineData("CoMmAnD", 1)]
    public void Query_CaseInsensitive_ASCII_Content(string query, int expectedCount)
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: query);

        results.Should().HaveCount(expectedCount,
            because: $"SQLite LIKE should match '{query}' case-insensitively");
        results[0].Content.Should().ContainEquivalentOf("command");
    }

    /// <summary>
    /// CS-02: Korean text has no case distinction — search should match exactly.
    /// </summary>
    [Fact]
    public void Query_Korean_Content_MatchesExactly()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(query: "유니코드");

        results.Should().HaveCount(1, because: "record 4 contains '유니코드'");
        results[0].Content.Should().Contain("유니코드");
    }

    /// <summary>
    /// CS-03: ASCII tag search should be case-insensitive.
    /// "cli", "CLI", "Cli" should all match record 1's "cli" tag.
    /// </summary>
    [Theory]
    [InlineData("cli", 1)]
    [InlineData("CLI", 1)]
    [InlineData("Cli", 1)]
    public void Query_CaseInsensitive_ASCII_Tags(string tag, int expectedCount)
    {
        using var service = _fixture.CreateService();
        var results = service.Query(tags: tag);

        results.Should().HaveCount(expectedCount,
            because: $"tag '{tag}' should match case-insensitively");
    }
}
