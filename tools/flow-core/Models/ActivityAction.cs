namespace FlowCore.Models;

/// <summary>Activity log 전용 action enum. FlowEvent 1:1 매핑 + 로그 전용 값</summary>
public enum ActivityAction
{
    // FlowEvent 1:1 매핑 (24개)
    DraftCreated,
    DraftUpdated,
    AcPrecheckPassed,
    AcPrecheckRejected,
    ArchitectReviewPassed,
    ArchitectReviewRejected,
    AssignmentStarted,
    ImplementationSubmitted,
    TestGenerationCompleted,
    TestGenerationRejected,
    SpecValidationPassed,
    SpecValidationReworkRequested,
    SpecValidationUserReviewRequested,
    UserReviewSubmitted,
    SpecValidationFailed,
    SpecCompleted,
    CancelRequested,
    DependencyBlocked,
    DependencyFailed,
    DependencyResolved,
    AssignmentTimedOut,
    AssignmentResumed,
    ReviewRequestTimedOut,
    RollbackRequested,
    SpecArchived,

    // 로그 전용 값 — side effect / 상태 변경
    SpecActivated,
    SpecFailed,
    AssignmentCancelled,
    AssignmentFailed,
    ReviewRequestClosed,
    ReviewRequestSuperseded,
    CounterReset,
    ManualOverride,

    // 로그 전용 값 — runner orchestration
    SpecSelected,
    DispatchDecided,
    AgentInvoked,
    AgentCompleted,
    EventRejected,
    StateTransitionCommitted,
    ConflictDetected,
    RetryScheduled
}
