using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace flow_api.tests;

public sealed class AssignmentEndpointsTests : IAsyncLifetime
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
    public async Task ListAssignments_Empty_ReturnsEmptyArray()
    {
        var spec = await CreateSpecAsync();
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignments = await response.Content.ReadFromJsonAsync<List<Assignment>>(Json);
        assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAssignments_WithData_ReturnsList()
    {
        var spec = await CreateSpecAsync();

        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var assignment = new Assignment
        {
            Id = "A-001",
            SpecId = spec.Id,
            AgentRole = AgentRole.Developer,
            Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Queued
        };
        await ((IAssignmentStore)store).SaveAsync(assignment);

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/assignments");
        var assignments = await response.Content.ReadFromJsonAsync<List<Assignment>>(Json);

        assignments.Should().HaveCount(1);
        assignments![0].Id.Should().Be("A-001");
    }

    [Fact]
    public async Task GetAssignment_NotFound_Returns404()
    {
        var spec = await CreateSpecAsync();
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/assignments/A-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAssignment_Exists_ReturnsOk()
    {
        var spec = await CreateSpecAsync();

        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var assignment = new Assignment
        {
            Id = "A-002",
            SpecId = spec.Id,
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Completed
        };
        await ((IAssignmentStore)store).SaveAsync(assignment);

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/{spec.Id}/assignments/A-002");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<Assignment>(Json);
        result!.Type.Should().Be(AssignmentType.Planning);
    }
}
