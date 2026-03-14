namespace FlowCore.Models;

/// <summary>Side effect 유형</summary>
public enum SideEffectKind
{
    LogActivity,
    CreateAssignment,
    CancelAssignment,
    FailAssignment,
    CreateReviewRequest,
    CloseReviewRequest
}

/// <summary>Rule evaluator가 반환하는 부수 효과. runner가 실행할 수 있도록 대상 ID와 payload를 포함한다.</summary>
public sealed class SideEffect
{
    public required SideEffectKind Kind { get; init; }
    public string? Description { get; init; }
    public string? ActivityAction { get; init; }

    // ── CreateAssignment payload ──
    public AgentRole? AgentRole { get; init; }
    public AssignmentType? AssignmentType { get; init; }
    public string? SpecId { get; init; }

    // ── CancelAssignment / FailAssignment target ──
    public string? TargetAssignmentId { get; init; }

    // ── CreateReviewRequest payload ──
    public string? Reason { get; init; }
    public IReadOnlyList<string>? Questions { get; init; }
    public int? DeadlineSeconds { get; init; }

    // ── CloseReviewRequest target ──
    public string? TargetReviewRequestId { get; init; }

    // ── factory methods ──

    public static SideEffect Log(string description, string? action = null) => new()
    {
        Kind = SideEffectKind.LogActivity,
        Description = description,
        ActivityAction = action
    };

    public static SideEffect CreateAssignment(
        AgentRole role, AssignmentType type,
        string? specId = null, string? description = null) => new()
    {
        Kind = SideEffectKind.CreateAssignment,
        AgentRole = role,
        AssignmentType = type,
        SpecId = specId,
        Description = description
    };

    public static SideEffect CancelAssignment(
        string? targetAssignmentId = null, string? description = null) => new()
    {
        Kind = SideEffectKind.CancelAssignment,
        TargetAssignmentId = targetAssignmentId,
        Description = description
    };

    public static SideEffect FailAssignment(
        string? targetAssignmentId = null, string? description = null) => new()
    {
        Kind = SideEffectKind.FailAssignment,
        TargetAssignmentId = targetAssignmentId,
        Description = description
    };

    public static SideEffect CreateReviewRequest(
        string reason, string? specId = null,
        IReadOnlyList<string>? questions = null,
        int? deadlineSeconds = null) => new()
    {
        Kind = SideEffectKind.CreateReviewRequest,
        Reason = reason,
        SpecId = specId,
        Questions = questions,
        DeadlineSeconds = deadlineSeconds
    };

    public static SideEffect CloseReviewRequest(
        string? targetReviewRequestId = null, string? description = null) => new()
    {
        Kind = SideEffectKind.CloseReviewRequest,
        TargetReviewRequestId = targetReviewRequestId,
        Description = description
    };
}
