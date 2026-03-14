namespace FlowCore.Models;

/// <summary>비즈니스 상태 (phase)</summary>
public enum FlowState
{
    Draft,              // 초안
    Queued,             // 대기
    ArchitectureReview, // 구현 검토
    Implementation,     // 구현
    TestValidation,     // 테스트 검증
    Review,             // 검토
    Active,             // 활성
    Failed,             // 실패
    Completed,          // 완료
    Archived            // 보관
}

/// <summary>처리 상태</summary>
public enum ProcessingStatus
{
    Pending,        // 대기
    InProgress,     // 처리중
    InReview,       // 검토
    UserReview,     // 사용자검토
    Done,           // 완료
    Error,          // 실패
    OnHold          // 보류
}

/// <summary>상태 전이 이벤트</summary>
public enum FlowEvent
{
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
    SpecArchived,
    ExecutionFailed
}

/// <summary>위험도</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>에이전트 역할</summary>
public enum AgentRole
{
    Planner,
    Architect,
    Developer,
    TestValidator,
    SpecValidator,
    SpecManager
}

/// <summary>Assignment 유형</summary>
public enum AssignmentType
{
    Planning,
    AcPrecheck,
    ArchitectureReview,
    Implementation,
    TestValidation,
    SpecValidation,
    StateTransition
}

/// <summary>Assignment 상태</summary>
public enum AssignmentStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Review request 상태</summary>
public enum ReviewRequestStatus
{
    Open,
    Answered,
    Closed,
    Superseded
}

/// <summary>Agent 결과 유형</summary>
public enum AgentResult
{
    Success,
    RetryableFailure,
    TerminalFailure,
    NoOp
}
