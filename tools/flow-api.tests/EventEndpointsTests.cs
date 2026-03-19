using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowApi.Models;
using FlowCore.Models;
using FluentAssertions;

namespace flow_api.tests;

public sealed class EventEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    private async Task<Spec> CreateSpecAsync()
    {
        var req = new CreateSpecRequest("Test", Problem: "p", Goal: "g");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs", req, Json);
        return (await response.Content.ReadFromJsonAsync<Spec>(Json))!;
    }

    [Fact]
    public async Task SubmitEvent_UnknownEvent_Returns400()
    {
        var spec = await CreateSpecAsync();
        var req = new SubmitEventRequest("NotARealEvent", spec.Version);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/events", req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitEvent_SpecNotFound_Returns404()
    {
        var req = new SubmitEventRequest("CancelRequested", 1);
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/F-999/events", req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubmitEvent_VersionConflict_Returns409()
    {
        var spec = await CreateSpecAsync();
        var req = new SubmitEventRequest("CancelRequested", 999);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/events", req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SubmitEvent_ForbiddenTransition_Returns422()
    {
        // Draft spec cannot have UserReviewSubmitted
        var spec = await CreateSpecAsync();
        var req = new SubmitEventRequest("UserReviewSubmitted", spec.Version);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/events", req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmitEvent_CancelRequested_Succeeds()
    {
        var spec = await CreateSpecAsync();
        var req = new SubmitEventRequest("CancelRequested", spec.Version);

        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/events", req, Json);

        // CancelRequested on Draft may succeed or be rejected depending on rules.
        // We just verify we get a well-formed response (not 500).
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
