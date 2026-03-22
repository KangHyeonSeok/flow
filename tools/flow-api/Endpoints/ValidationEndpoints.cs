using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class ValidationEndpoints
{
    public static void MapValidationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/projects/{projectId}/specs/{specId}/validate",
            async (string projectId, string specId, SubmitValidationRequest req, FlowStoreFactory factory) =>
            {
                var store = factory.GetStore(projectId);
                var spec = await store.LoadAsync(specId);
                if (spec == null)
                    return Results.NotFound(new { error = $"spec not found: {specId}" });

                var eventResult = MapValidationEvent(spec.State, spec.ProcessingStatus, req.Outcome);
                if (!eventResult.Success)
                    return Results.BadRequest(new { error = eventResult.Error });

                var submitter = new EventSubmitter(store);
                var result = await submitter.SubmitAsActorAsync(
                    specId,
                    eventResult.Event!.Value,
                    req.Version,
                    ActorKind.SpecValidator);

                if (!result.Success)
                {
                    if (result.Error?.Contains("not found") == true)
                        return Results.NotFound(new { error = result.Error });
                    if (result.Error?.Contains("version conflict") == true)
                        return Results.Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
                    if (result.RejectionReason.HasValue)
                        return Results.UnprocessableEntity(new
                        {
                            error = result.Error,
                            rejectionReason = result.RejectionReason.Value.ToString()
                        });
                    return Results.BadRequest(new { error = result.Error });
                }

                return Results.Ok(new
                {
                    status = "accepted",
                    eventName = eventResult.Event.Value.ToString(),
                    currentVersion = result.CurrentVersion
                });
            });
    }

    private static (bool Success, FlowEvent? Event, string? Error) MapValidationEvent(
        FlowState state,
        ProcessingStatus processingStatus,
        string? outcome)
    {
        var normalizedOutcome = string.IsNullOrWhiteSpace(outcome)
            ? "pass"
            : outcome.Trim().ToLowerInvariant();

        if (state == FlowState.Draft)
        {
            return normalizedOutcome switch
            {
                "pass" or "passed" => (true, FlowEvent.AcPrecheckPassed, null),
                "reject" or "rejected" or "fail" or "failed" => (true, FlowEvent.AcPrecheckRejected, null),
                _ => (false, null, "draft validation outcome must be one of: pass, reject")
            };
        }

        if (state == FlowState.Review && processingStatus == ProcessingStatus.InReview)
        {
            return normalizedOutcome switch
            {
                "pass" or "passed" => (true, FlowEvent.SpecValidationPassed, null),
                "rework" => (true, FlowEvent.SpecValidationReworkRequested, null),
                "userreview" or "user-review" or "user_review" => (true, FlowEvent.SpecValidationUserReviewRequested, null),
                "fail" or "failed" => (true, FlowEvent.SpecValidationFailed, null),
                _ => (false, null, "review validation outcome must be one of: pass, rework, userReview, fail")
            };
        }

        return (false, null, $"validation is only supported for draft specs or review specs in inReview status (current: {state}/{processingStatus})");
    }
}