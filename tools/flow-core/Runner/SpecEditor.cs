using FlowCore.Models;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Runner;

/// <summary>스펙 필드 수정 요청</summary>
public sealed class SpecEditRequest
{
    public required int ExpectedVersion { get; init; }
    public string? Title { get; init; }
    public string? Problem { get; init; }
    public string? Goal { get; init; }
    public List<AcceptanceCriterion>? AcceptanceCriteria { get; init; }
    public RiskLevel? RiskLevel { get; init; }
}

/// <summary>스펙 수정 결과</summary>
public sealed class SpecEditResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? CurrentVersion { get; init; }
    public Spec? Spec { get; init; }

    public static SpecEditResult Ok(Spec spec) => new() { Success = true, Spec = spec, CurrentVersion = spec.Version };
    public static SpecEditResult NotFound(string specId) => new() { Error = $"spec not found: {specId}" };
    public static SpecEditResult NotAllowed(string reason) => new() { Error = reason };
    public static SpecEditResult Conflict(int currentVersion) =>
        new() { Error = $"version conflict (current: {currentVersion})", CurrentVersion = currentVersion };
}

/// <summary>
/// 스펙 필드를 수정한다. Draft/Queued 상태에서만 허용.
/// </summary>
public sealed class SpecEditor
{
    private static readonly HashSet<FlowState> EditableStates = [FlowState.Draft, FlowState.Queued];

    private readonly IFlowStore _store;
    private readonly TimeProvider _time;

    public SpecEditor(IFlowStore store, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SpecEditResult> UpdateAsync(
        string specId, SpecEditRequest edit, CancellationToken ct = default)
    {
        var spec = await _store.LoadAsync(specId, ct);
        if (spec == null)
            return SpecEditResult.NotFound(specId);

        if (!EditableStates.Contains(spec.State))
            return SpecEditResult.NotAllowed($"spec in {spec.State} state cannot be edited");

        if (spec.Version != edit.ExpectedVersion)
            return SpecEditResult.Conflict(spec.Version);

        // Apply field changes
        var changed = false;
        if (edit.Title != null && edit.Title != spec.Title)
        {
            spec.Title = edit.Title;
            changed = true;
        }
        if (edit.Problem != null && edit.Problem != spec.Problem)
        {
            spec.Problem = edit.Problem;
            changed = true;
        }
        if (edit.Goal != null && edit.Goal != spec.Goal)
        {
            spec.Goal = edit.Goal;
            changed = true;
        }
        if (edit.AcceptanceCriteria != null)
        {
            spec.AcceptanceCriteria = edit.AcceptanceCriteria;
            changed = true;
        }
        if (edit.RiskLevel.HasValue && edit.RiskLevel != spec.RiskLevel)
        {
            spec.RiskLevel = edit.RiskLevel.Value;
            changed = true;
        }

        if (!changed)
            return SpecEditResult.Ok(spec);

        spec.UpdatedAt = _time.GetUtcNow();
        var saveResult = await _store.SaveAsync(spec, spec.Version, ct);

        if (!saveResult.IsSuccess)
            return SpecEditResult.Conflict(saveResult.CurrentVersion ?? spec.Version);

        // Activity log (best-effort)
        var activity = new ActivityEvent
        {
            EventId = FlowId.New("evt"),
            Timestamp = _time.GetUtcNow(),
            SpecId = spec.Id,
            Actor = "user",
            Action = ActivityAction.ManualOverride,
            SourceType = "api",
            BaseVersion = spec.Version,
            State = spec.State,
            ProcessingStatus = spec.ProcessingStatus,
            Message = "spec fields updated via API",
            CorrelationId = FlowId.New("run")
        };
        try { await ((IActivityStore)_store).AppendAsync(activity, ct); }
        catch { /* best-effort */ }

        return SpecEditResult.Ok(spec);
    }
}
