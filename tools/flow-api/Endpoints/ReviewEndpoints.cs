using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId}/specs/{specId}/review-requests");

        group.MapGet("/", async (string projectId, string specId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var reviewRequests = await ((IReviewRequestStore)store).LoadBySpecAsync(specId);
            return Results.Ok(reviewRequests);
        });

        group.MapGet("/{rrId}", async (string projectId, string specId,
            string rrId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var rr = await ((IReviewRequestStore)store).LoadAsync(specId, rrId);
            return rr != null
                ? Results.Ok(rr)
                : Results.NotFound(new { error = $"review request not found: {rrId}" });
        });

        group.MapPost("/{rrId}/respond", async (string projectId, string specId,
            string rrId, SubmitReviewResponseRequest req, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var spec = await store.LoadAsync(specId);
            if (spec == null)
                return Results.NotFound(new { error = $"spec not found: {specId}" });

            // Failed 스펙 재등록은 API에서 지원하지 않음 (agent 필요)
            if (spec.State == FlowState.Failed)
                return Results.UnprocessableEntity(new
                {
                    error = "Failed spec re-registration requires the runner CLI"
                });

            if (spec.State != FlowState.Review || spec.ProcessingStatus != ProcessingStatus.UserReview)
                return Results.BadRequest(new
                {
                    error = $"spec is not awaiting review response (state: {spec.State}/{spec.ProcessingStatus})"
                });

            var rr = await ((IReviewRequestStore)store).LoadAsync(specId, rrId);
            if (rr == null || rr.Status != ReviewRequestStatus.Open)
                return Results.BadRequest(new { error = "no open review request found" });

            var responseType = req.Type?.ToLowerInvariant() switch
            {
                "approve" or "approveoption" => ReviewResponseType.ApproveOption,
                "reject" or "rejectwithcomment" => ReviewResponseType.RejectWithComment,
                _ => ReviewResponseType.ApproveOption
            };

            var response = new ReviewResponse
            {
                RespondedBy = "api-user",
                RespondedAt = DateTimeOffset.UtcNow,
                Type = responseType,
                SelectedOptionId = req.SelectedOptionId,
                Comment = req.Comment
            };

            // ReviewResponseSubmitter로 검증
            var submitter = new ReviewResponseSubmitter(store);
            var submitResult = await submitter.SubmitResponseAsync(specId, rrId, response);

            if (submitResult.Kind != SubmitResultKind.Success)
                return Results.BadRequest(new { error = "review response submission failed" });

            // 상태 전이 적용
            if (submitResult.ProposedEvent.HasValue)
            {
                var eventSubmitter = new EventSubmitter(store);
                var eventResult = await eventSubmitter.SubmitAsync(
                    specId, submitResult.ProposedEvent.Value, spec.Version);

                if (!eventResult.Success)
                    return Results.UnprocessableEntity(new { error = eventResult.Error });
            }

            // RR을 Answered로 커밋
            if (submitResult.ValidatedReviewRequest != null)
                await submitter.CommitAsync(submitResult.ValidatedReviewRequest);

            return Results.Ok(new { status = "responded", specId, reviewRequestId = rrId });
        });
    }
}
