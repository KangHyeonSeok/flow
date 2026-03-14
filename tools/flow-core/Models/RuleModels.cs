namespace FlowCore.Models;

/// <summary>이벤트 발생 주체</summary>
public enum ActorKind
{
    Planner,
    Architect,
    Developer,
    TestValidator,
    SpecValidator,
    SpecManager,
    Runner,
    User
}

/// <summary>Rule evaluator 입력</summary>
public sealed class RuleInput
{
    public required SpecSnapshot Spec { get; init; }
    public required FlowEvent Event { get; init; }
    public required ActorKind Actor { get; init; }
    public IReadOnlyList<Assignment> Assignments { get; init; } = [];
    public IReadOnlyList<ReviewRequest> ReviewRequests { get; init; } = [];
    public int BaseVersion { get; init; }
}

/// <summary>상태 변경안</summary>
public sealed class StateMutation
{
    public FlowState? NewState { get; init; }
    public ProcessingStatus? NewProcessingStatus { get; init; }
    public RetryCounters? NewRetryCounters { get; init; }
    public int NewVersion { get; init; }
}

/// <summary>거부 사유</summary>
public enum RejectionReason
{
    None,
    ForbiddenTransition,
    InvalidStateForEvent,
    RetryLimitExceeded,
    ConflictError,
    MissingPrecondition,
    UnauthorizedActor,
    ActiveAssignmentExists
}

/// <summary>Rule evaluator 출력</summary>
public sealed class RuleOutput
{
    public required bool Accepted { get; init; }
    public RejectionReason RejectionReason { get; init; } = RejectionReason.None;
    public StateMutation? Mutation { get; init; }
    public IReadOnlyList<SideEffect> SideEffects { get; init; } = [];

    public static RuleOutput Reject(RejectionReason reason, IReadOnlyList<SideEffect>? sideEffects = null) => new()
    {
        Accepted = false,
        RejectionReason = reason,
        SideEffects = sideEffects ?? []
    };
}
