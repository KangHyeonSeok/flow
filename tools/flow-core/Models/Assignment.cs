namespace FlowCore.Models;

/// <summary>실행 단위 task</summary>
public sealed class Assignment
{
    public required string Id { get; init; }
    public required string SpecId { get; init; }
    public required AgentRole AgentRole { get; init; }
    public required AssignmentType Type { get; init; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Queued;
}
