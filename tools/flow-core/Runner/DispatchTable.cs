using FlowCore.Models;

namespace FlowCore.Runner;

/// <summary>dispatch 결과 유형</summary>
public enum DispatchKind
{
    /// <summary>agent 호출 없이 RuleEvaluator만으로 처리</summary>
    RuleOnly,
    /// <summary>agent를 호출하여 처리</summary>
    Agent,
    /// <summary>처리할 작업이 없어 대기</summary>
    Wait
}

/// <summary>dispatch table 조회 결과</summary>
public sealed class DispatchDecision
{
    public required DispatchKind Kind { get; init; }
    public AgentRole? AgentRole { get; init; }
    public AssignmentType? AssignmentType { get; init; }
    public FlowEvent? RuleOnlyEvent { get; init; }
    public string? Reason { get; init; }

    public static DispatchDecision RuleOnly(FlowEvent ev, string reason) => new()
    {
        Kind = DispatchKind.RuleOnly,
        RuleOnlyEvent = ev,
        Reason = reason
    };

    public static DispatchDecision AgentDispatch(AgentRole role, AssignmentType type, string reason) => new()
    {
        Kind = DispatchKind.Agent,
        AgentRole = role,
        AssignmentType = type,
        Reason = reason
    };

    public static DispatchDecision Waiting(string reason) => new()
    {
        Kind = DispatchKind.Wait,
        Reason = reason
    };
}

/// <summary>dispatch table: (FlowState, ProcessingStatus) → DispatchDecision</summary>
public static class DispatchTable
{
    /// <summary>FlowState 진행도 (높을수록 우선 처리)</summary>
    private static readonly Dictionary<FlowState, int> StateProgress = new()
    {
        { FlowState.Review, 6 },
        { FlowState.TestValidation, 5 },
        { FlowState.Implementation, 4 },
        { FlowState.ArchitectureReview, 3 },
        { FlowState.Queued, 2 },
        { FlowState.Draft, 1 },
        { FlowState.Active, 0 },
        { FlowState.Completed, 0 },
        { FlowState.Failed, 0 }
    };

    /// <summary>spec의 현재 상태로 dispatch 결정을 내린다.</summary>
    public static DispatchDecision Decide(
        Spec spec,
        IReadOnlyList<Assignment> assignments,
        IReadOnlyList<ReviewRequest> reviewRequests)
    {
        var hasActiveAssignment = assignments.Any(a =>
            a.Status is AssignmentStatus.Running or AssignmentStatus.Queued);
        var hasOpenReviewRequest = reviewRequests.Any(r =>
            r.Status == ReviewRequestStatus.Open);

        // Planning assignment 우선 처리: open Planning assignment가 있으면 Planner를 먼저 dispatch
        var openPlanningAssignment = assignments.FirstOrDefault(a =>
            a.Type == AssignmentType.Planning
            && a.Status is AssignmentStatus.Queued or AssignmentStatus.Running);
        if (openPlanningAssignment != null)
        {
            if (openPlanningAssignment.Status == AssignmentStatus.Running && hasActiveAssignment)
                return DispatchDecision.Waiting("Planning/Running — active Planner assignment");
            return DispatchDecision.AgentDispatch(
                Models.AgentRole.Planner, Models.AssignmentType.Planning,
                "open Planning assignment → Planner");
        }

        return (spec.State, spec.ProcessingStatus) switch
        {
            (FlowState.Draft, ProcessingStatus.Pending)
                => DispatchDecision.AgentDispatch(
                    Models.AgentRole.SpecValidator, Models.AssignmentType.AcPrecheck,
                    "Draft/Pending → AC precheck"),

            (FlowState.Queued, ProcessingStatus.Pending)
                when spec.RiskLevel is RiskLevel.Medium or RiskLevel.High or RiskLevel.Critical
                => DispatchDecision.RuleOnly(FlowEvent.AssignmentStarted,
                    "Queued/Pending (Medium+) → ArchitectureReview"),

            (FlowState.Queued, ProcessingStatus.Pending)
                => DispatchDecision.RuleOnly(FlowEvent.AssignmentStarted,
                    "Queued/Pending (Low) → Implementation"),

            (FlowState.ArchitectureReview, ProcessingStatus.Pending)
                => DispatchDecision.AgentDispatch(
                    Models.AgentRole.Architect, Models.AssignmentType.ArchitectureReview,
                    "ArchitectureReview/Pending → Architect"),

            (FlowState.ArchitectureReview, ProcessingStatus.InProgress) when hasActiveAssignment
                => DispatchDecision.Waiting("ArchitectureReview/InProgress — active assignment"),

            (FlowState.Implementation, ProcessingStatus.Pending)
                => DispatchDecision.AgentDispatch(
                    Models.AgentRole.Developer, Models.AssignmentType.Implementation,
                    "Implementation/Pending → Developer"),

            (FlowState.Implementation, ProcessingStatus.InProgress) when hasActiveAssignment
                => DispatchDecision.Waiting("Implementation/InProgress — active assignment"),

            (FlowState.TestValidation, ProcessingStatus.Pending)
                => DispatchDecision.AgentDispatch(
                    Models.AgentRole.TestValidator, Models.AssignmentType.TestValidation,
                    "TestValidation/Pending → TestValidator"),

            (FlowState.TestValidation, ProcessingStatus.InProgress) when hasActiveAssignment
                => DispatchDecision.Waiting("TestValidation/InProgress — active assignment"),

            (FlowState.Review, ProcessingStatus.InReview)
                => DispatchDecision.AgentDispatch(
                    Models.AgentRole.SpecValidator, Models.AssignmentType.SpecValidation,
                    "Review/InReview → SpecValidator"),

            (FlowState.Review, ProcessingStatus.UserReview) when hasOpenReviewRequest
                => DispatchDecision.Waiting("Review/UserReview — waiting for user"),

            (FlowState.Active, ProcessingStatus.Done)
                => DispatchDecision.Waiting("Active/Done — waiting for SpecCompleted"),

            _ => DispatchDecision.Waiting($"no dispatch for {spec.State}/{spec.ProcessingStatus}")
        };
    }

    /// <summary>dispatch에서 제외할 spec인지 확인한다.</summary>
    public static bool ShouldExclude(Spec spec, TimeProvider time)
    {
        if (spec.State is FlowState.Failed or FlowState.Completed or FlowState.Archived)
            return true;
        if (spec.ProcessingStatus is ProcessingStatus.OnHold or ProcessingStatus.Error)
            return true;
        if (spec.RetryCounters.RetryNotBefore is { } notBefore
            && notBefore > time.GetUtcNow())
            return true;
        return false;
    }

    /// <summary>upstream이 모두 완료/활성인지 확인한다. 미완료 upstream이 있으면 true.</summary>
    public static bool HasIncompleteUpstream(Spec spec, IReadOnlyDictionary<string, Spec> allSpecs)
    {
        foreach (var upId in spec.Dependencies.DependsOn)
        {
            if (!allSpecs.TryGetValue(upId, out var upstream))
                continue; // upstream이 없으면 pass (외부 dependency 등)
            if (upstream.State is not (FlowState.Active or FlowState.Completed))
                return true;
        }
        return false;
    }

    /// <summary>backlog 정렬: timeout → 진행도 높은 순 → UpdatedAt 오래된 순</summary>
    public static IReadOnlyList<Spec> SortBacklog(
        IEnumerable<Spec> specs,
        IReadOnlyDictionary<string, IReadOnlyList<Assignment>> assignmentsBySpec,
        TimeProvider time)
    {
        return specs.OrderByDescending(s => IsStaleAssignment(s, assignmentsBySpec, time) ? 1 : 0)
            .ThenByDescending(s => StateProgress.GetValueOrDefault(s.State, 0))
            .ThenBy(s => s.UpdatedAt)
            .ToList();
    }

    private static bool IsStaleAssignment(
        Spec spec,
        IReadOnlyDictionary<string, IReadOnlyList<Assignment>> assignmentsBySpec,
        TimeProvider time)
    {
        if (!assignmentsBySpec.TryGetValue(spec.Id, out var assignments))
            return false;
        var now = time.GetUtcNow();
        return assignments.Any(a =>
            a.Status == AssignmentStatus.Running
            && a.StartedAt.HasValue
            && a.TimeoutSeconds.HasValue
            && a.StartedAt.Value.AddSeconds(a.TimeoutSeconds.Value) < now);
    }
}
