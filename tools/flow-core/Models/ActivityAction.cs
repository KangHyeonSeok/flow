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
    TestValidationPassed,
    TestValidationRejected,
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

    // 로그 전용 값
    SpecActivated,
    SpecFailed,
    AssignmentCancelled,
    AssignmentFailed,
    ReviewRequestClosed,
    CounterReset,
    ManualOverride
}
