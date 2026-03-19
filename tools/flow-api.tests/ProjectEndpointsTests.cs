using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace flow_api.tests;

public sealed class ProjectEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ListProjects_Empty_ReturnsEmptyArray()
    {
        var response = await _fixture.Client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var projects = JsonSerializer.Deserialize<string[]>(json, FlowApiFixture.JsonOptions);
        projects.Should().BeEmpty();
    }

    [Fact]
    public async Task ListProjects_WithProjects_ReturnsSorted()
    {
        // Arrange: create project dirs
        Directory.CreateDirectory(Path.Combine(_fixture.FlowHome, "projects", "beta"));
        Directory.CreateDirectory(Path.Combine(_fixture.FlowHome, "projects", "alpha"));

        var response = await _fixture.Client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var projects = JsonSerializer.Deserialize<string[]>(json, FlowApiFixture.JsonOptions);
        projects.Should().BeEquivalentTo(new[] { "alpha", "beta" },
            o => o.WithStrictOrdering());
    }
}
