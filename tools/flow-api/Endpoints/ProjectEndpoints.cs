using FlowCore.Models;
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

        app.MapGet("/api/projects/{projectId}/view", async (string projectId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var specs = await store.LoadAllAsync();
            var doc = ProjectDocumentStore.Load(factory.FlowHome, projectId);

            var failedCount = specs.Count(s => s.State == FlowState.Failed);
            var onHoldCount = specs.Count(s => s.ProcessingStatus == ProcessingStatus.OnHold);
            var reviewCount = specs.Count(s =>
                s.State == FlowState.Review || s.ProcessingStatus == ProcessingStatus.InReview
                || s.ProcessingStatus == ProcessingStatus.UserReview);

            var epics = EpicDocumentStore.LoadAll(factory.FlowHome, projectId);
            var epicSummaries = epics.Select(e => BuildEpicSummary(e, specs)).ToList();

            var lastActivity = specs
                .Where(s => s.UpdatedAt != default)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => (DateTimeOffset?)s.UpdatedAt)
                .FirstOrDefault();

            var view = new ProjectView
            {
                ProjectId = projectId,
                Title = doc?.Title ?? projectId,
                Summary = doc?.Summary,
                DocumentVersion = doc?.Version ?? 0,
                LastActivityAt = lastActivity,
                Stats = new ProjectStats
                {
                    SpecCount = specs.Count,
                    EpicCount = epics.Count,
                    ActiveEpicCount = epics.Count(e => e.ChildSpecIds.Count > 0),
                    OpenReviewCount = reviewCount,
                    FailedSpecCount = failedCount,
                    OnHoldSpecCount = onHoldCount
                },
                Document = new ProjectDocumentSection
                {
                    Problem = doc?.Problem,
                    Goals = doc?.Goals ?? [],
                    NonGoals = doc?.NonGoals ?? [],
                    ContextAndConstraints = doc?.ContextAndConstraints ?? [],
                    ArchitectureOverview = doc?.ArchitectureOverview ?? []
                },
                Epics = epicSummaries
            };

            return Results.Ok(view);
        });

        app.MapGet("/api/projects/{projectId}/document", (string projectId, FlowStoreFactory factory) =>
        {
            var doc = ProjectDocumentStore.Load(factory.FlowHome, projectId);
            return doc != null ? Results.Ok(doc) : Results.NotFound(new { error = "project document not found" });
        });

        app.MapGet("/api/projects/{projectId}/epics", (string projectId, FlowStoreFactory factory) =>
        {
            var epics = EpicDocumentStore.LoadAll(factory.FlowHome, projectId);
            return Results.Ok(epics.OrderBy(e => e.EpicId).ToList());
        });
    }

    private static EpicSummary BuildEpicSummary(EpicDocument epic, IReadOnlyList<Spec> allSpecs)
    {
        var childSpecs = allSpecs.Where(s => epic.ChildSpecIds.Contains(s.Id)).ToList();
        return new EpicSummary
        {
            EpicId = epic.EpicId,
            Title = epic.Title,
            Summary = epic.Summary,
            Priority = epic.Priority,
            Milestone = epic.Milestones.FirstOrDefault()?.Title,
            Owner = epic.Owner,
            SpecCounts = new EpicSpecCounts
            {
                Total = childSpecs.Count,
                Completed = childSpecs.Count(s => s.State == FlowState.Completed || s.State == FlowState.Active),
                Active = childSpecs.Count(s =>
                    s.State is FlowState.Implementation or FlowState.TestGeneration
                    or FlowState.ArchitectureReview or FlowState.Queued),
                Blocked = childSpecs.Count(s => s.ProcessingStatus == ProcessingStatus.OnHold),
                Review = childSpecs.Count(s =>
                    s.State == FlowState.Review || s.ProcessingStatus == ProcessingStatus.InReview
                    || s.ProcessingStatus == ProcessingStatus.UserReview)
            }
        };
    }
}
