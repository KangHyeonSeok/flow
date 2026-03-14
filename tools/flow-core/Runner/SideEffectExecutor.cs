using FlowCore.Models;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Runner;

/// <summary>side effect 실행 결과</summary>
public sealed class SideEffectResult
{
    public IReadOnlyList<string> CreatedAssignmentIds { get; init; } = [];
    public IReadOnlyList<string> CreatedReviewRequestIds { get; init; } = [];
    public IReadOnlyList<ActivityEvent> ActivityEvents { get; init; } = [];

    /// <summary>CAS 실패 시 생성된 파일을 삭제하기 위한 specId</summary>
    public string? SpecId { get; init; }
}

/// <summary>
/// RuleEvaluator가 반환한 side effect를 실행한다.
/// spec 객체를 in-place로 변경하며, spec 저장(CAS)은 호출자가 수행한다.
/// </summary>
public sealed class SideEffectExecutor
{
    private readonly IFlowStore _store;
    private readonly RunnerConfig _config;
    private readonly TimeProvider _time;

    public SideEffectExecutor(IFlowStore store, RunnerConfig config, TimeProvider time)
    {
        _store = store;
        _config = config;
        _time = time;
    }

    public async Task<SideEffectResult> ExecuteAsync(
        IReadOnlyList<SideEffect> effects,
        Spec spec,
        string correlationId,
        CancellationToken ct = default)
    {
        var createdAssignmentIds = new List<string>();
        var createdReviewRequestIds = new List<string>();
        var activityEvents = new List<ActivityEvent>();

        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case SideEffectKind.CreateAssignment:
                {
                    var asgId = FlowId.New("asg");
                    var assignment = new Assignment
                    {
                        Id = asgId,
                        SpecId = effect.SpecId ?? spec.Id,
                        AgentRole = effect.AgentRole!.Value,
                        Type = effect.AssignmentType!.Value,
                        Status = AssignmentStatus.Running,
                        StartedAt = _time.GetUtcNow(),
                        TimeoutSeconds = _config.DefaultTimeoutSeconds
                    };
                    await ((IAssignmentStore)_store).SaveAsync(assignment, ct);
                    spec.Assignments = spec.Assignments.Append(asgId).ToList();
                    createdAssignmentIds.Add(asgId);
                    break;
                }
                case SideEffectKind.CancelAssignment:
                {
                    if (effect.TargetAssignmentId is { } targetId)
                    {
                        var asg = await ((IAssignmentStore)_store).LoadAsync(spec.Id, targetId, ct);
                        if (asg != null)
                        {
                            asg.Status = AssignmentStatus.Cancelled;
                            asg.FinishedAt = _time.GetUtcNow();
                            asg.CancelReason = effect.Description;
                            await ((IAssignmentStore)_store).SaveAsync(asg, ct);
                        }
                    }
                    break;
                }
                case SideEffectKind.FailAssignment:
                {
                    if (effect.TargetAssignmentId is { } targetId)
                    {
                        var asg = await ((IAssignmentStore)_store).LoadAsync(spec.Id, targetId, ct);
                        if (asg != null)
                        {
                            asg.Status = AssignmentStatus.Failed;
                            asg.FinishedAt = _time.GetUtcNow();
                            asg.ResultSummary = effect.Description;
                            await ((IAssignmentStore)_store).SaveAsync(asg, ct);
                        }
                    }
                    break;
                }
                case SideEffectKind.CreateReviewRequest:
                {
                    var rrId = FlowId.New("rr");
                    var rr = new ReviewRequest
                    {
                        Id = rrId,
                        SpecId = effect.SpecId ?? spec.Id,
                        CreatedBy = "runner",
                        CreatedAt = _time.GetUtcNow(),
                        Reason = effect.Reason,
                        Status = ReviewRequestStatus.Open,
                        DeadlineAt = _time.GetUtcNow().AddSeconds(_config.DefaultReviewDeadlineSeconds)
                    };
                    await ((IReviewRequestStore)_store).SaveAsync(rr, ct);
                    spec.ReviewRequestIds = spec.ReviewRequestIds.Append(rrId).ToList();
                    createdReviewRequestIds.Add(rrId);
                    break;
                }
                case SideEffectKind.CloseReviewRequest:
                {
                    if (effect.TargetReviewRequestId is { } targetId)
                    {
                        var rr = await ((IReviewRequestStore)_store).LoadAsync(spec.Id, targetId, ct);
                        if (rr != null)
                        {
                            rr.Status = ReviewRequestStatus.Closed;
                            rr.Resolution = effect.Description;
                            await ((IReviewRequestStore)_store).SaveAsync(rr, ct);
                        }
                    }
                    break;
                }
                case SideEffectKind.LogActivity:
                {
                    var action = Enum.TryParse<ActivityAction>(effect.ActivityAction, true, out var parsed)
                        ? parsed
                        : ActivityAction.ManualOverride;
                    var evt = new ActivityEvent
                    {
                        EventId = FlowId.New("evt"),
                        Timestamp = _time.GetUtcNow(),
                        SpecId = spec.Id,
                        Actor = "runner",
                        Action = action,
                        SourceType = "runner",
                        BaseVersion = spec.Version,
                        State = spec.State,
                        ProcessingStatus = spec.ProcessingStatus,
                        Message = effect.Description ?? "",
                        CorrelationId = correlationId
                    };
                    activityEvents.Add(evt);
                    break;
                }
            }
        }

        return new SideEffectResult
        {
            CreatedAssignmentIds = createdAssignmentIds,
            CreatedReviewRequestIds = createdReviewRequestIds,
            ActivityEvents = activityEvents,
            SpecId = spec.Id
        };
    }

    /// <summary>
    /// CAS 실패 시 생성된 assignment/review request 파일을 삭제한다.
    /// spec.Assignments / spec.ReviewRequestIds에서도 제거한다.
    /// </summary>
    public async Task RollbackCreatedFilesAsync(
        SideEffectResult result, Spec spec, CancellationToken ct = default)
    {
        // 생성된 assignment 파일 삭제
        foreach (var asgId in result.CreatedAssignmentIds)
        {
            try
            {
                var asg = await ((IAssignmentStore)_store).LoadAsync(spec.Id, asgId, ct);
                if (asg != null)
                {
                    asg.Status = AssignmentStatus.Cancelled;
                    asg.CancelReason = "CAS conflict rollback";
                    asg.FinishedAt = _time.GetUtcNow();
                    await ((IAssignmentStore)_store).SaveAsync(asg, ct);
                }
            }
            catch { /* best-effort rollback */ }
        }
        // spec.Assignments에서 제거
        var rollbackAsgSet = new HashSet<string>(result.CreatedAssignmentIds);
        spec.Assignments = spec.Assignments.Where(id => !rollbackAsgSet.Contains(id)).ToList();

        // 생성된 review request 파일 삭제
        foreach (var rrId in result.CreatedReviewRequestIds)
        {
            try
            {
                var rr = await ((IReviewRequestStore)_store).LoadAsync(spec.Id, rrId, ct);
                if (rr != null)
                {
                    rr.Status = ReviewRequestStatus.Closed;
                    rr.Resolution = "CAS conflict rollback";
                    await ((IReviewRequestStore)_store).SaveAsync(rr, ct);
                }
            }
            catch { /* best-effort rollback */ }
        }
        // spec.ReviewRequestIds에서 제거
        var rollbackRrSet = new HashSet<string>(result.CreatedReviewRequestIds);
        spec.ReviewRequestIds = spec.ReviewRequestIds.Where(id => !rollbackRrSet.Contains(id)).ToList();
    }
}
