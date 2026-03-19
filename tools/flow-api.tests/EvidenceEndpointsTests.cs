using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace flow_api.tests;

public sealed class EvidenceEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    [Fact]
    public async Task ListEvidence_Empty_ReturnsEmptyArray()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/evidence");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifests = await response.Content.ReadFromJsonAsync<List<EvidenceManifest>>(Json);
        manifests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEvidence_NotFound_Returns404()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/evidence/run-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvidence_Exists_ReturnsOk()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var manifest = new EvidenceManifest
        {
            SpecId = "F-001",
            RunId = "run-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Refs = new List<EvidenceRef>
            {
                new()
                {
                    Kind = "build-log",
                    RelativePath = "build.log",
                    Summary = "Build output"
                }
            }
        };
        await ((IEvidenceStore)store).SaveManifestAsync(manifest);

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/evidence/run-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<EvidenceManifest>(Json);
        result!.RunId.Should().Be("run-001");
        result.Refs.Should().HaveCount(1);
    }
}
