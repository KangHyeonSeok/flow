using FlowCore.Models;
using FlowCore.Rules;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Runner;

/// <summary>이벤트 제출 결과</summary>
public sealed class EventSubmitResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public RejectionReason? RejectionReason { get; init; }
    public int? CurrentVersion { get; init; }

    public static EventSubmitResult Ok(int version) => new() { Success = true, CurrentVersion = version };
    public static EventSubmitResult NotFound(string specId) => new() { Error = $"spec not found: {specId}" };
    public static EventSubmitResult Rejected(RejectionReason reason, string msg) =>
        new() { Error = msg, RejectionReason = reason };
    public static EventSubmitResult Conflict(int currentVersion) =>
        new() { Error = $"version conflict (current: {currentVersion})", CurrentVersion = currentVersion };
}

/// <summary>
/// 사용자 이벤트를 RuleEvaluator를 통해 제출한다.
/// FlowRunner 없이 독립적으로 동작하며, agent adapter가 필요 없다.
/// </summary>
public sealed class EventSubmitter
{
    private static readonly HashSet<FlowEvent> AllowedUserEvents =
    [
        FlowEvent.UserReviewSubmitted,
        FlowEvent.SpecCompleted,
        FlowEvent.CancelRequested,
        FlowEvent.RollbackRequested
    ];

    private readonly IFlowStore _store;
    private readonly RunnerConfig _config;
    private readonly TimeProvider _time;

    public EventSubmitter(IFlowStore store, RunnerConfig? config = null, TimeProvider? time = null)
    {
        _store = store;
        _config = config ?? new RunnerConfig();
        _time = time ?? TimeProvider.System;
    }

    public async Task<EventSubmitResult> SubmitAsync(
        string specId, FlowEvent ev, int expectedVersion, CancellationToken ct = default)
    {
        if (!AllowedUserEvents.Contains(ev))
            return EventSubmitResult.Rejected(RejectionReason.UnauthorizedActor,
                $"event {ev} is not allowed for user submission");

        return await SubmitAsActorAsync(specId, ev, expectedVersion, ActorKind.User, ct);
    }

    public async Task<EventSubmitResult> SubmitAsActorAsync(
        string specId, FlowEvent ev, int expectedVersion, ActorKind actor, CancellationToken ct = default)
    {
        var spec = await _store.LoadAsync(specId, ct);
        if (spec == null)
            return EventSubmitResult.NotFound(specId);

        if (spec.Version != expectedVersion)
            return EventSubmitResult.Conflict(spec.Version);

        var assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(specId, ct);
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(specId, ct);

        var ruleInput = new RuleInput
        {
            Spec = spec.ToSnapshot(),
            Event = ev,
            Actor = actor,
            Assignments = assignments,
            ReviewRequests = reviewRequests,
            BaseVersion = spec.Version
        };

        var ruleOutput = RuleEvaluator.Evaluate(ruleInput);
        if (!ruleOutput.Accepted)
        {
            return EventSubmitResult.Rejected(
                ruleOutput.RejectionReason,
                $"event {ev} rejected: {ruleOutput.RejectionReason}");
        }

        // Apply mutation
        if (ruleOutput.Mutation is { } mutation)
        {
            if (mutation.NewState.HasValue) spec.State = mutation.NewState.Value;
            if (mutation.NewProcessingStatus.HasValue) spec.ProcessingStatus = mutation.NewProcessingStatus.Value;
            if (mutation.NewRetryCounters != null) spec.RetryCounters = mutation.NewRetryCounters;
            spec.Version = mutation.NewVersion;
            spec.UpdatedAt = _time.GetUtcNow();
        }

        // Execute side effects
        var runId = FlowId.New("run");
        var executor = new SideEffectExecutor(_store, _config, _time);
        var sideEffectResult = await executor.ExecuteAsync(ruleOutput.SideEffects, spec, runId, ct);

        // CAS save
        var casVersion = (ruleOutput.Mutation?.NewVersion ?? spec.Version) - 1;
        var saveResult = await _store.SaveAsync(spec, casVersion, ct);

        if (!saveResult.IsSuccess)
        {
            await executor.RollbackCreatedFilesAsync(sideEffectResult, spec, ct);
            return EventSubmitResult.Conflict(saveResult.CurrentVersion ?? spec.Version);
        }

        // Log activity (best-effort)
        var activityEvent = new ActivityEvent
        {
            EventId = FlowId.New("evt"),
            Timestamp = _time.GetUtcNow(),
            SpecId = spec.Id,
            Actor = actor.ToString().ToLowerInvariant(),
            Action = ActivityAction.StateTransitionCommitted,
            SourceType = "api",
            BaseVersion = spec.Version,
            State = spec.State,
            ProcessingStatus = spec.ProcessingStatus,
            Message = $"{ev} → {spec.State}/{spec.ProcessingStatus}",
            CorrelationId = runId
        };
        try { await ((IActivityStore)_store).AppendAsync(activityEvent, ct); }
        catch { /* best-effort */ }

        foreach (var ae in sideEffectResult.ActivityEvents)
        {
            try { await ((IActivityStore)_store).AppendAsync(ae, ct); }
            catch { /* best-effort */ }
        }

        return EventSubmitResult.Ok(spec.Version);
    }
}
