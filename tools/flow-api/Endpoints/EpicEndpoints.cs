using FlowCore.Models;
using FlowCore.Storage;

namespace FlowApi.Endpoints;

public static class EpicEndpoints
{
    public static void MapEpicEndpoints(this WebApplication app)
    {
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
    }
}
