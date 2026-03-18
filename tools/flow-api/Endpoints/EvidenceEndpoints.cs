using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class EvidenceEndpoints
{
    public static void MapEvidenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId}/specs/{specId}/evidence");

        group.MapGet("/", async (string projectId, string specId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var manifests = await ((IEvidenceStore)store).LoadBySpecAsync(specId);
            return Results.Ok(manifests);
        });

        group.MapGet("/{runId}", async (string projectId, string specId,
            string runId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var manifest = await ((IEvidenceStore)store).LoadManifestAsync(specId, runId);
            return manifest != null
                ? Results.Ok(manifest)
                : Results.NotFound(new { error = $"evidence not found for run: {runId}" });
        });
    }
}
