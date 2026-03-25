using FlowApi.Models;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowApi.Endpoints;

public static class SpecEndpoints
{
    public static void MapSpecEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId}/specs");

        group.MapGet("/", async (string projectId, FlowStoreFactory factory, string? state, string? status) =>
        {
            var store = factory.GetStore(projectId);
            var specs = await store.LoadAllAsync();

            if (state != null && Enum.TryParse<FlowState>(state, true, out var stateFilter))
                specs = specs.Where(s => s.State == stateFilter).ToList();
            if (status != null && Enum.TryParse<ProcessingStatus>(status, true, out var statusFilter))
                specs = specs.Where(s => s.ProcessingStatus == statusFilter).ToList();

            return Results.Ok(specs.OrderBy(s => s.Id).ToList());
        });

        group.MapGet("/{specId}", async (string projectId, string specId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var spec = await store.LoadAsync(specId);
            return spec != null ? Results.Ok(spec) : Results.NotFound(new { error = $"spec not found: {specId}" });
        });

        group.MapPost("/", async (string projectId, CreateSpecRequest req, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);

            // Auto-generate ID
            var allSpecs = await store.LoadAllAsync();
            var maxNum = 0;
            foreach (var s in allSpecs)
            {
                if (s.Id.StartsWith("F-") && int.TryParse(s.Id[2..], out var n) && n > maxNum)
                    maxNum = n;
            }
            var specId = $"F-{maxNum + 1:D3}";

            var specType = req.Type?.ToLowerInvariant() switch
            {
                "task" => SpecType.Task,
                _ => SpecType.Feature
            };

            var riskLevel = req.RiskLevel?.ToLowerInvariant() switch
            {
                "medium" => RiskLevel.Medium,
                "high" => RiskLevel.High,
                "critical" => RiskLevel.Critical,
                _ => RiskLevel.Low
            };

            var now = DateTimeOffset.UtcNow;
            var spec = new Spec
            {
                Id = specId,
                ProjectId = projectId,
                EpicId = req.EpicId,
                Title = req.Title,
                Type = specType,
                Problem = req.Problem,
                Goal = req.Goal,
                Context = req.Context,
                NonGoals = req.NonGoals,
                ImplementationNotes = req.ImplementationNotes,
                TestPlan = req.TestPlan,
                RiskLevel = riskLevel,
                State = FlowState.Draft,
                ProcessingStatus = ProcessingStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };

            if (req.AcceptanceCriteria is { Count: > 0 } acList)
            {
                spec.AcceptanceCriteria = acList.Select((ac, i) => new AcceptanceCriterion
                {
                    Id = $"AC-{i + 1:D3}",
                    Text = ac.Text,
                    Testable = ac.Testable,
                    Notes = ac.Notes
                }).ToList();
            }

            var result = await store.SaveAsync(spec, 0);
            if (!result.IsSuccess)
                return Results.Conflict(new { error = "failed to save spec", currentVersion = result.CurrentVersion });

            return Results.Created($"/api/projects/{projectId}/specs/{specId}", spec);
        });

        group.MapPatch("/{specId}", async (string projectId, string specId,
            UpdateSpecRequest req, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var editor = new SpecEditor(store);

            List<AcceptanceCriterion>? acList = null;
            if (req.AcceptanceCriteria != null)
            {
                acList = req.AcceptanceCriteria.Select((ac, i) => new AcceptanceCriterion
                {
                    Id = $"AC-{i + 1:D3}",
                    Text = ac.Text,
                    Testable = ac.Testable,
                    Notes = ac.Notes
                }).ToList();
            }

            RiskLevel? riskLevel = req.RiskLevel?.ToLowerInvariant() switch
            {
                "low" => RiskLevel.Low,
                "medium" => RiskLevel.Medium,
                "high" => RiskLevel.High,
                "critical" => RiskLevel.Critical,
                _ => null
            };

            var editReq = new SpecEditRequest
            {
                ExpectedVersion = req.Version,
                EpicId = req.EpicId,
                Title = req.Title,
                Problem = req.Problem,
                Goal = req.Goal,
                Context = req.Context,
                NonGoals = req.NonGoals,
                ImplementationNotes = req.ImplementationNotes,
                TestPlan = req.TestPlan,
                AcceptanceCriteria = acList,
                RiskLevel = riskLevel
            };

            var result = await editor.UpdateAsync(specId, editReq);

            if (!result.Success)
            {
                if (result.Error?.Contains("not found") == true)
                    return Results.NotFound(new { error = result.Error });
                if (result.CurrentVersion.HasValue)
                    return Results.Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
                return Results.BadRequest(new { error = result.Error });
            }

            return Results.Ok(result.Spec);
        });

        group.MapDelete("/{specId}", async (string projectId, string specId, FlowStoreFactory factory) =>
        {
            var store = factory.GetStore(projectId);
            var spec = await store.LoadAsync(specId);
            if (spec == null)
                return Results.NotFound(new { error = $"spec not found: {specId}" });

            await store.DeleteSpecAsync(specId);
            return Results.NoContent();
        });
    }
}
