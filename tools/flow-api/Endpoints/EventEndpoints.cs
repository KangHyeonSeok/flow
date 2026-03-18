using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/projects/{projectId}/specs/{specId}/events",
            async (string projectId, string specId, SubmitEventRequest req, FlowStoreFactory factory) =>
            {
                if (!Enum.TryParse<FlowEvent>(req.Event, true, out var flowEvent))
                    return Results.BadRequest(new { error = $"unknown event: {req.Event}" });

                var store = factory.GetStore(projectId);
                var submitter = new EventSubmitter(store);
                var result = await submitter.SubmitAsync(specId, flowEvent, req.Version);

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

                return Results.Ok(new { status = "accepted", currentVersion = result.CurrentVersion });
            });
    }
}
