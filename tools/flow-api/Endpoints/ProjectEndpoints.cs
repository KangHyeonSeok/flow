using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        app.MapGet("/api/projects", (FlowStoreFactory factory) =>
        {
            var projects = factory.ListProjects();
            return Results.Ok(projects);
        });
    }
}
