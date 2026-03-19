using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace flow_api.tests;

public sealed class ReviewEndpointsTests : IAsyncLifetime
{
    private readonly FlowApiFixture _fixture = new();
    private const string ProjectId = "test-proj";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private HttpClient Client => _fixture.Client;
    private static JsonSerializerOptions Json => FlowApiFixture.JsonOptions;

    [Fact]
    public async Task ListReviewRequests_Empty_ReturnsEmptyArray()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/review-requests");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rrs = await response.Content.ReadFromJsonAsync<List<ReviewRequest>>(Json);
        rrs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReviewRequest_NotFound_Returns404()
    {
        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/review-requests/RR-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReviewRequest_Exists_ReturnsOk()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var rr = new ReviewRequest
        {
            Id = "RR-001",
            SpecId = "F-001",
            Status = ReviewRequestStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = "Need approval",
            Summary = "Please review",
            Options = new List<ReviewRequestOption>
            {
                new() { Id = "opt-1", Label = "Yes" }
            }
        };
        await ((IReviewRequestStore)store).SaveAsync(rr);

        var response = await Client.GetAsync(
            $"/api/projects/{ProjectId}/specs/F-001/review-requests/RR-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReviewRequest>(Json);
        result!.Id.Should().Be("RR-001");
        result.Summary.Should().Be("Please review");
    }

    [Fact]
    public async Task RespondToReview_SpecNotFound_Returns404()
    {
        var req = new SubmitReviewResponseRequest("approve");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/F-999/review-requests/RR-001/respond",
            req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToReview_SpecNotInReview_Returns400()
    {
        // Create a spec in Draft state
        var createReq = new CreateSpecRequest("Test");
        var createResponse = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs", createReq, Json);
        var spec = await createResponse.Content.ReadFromJsonAsync<Spec>(Json);

        var req = new SubmitReviewResponseRequest("approve");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/{spec!.Id}/review-requests/RR-001/respond",
            req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RespondToReview_FailedSpec_Returns422()
    {
        var factory = new FlowStoreFactory(_fixture.FlowHome);
        var store = factory.GetStore(ProjectId);
        var spec = new Spec
        {
            Id = "F-FAIL",
            ProjectId = ProjectId,
            Title = "Failed",
            State = FlowState.Failed,
            ProcessingStatus = ProcessingStatus.UserReview,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(spec, 0);

        var req = new SubmitReviewResponseRequest("approve");
        var response = await Client.PostAsJsonAsync(
            $"/api/projects/{ProjectId}/specs/F-FAIL/review-requests/RR-001/respond",
            req, Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
