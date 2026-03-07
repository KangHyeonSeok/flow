using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Edge case tests (EC-01 ~ EC-06).
/// Verifies handling of special characters, SQL injection, long strings,
/// unicode/emoji, and null bytes.
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_EdgeCases : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_EdgeCases(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// EC-01: Query containing '%' character.
    /// With hybrid search (Contains-based), '%' is treated as a literal character,
    /// not as a SQL LIKE wildcard.
    /// </summary>
    [Fact]
    public void Query_WithPercentWildcard_HandlesCorrectly()
    {
        using var service = _fixture.CreateService();

        var results = service.Query(query: "%");

        // Hybrid search uses String.Contains, so '%' is literal.
        // No sample records contain literal '%', so result should be empty.
        results.Should().NotBeNull();
        results.Should().BeEmpty(because: "no sample records contain literal '%'");
    }

    /// <summary>
    /// EC-02: Query containing '_' character.
    /// With hybrid search (Contains-based), '_' is treated as a literal character.
    /// </summary>
    [Fact]
    public void Query_WithUnderscoreWildcard_HandlesCorrectly()
    {
        using var service = _fixture.CreateService();

        var results = service.Query(query: "_");

        // Hybrid search uses String.Contains, so '_' is literal.
        // No sample records contain literal '_', so result should be empty.
        results.Should().NotBeNull();
        results.Should().BeEmpty(because: "no sample records contain literal '_'");
    }

    /// <summary>
    /// EC-03: SQL injection attempt via query parameter.
    /// CRITICAL: Must pass — parameterized queries should prevent injection.
    /// </summary>
    [Fact]
    public void Query_WithSqlInjectionAttempt_ReturnsSafeResult()
    {
        using var service = _fixture.CreateService();

        var malicious = "' OR '1'='1";
        var results = service.Query(query: malicious);

        // Parameterized queries should treat the entire string as a literal value.
        // The malicious string should NOT bypass the WHERE clause.
        results.Should().NotHaveCount(5,
            "because parameterized queries should prevent SQL injection; " +
            "if all 5 records are returned, the injection succeeded");
    }

    /// <summary>
    /// EC-04: Very long query string.
    /// Should not throw or crash.
    /// </summary>
    [Fact]
    public void Query_WithVeryLongString_HandlesWithoutError()
    {
        using var service = _fixture.CreateService();

        var longString = new string('a', 10000);
        var action = () => service.Query(query: longString);

        action.Should().NotThrow();
    }

    /// <summary>
    /// EC-05: Unicode emoji in query.
    /// Should not crash; may return 0 results.
    /// </summary>
    [Fact]
    public void Query_WithUnicodeEmoji_HandlesCorrectly()
    {
        using var service = _fixture.CreateService();

        var results = service.Query(query: "🔥");

        results.Should().NotBeNull();
        // No sample data contains 🔥, so expect empty results
        results.Should().BeEmpty("no sample records contain emoji 🔥");
    }

    /// <summary>
    /// EC-06: NULL byte in query.
    /// Should not crash.
    /// </summary>
    [Fact]
    public void Query_WithNullByte_HandlesWithoutCrash()
    {
        using var service = _fixture.CreateService();

        var queryWithNull = "test\0data";
        var action = () => service.Query(query: queryWithNull);

        action.Should().NotThrow();
    }
}
