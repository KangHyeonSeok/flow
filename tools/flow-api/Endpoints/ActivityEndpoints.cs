using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/projects/{projectId}/specs/{specId}/activity",
            async (string projectId, string specId, FlowStoreFactory factory, int? count) =>
            {
                var store = factory.GetStore(projectId);
                var maxCount = Math.Clamp(count ?? 50, 1, 200);
                var activity = await ((IActivityStore)store).LoadRecentAsync(specId, maxCount);
                return Results.Ok(activity);
            });
    }
}
