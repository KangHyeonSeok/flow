using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class AssignmentEndpoints
{
    public static void MapAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId}/specs/{specId}/assignments");

        group.MapGet("/", async (string projectId, string specId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var assignments = await ((IAssignmentStore)store).LoadBySpecAsync(specId);
            return Results.Ok(assignments);
        });

        group.MapGet("/{assignmentId}", async (string projectId, string specId,
            string assignmentId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var assignment = await ((IAssignmentStore)store).LoadAsync(specId, assignmentId);
            return assignment != null
                ? Results.Ok(assignment)
                : Results.NotFound(new { error = $"assignment not found: {assignmentId}" });
        });
    }
}
