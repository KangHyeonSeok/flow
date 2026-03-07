using FlowCLI.Services;
using FlowCLI.Tests.Fixtures;
using FluentAssertions;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Tests for EmbeddingBridge integration in DatabaseService.
/// Since embed.exe is not available in test environment, these tests
/// verify graceful fallback behavior and API contract.
/// </summary>
public class EmbeddingIntegrationTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly DatabaseService _service;

    public EmbeddingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _service = _fixture.CreateService();
    }

    /// <summary>
    /// EI-01: DatabaseService should initialize successfully with EmbeddingBridge.
    /// </summary>
    [Fact]
    public void EI01_DatabaseService_Initializes_WithEmbeddingBridge()
    {
        // The service is already constructed — verify it works normally
        var results = _service.Query(top: 5);
        results.Should().NotBeNull();
        results.Should().HaveCountGreaterThan(0);
    }

    /// <summary>
    /// EI-02: GetEmbeddingAsync returns null for null/whitespace text.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EI02_GetEmbeddingAsync_NullOrWhitespace_ReturnsNull(string? text)
    {
        var result = await _service.GetEmbeddingAsync(text!);
        result.Should().BeNull();
    }

    /// <summary>
    /// EI-03: GetEmbeddingAsync returns null gracefully when embed.exe is not available.
    /// </summary>
    [Fact]
    public async Task EI03_GetEmbeddingAsync_NoEmbedExe_ReturnsNull()
    {
        var result = await _service.GetEmbeddingAsync("some test text");
        result.Should().BeNull("embed.exe is not available in test environment");
    }

    /// <summary>
    /// EI-04: DetectEmbeddingDimensionAsync throws when embed.exe is not available.
    /// </summary>
    [Fact]
    public async Task EI04_DetectEmbeddingDimension_NoEmbedExe_Throws()
    {
        var act = () => _service.DetectEmbeddingDimensionAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*embed.exe*");
    }

    /// <summary>
    /// EI-05: DefaultEmbeddingDimension constant is 1024 (BGE-M3).
    /// </summary>
    [Fact]
    public void EI05_DefaultEmbeddingDimension_Is1024()
    {
        DatabaseService.DefaultEmbeddingDimension.Should().Be(1024);
    }

    /// <summary>
    /// EI-06: Query still works normally even when embedding bridge cannot connect.
    /// </summary>
    [Fact]
    public void EI06_Query_StillWorks_WithoutEmbeddings()
    {
        var results = _service.Query(query: "CLI", top: 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Content.Contains("CLI"));
    }

    /// <summary>
    /// EI-07: AddDocument still works when embedding is unavailable.
    /// </summary>
    [Fact]
    public void EI07_AddDocument_StillWorks_WithoutEmbeddings()
    {
        var record = new FlowCLI.Models.TaskRecord
        {
            Content = "Test embedding integration record",
            CanonicalTags = "test,embedding",
            FeatureName = "ei_test",
            CommitId = "test123",
            StateAtCreation = "EXECUTING",
            Metadata = "{}",
            PlanText = "",
            ResultText = ""
        };

        var id = _service.AddDocument(record);
        id.Should().BeGreaterThan(0);

        // Verify it was saved
        var results = _service.Query(query: "embedding integration record");
        results.Should().ContainSingle(r => r.Content.Contains("embedding integration record"));
    }

    /// <summary>
    /// EI-08: GetEmbeddingAsync handles long text without crashing.
    /// </summary>
    [Fact]
    public async Task EI08_GetEmbeddingAsync_LongText_HandledGracefully()
    {
        var longText = new string('A', 10000);
        var result = await _service.GetEmbeddingAsync(longText);
        // Should return null since embed.exe is not available, but should NOT throw
        result.Should().BeNull();
    }

    public void Dispose() => _service.Dispose();
}
