using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowApi.Models;
using FlowCore.Models;
using FluentAssertions;

namespace flow_api.tests;

public sealed class SpecEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    private async Task<Spec> CreateSpecAsync(string title = "Test spec",
        string? type = null, List<AcRequest>? ac = null)
    {
        var req = new CreateSpecRequest(title, type, "problem", "goal", ac);
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs", req, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Spec>(Json))!;
    }

    // --- Create ---

    [Fact]
    public async Task CreateSpec_ReturnsCreated_WithAutoId()
    {
        var req = new CreateSpecRequest("My Feature", Problem: "p", Goal: "g");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs", req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spec = await response.Content.ReadFromJsonAsync<Spec>(Json);

        spec!.Id.Should().Be("F-001");
        spec.Title.Should().Be("My Feature");
        spec.State.Should().Be(FlowState.Draft);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
        spec.Version.Should().Be(1);
    }

    [Fact]
    public async Task CreateSpec_SequentialIds()
    {
        await CreateSpecAsync("First");
        var second = await CreateSpecAsync("Second");

        second.Id.Should().Be("F-002");
    }

    [Fact]
    public async Task CreateSpec_WithAcceptanceCriteria()
    {
        var ac = new List<AcRequest>
        {
            new("AC one"),
            new("AC two", Testable: false, Notes: "note")
        };
        var spec = await CreateSpecAsync("With AC", ac: ac);

        spec.AcceptanceCriteria.Should().HaveCount(2);
        spec.AcceptanceCriteria[0].Id.Should().Be("AC-001");
        spec.AcceptanceCriteria[0].Text.Should().Be("AC one");
        spec.AcceptanceCriteria[1].Testable.Should().BeFalse();
        spec.AcceptanceCriteria[1].Notes.Should().Be("note");
    }

    [Fact]
    public async Task CreateSpec_WithTypeAndRisk()
    {
        var req = new CreateSpecRequest("Task", Type: "task", RiskLevel: "high");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs", req, Json);
        var spec = await response.Content.ReadFromJsonAsync<Spec>(Json);

        spec!.Type.Should().Be(SpecType.Task);
        spec.RiskLevel.Should().Be(RiskLevel.High);
    }

    // --- Get ---

    [Fact]
    public async Task GetSpec_Exists_ReturnsOk()
    {
        var created = await CreateSpecAsync("Get me");
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec = await response.Content.ReadFromJsonAsync<Spec>(Json);
        spec!.Title.Should().Be("Get me");
    }

    [Fact]
    public async Task GetSpec_NotFound_Returns404()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- List ---

    [Fact]
    public async Task ListSpecs_ReturnsAll()
    {
        await CreateSpecAsync("One");
        await CreateSpecAsync("Two");

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs");
        var specs = await response.Content.ReadFromJsonAsync<List<Spec>>(Json);

        specs.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ListSpecs_FilterByState()
    {
        await CreateSpecAsync("Draft spec");

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs?state=draft");
        var specs = await response.Content.ReadFromJsonAsync<List<Spec>>(Json);

        specs!.Should().OnlyContain(s => s.State == FlowState.Draft);
    }

    [Fact]
    public async Task ListSpecs_FilterByStatus()
    {
        await CreateSpecAsync("Pending spec");

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs?status=pending");
        var specs = await response.Content.ReadFromJsonAsync<List<Spec>>(Json);

        specs!.Should().OnlyContain(s => s.ProcessingStatus == ProcessingStatus.Pending);
    }

    // --- Update (PATCH) ---

    [Fact]
    public async Task UpdateSpec_ChangesTitle()
    {
        var spec = await CreateSpecAsync("Original");
        var updateReq = new UpdateSpecRequest(Version: spec.Version, Title: "Updated");

        var response = await Client.PatchAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}", updateReq, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<Spec>(Json);
        updated!.Title.Should().Be("Updated");
        updated.Version.Should().Be(spec.Version); // version unchanged (SpecEditor does not bump)
    }

    [Fact]
    public async Task UpdateSpec_VersionConflict_Returns409()
    {
        var spec = await CreateSpecAsync("Original");
        var updateReq = new UpdateSpecRequest(Version: 999, Title: "Bad");

        var response = await Client.PatchAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}", updateReq, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateSpec_NotFound_Returns404()
    {
        var updateReq = new UpdateSpecRequest(Version: 1, Title: "X");
        var response = await Client.PatchAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/F-999", updateReq, Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteSpec_ReturnsNoContent()
    {
        var spec = await CreateSpecAsync("To delete");
        var response = await Client.DeleteAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var getResponse = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSpec_NotFound_Returns404()
    {
        var response = await Client.DeleteAsync(
            $"/api/projects/{ProjectId}/specs/F-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
