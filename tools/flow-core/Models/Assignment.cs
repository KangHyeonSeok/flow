namespace FlowCore.Models;

/// <summary>실행 단위 task</summary>
public sealed class Assignment
{
    public required string Id { get; init; }
    public required string SpecId { get; init; }
    public required AgentRole AgentRole { get; init; }
    public required AssignmentType Type { get; init; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Queued;

    // Phase 2 확장 필드
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public int? TimeoutSeconds { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ResultSummary { get; set; }
    public string? CancelReason { get; set; }
    public AssignmentWorktree? Worktree { get; init; }
}

/// <summary>Assignment 실행 worktree 정보</summary>
public sealed class AssignmentWorktree
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public string? Branch { get; init; }
}
