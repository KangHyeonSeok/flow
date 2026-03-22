using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace flow_api.tests;

public sealed class ValidationEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    [Fact]
    public async Task ValidateDraft_Pass_MovesSpecToQueued()
    {
        var createResponse = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs",
            new CreateSpecRequest("Validation target"),
            Json);
        createResponse.EnsureSuccessStatusCode();
        var spec = (await createResponse.Content.ReadFromJsonAsync<Spec>(Json))!;

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/validate",
            new SubmitValidationRequest(spec.Version),
            Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await Client.GetFromJsonAsync<Spec>(
            $"/api/projects/{ProjectId}/specs/{spec.Id}",
            Json);

        updated!.State.Should().Be(FlowState.Queued);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
        updated.Version.Should().Be(spec.Version + 1);
    }

    [Fact]
    public async Task ValidateReview_Pass_MovesSpecToActive()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var spec = new Spec
        {
            Id = "F-REVIEW",
            ProjectId = ProjectId,
            Title = "Ready for validation",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            Version = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(spec, 0);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/validate",
            new SubmitValidationRequest(spec.Version, "pass"),
            Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await Client.GetFromJsonAsync<Spec>(
            $"/api/projects/{ProjectId}/specs/{spec.Id}",
            Json);

        updated!.State.Should().Be(FlowState.Active);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Done);
        updated.Version.Should().Be(spec.Version + 1);
    }

    [Fact]
    public async Task ValidateReview_Rework_MovesSpecBackToImplementation()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var spec = new Spec
        {
            Id = "F-REWORK",
            ProjectId = ProjectId,
            Title = "Needs rework",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            Version = 7,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(spec, 0);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/validate",
            new SubmitValidationRequest(spec.Version, "rework"),
            Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await Client.GetFromJsonAsync<Spec>(
            $"/api/projects/{ProjectId}/specs/{spec.Id}",
            Json);

        updated!.State.Should().Be(FlowState.Implementation);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
        updated.Version.Should().Be(spec.Version + 1);
    }
}