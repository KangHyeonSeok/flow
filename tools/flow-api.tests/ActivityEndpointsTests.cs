using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace flow_api.tests;

public sealed class ActivityEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    private ActivityEvent MakeEvent(string specId, int index = 0) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        SpecId = specId,
        Action = ActivityAction.DraftCreated,
        Actor = "test",
        SourceType = "test",
        BaseVersion = 1,
        State = FlowState.Draft,
        ProcessingStatus = ProcessingStatus.Pending,
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(index),
        Message = $"event {index}"
    };

    [Fact]
    public async Task GetActivity_Empty_ReturnsEmptyArray()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var activity = await response.Content.ReadFromJsonAsync<List<ActivityEvent>>(Json);
        activity.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivity_WithData_ReturnsEvents()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        await ((IActivityStore)store).AppendAsync(MakeEvent("F-001"));

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/activity");
        var activity = await response.Content.ReadFromJsonAsync<List<ActivityEvent>>(Json);

        activity.Should().HaveCount(1);
        activity![0].Action.Should().Be(ActivityAction.DraftCreated);
    }

    [Fact]
    public async Task GetActivity_CountParam_LimitsResults()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);

        for (int i = 0; i < 5; i++)
            await ((IActivityStore)store).AppendAsync(MakeEvent("F-002", i));

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-002/activity?count=3");
        var activity = await response.Content.ReadFromJsonAsync<List<ActivityEvent>>(Json);

        activity.Should().HaveCount(3);
    }
}
