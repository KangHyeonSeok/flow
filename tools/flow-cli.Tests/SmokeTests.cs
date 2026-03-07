using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests;

/// <summary>
/// Smoke tests to verify test infrastructure is working correctly.
/// </summary>
public class SmokeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SmokeTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CanCreateService_ReturnsNonNull()
    {
        using var service = _fixture.CreateService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void SampleRecords_HasFiveItems()
    {
        _fixture.SampleRecords.Should().HaveCount(5);
    }

    [Fact]
    public void Query_NoFilter_ReturnsSampleRecords()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 10);
        results.Should().HaveCount(5);
    }

    [Fact]
    public void TestDbPath_FileExists()
    {
        File.Exists(_fixture.TestDbPath).Should().BeTrue();
    }
}
