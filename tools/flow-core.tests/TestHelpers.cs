using FlowCore.Models;

namespace FlowCore.Tests;

internal static class TestHelpers
{
    /// <summary>이벤트별 기본 actor 매핑 — 테스트에서 매번 actor를 지정하지 않아도 되도록</summary>
    private static readonly Dictionary<FlowEvent, ActorKind> DefaultActors = new()
    {
        { FlowEvent.DraftCreated, ActorKind.Planner },
        { FlowEvent.DraftUpdated, ActorKind.Planner },
        { FlowEvent.AcPrecheckPassed, ActorKind.SpecValidator },
        { FlowEvent.AcPrecheckRejected, ActorKind.SpecValidator },
        { FlowEvent.ArchitectReviewPassed, ActorKind.Architect },
        { FlowEvent.ArchitectReviewRejected, ActorKind.Architect },
        { FlowEvent.AssignmentStarted, ActorKind.Runner },
        { FlowEvent.ImplementationSubmitted, ActorKind.Developer },
        { FlowEvent.TestValidationPassed, ActorKind.TestValidator },
        { FlowEvent.TestValidationRejected, ActorKind.TestValidator },
        { FlowEvent.SpecValidationPassed, ActorKind.SpecValidator },
        { FlowEvent.SpecValidationReworkRequested, ActorKind.SpecValidator },
        { FlowEvent.SpecValidationUserReviewRequested, ActorKind.SpecValidator },
        { FlowEvent.SpecValidationFailed, ActorKind.SpecValidator },
        { FlowEvent.UserReviewSubmitted, ActorKind.User },
        { FlowEvent.SpecCompleted, ActorKind.User },
        { FlowEvent.CancelRequested, ActorKind.User },
        { FlowEvent.RollbackRequested, ActorKind.User },
        { FlowEvent.DependencyBlocked, ActorKind.Runner },
        { FlowEvent.DependencyFailed, ActorKind.Runner },
        { FlowEvent.DependencyResolved, ActorKind.Runner },
        { FlowEvent.AssignmentTimedOut, ActorKind.Runner },
        { FlowEvent.AssignmentResumed, ActorKind.Runner },
        { FlowEvent.ReviewRequestTimedOut, ActorKind.Runner },
    };

    public static SpecSnapshot CreateSpec(
        FlowState state = FlowState.Draft,
        ProcessingStatus processingStatus = ProcessingStatus.Pending,
        RiskLevel riskLevel = RiskLevel.Low,
        int version = 1,
        string id = "spec-001",
        RetryCounters? retryCounters = null)
    {
        return new SpecSnapshot
        {
            Id = id,
            ProjectId = "proj-001",
            State = state,
            ProcessingStatus = processingStatus,
            RiskLevel = riskLevel,
            Version = version,
            RetryCounters = retryCounters ?? new RetryCounters()
        };
    }

    public static RuleInput CreateInput(
        SpecSnapshot spec,
        FlowEvent ev,
        int? baseVersion = null,
        ActorKind? actor = null,
        IReadOnlyList<Assignment>? assignments = null,
        IReadOnlyList<ReviewRequest>? reviewRequests = null)
    {
        return new RuleInput
        {
            Spec = spec,
            Event = ev,
            Actor = actor ?? DefaultActors.GetValueOrDefault(ev, ActorKind.Runner),
            BaseVersion = baseVersion ?? spec.Version,
            Assignments = assignments ?? [],
            ReviewRequests = reviewRequests ?? []
        };
    }

    public static Assignment CreateAssignment(
        string id = "asg-001",
        string specId = "spec-001",
        AgentRole role = AgentRole.Developer,
        AssignmentType type = AssignmentType.Implementation,
        AssignmentStatus status = AssignmentStatus.Running)
    {
        return new Assignment
        {
            Id = id,
            SpecId = specId,
            AgentRole = role,
            Type = type,
            Status = status
        };
    }

    public static ReviewRequest CreateReviewRequest(
        string id = "rr-001",
        string specId = "spec-001",
        ReviewRequestStatus status = ReviewRequestStatus.Open)
    {
        return new ReviewRequest
        {
            Id = id,
            SpecId = specId,
            Status = status
        };
    }
}
