using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class EpicEndpoints
{
    public static void MapEpicEndpoints(this WebApplication app)
    {
        // ── Backfill: epic.childSpecIds → spec.epicId ──

        app.MapPost("/api/projects/{projectId}/backfill-epic-ids",
            async (string projectId, FlowStoreFactory factory) =>
            {
                var epics = EpicDocumentStore.LoadAll(factory.FlowHome, projectId);
                if (epics.Count == 0)
                    return Results.Ok(new { processed = 0, updated = 0, skipped = 0, conflicts = 0 });

                // Build specId → epicId map from all epic documents
                var specToEpic = new Dictionary<string, string>();
                foreach (var epic in epics)
                {
                    foreach (var specId in epic.ChildSpecIds)
                    {
                        // First epic wins if a spec appears in multiple epics
                        specToEpic.TryAdd(specId, epic.EpicId);
                    }
                }

                var store = factory.GetStore(projectId);
                var allSpecs = await store.LoadAllAsync();

                var updated = 0;
                var skipped = 0;
                var conflicts = 0;

                foreach (var spec in allSpecs)
                {
                    if (!specToEpic.TryGetValue(spec.Id, out var epicId))
                    {
                        skipped++;
                        continue;
                    }

                    if (spec.EpicId == epicId)
                    {
                        skipped++;
                        continue;
                    }

                    spec.EpicId = epicId;
                    spec.UpdatedAt = DateTimeOffset.UtcNow;
                    var result = await store.SaveAsync(spec, spec.Version);
                    if (result.IsSuccess)
                        updated++;
                    else
                        conflicts++;
                }

                return Results.Ok(new
                {
                    processed = specToEpic.Count,
                    updated,
                    skipped,
                    conflicts
                });
            });
        app.MapGet("/api/projects/{projectId}/epics/{epicId}/view",
            async (string projectId, string epicId, FlowStoreFactory factory) =>
            {
                var epic = EpicDocumentStore.Load(factory.FlowHome, projectId, epicId);
                if (epic == null)
                    return Results.NotFound(new { error = $"epic not found: {epicId}" });

                var store = factory.GetStore(projectId);
                var allSpecs = await store.LoadAllAsync();
                var childSpecs = allSpecs.Where(s => epic.ChildSpecIds.Contains(s.Id)).ToList();

                var completed = childSpecs.Count(s => s.State == FlowState.Completed || s.State == FlowState.Active);
                var active = childSpecs.Count(s =>
                    s.State is FlowState.Implementation or FlowState.TestGeneration
                    or FlowState.ArchitectureReview or FlowState.Queued);
                var blocked = childSpecs.Count(s => s.ProcessingStatus == ProcessingStatus.OnHold);
                var total = childSpecs.Count;

                var view = new EpicView
                {
                    ProjectId = projectId,
                    EpicId = epic.EpicId,
                    Title = epic.Title,
                    Summary = epic.Summary,
                    DocumentVersion = epic.Version,
                    Priority = epic.Priority,
                    Owner = epic.Owner,
                    Milestone = epic.Milestones.FirstOrDefault()?.Title,
                    Progress = new EpicProgress
                    {
                        TotalSpecs = total,
                        CompletedSpecs = completed,
                        ActiveSpecs = active,
                        BlockedSpecs = blocked,
                        CompletionRatio = total > 0 ? (double)completed / total : 0
                    },
                    Narrative = new EpicNarrative
                    {
                        Problem = epic.Problem,
                        Goal = epic.Goal,
                        Scope = epic.Scope,
                        NonGoals = epic.NonGoals,
                        SuccessCriteria = epic.SuccessCriteria
                    },
                    ChildSpecs = childSpecs.Select(s => new EpicChildSpec
                    {
                        SpecId = s.Id,
                        Title = s.Title,
                        State = s.State,
                        ProcessingStatus = s.ProcessingStatus,
                        RiskLevel = s.RiskLevel,
                        LastActivityAt = s.UpdatedAt
                    }).OrderBy(s => s.SpecId).ToList(),
                    EpicDependsOn = epic.Dependencies,
                    RelatedDocs = epic.RelatedDocs
                };

                return Results.Ok(view);
            });

        app.MapGet("/api/projects/{projectId}/epics/{epicId}/document",
            (string projectId, string epicId, FlowStoreFactory factory) =>
            {
                var epic = EpicDocumentStore.Load(factory.FlowHome, projectId, epicId);
                return epic != null
                    ? Results.Ok(epic)
                    : Results.NotFound(new { error = $"epic document not found: {epicId}" });
            });

        app.MapGet("/api/projects/{projectId}/epics/{epicId}/specs",
            async (string projectId, string epicId, FlowStoreFactory factory) =>
            {
                var epic = EpicDocumentStore.Load(factory.FlowHome, projectId, epicId);
                if (epic == null)
                    return Results.NotFound(new { error = $"epic not found: {epicId}" });

                var store = factory.GetStore(projectId);
                var allSpecs = await store.LoadAllAsync();
                var childSpecs = allSpecs
                    .Where(s => epic.ChildSpecIds.Contains(s.Id))
                    .OrderBy(s => s.Id)
                    .ToList();

                return Results.Ok(childSpecs);
            });

        app.MapPut("/api/projects/{projectId}/epics/{epicId}/document",
            (string projectId, string epicId, UpdateEpicDocumentRequest req, FlowStoreFactory factory) =>
            {
                var existing = EpicDocumentStore.Load(factory.FlowHome, projectId, epicId);
                if (existing == null)
                    return Results.NotFound(new { error = $"epic document not found: {epicId}" });

                // Apply partial updates
                if (req.Title != null) existing.Title = req.Title;
                if (req.Summary != null) existing.Summary = req.Summary;
                if (req.Problem != null) existing.Problem = req.Problem;
                if (req.Goal != null) existing.Goal = req.Goal;
                if (req.Scope != null) existing.Scope = req.Scope;
                if (req.NonGoals != null) existing.NonGoals = req.NonGoals;
                if (req.SuccessCriteria != null) existing.SuccessCriteria = req.SuccessCriteria;
                if (req.ChildSpecIds != null) existing.ChildSpecIds = req.ChildSpecIds;
                if (req.Dependencies != null) existing.Dependencies = req.Dependencies;
                if (req.RelatedDocs != null) existing.RelatedDocs = req.RelatedDocs;
                if (req.Owner != null) existing.Owner = req.Owner;
                if (req.Priority != null) existing.Priority = req.Priority;

                var (ok, error) = EpicDocumentStore.Save(factory.FlowHome, projectId, epicId, existing, req.Version);
                if (!ok)
                    return Results.Conflict(new { error });

                return Results.Ok(existing);
            });
    }
}
