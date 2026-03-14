using FlowCore.Models;

namespace FlowCore.Agents;

/// <summary>Agent adapter 공통 인터페이스</summary>
public interface IAgentAdapter
{
    AgentRole Role { get; }
    Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default);
}

/// <summary>Agent 입력 envelope</summary>
public sealed class AgentInput
{
    public required Spec Spec { get; init; }
    public required Assignment Assignment { get; init; }
    public IReadOnlyList<ActivityEvent> RecentActivity { get; init; } = [];
    public IReadOnlyList<ReviewRequest> ReviewRequests { get; init; } = [];

    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required int CurrentVersion { get; init; }
}

/// <summary>Agent 출력</summary>
public sealed class AgentOutput
{
    public required AgentResult Result { get; init; }
    public required int BaseVersion { get; init; }
    public FlowEvent? ProposedEvent { get; init; }
    public string? Summary { get; init; }
    public string? Message { get; init; }
    public ProposedReviewRequest? ProposedReviewRequest { get; init; }
}

/// <summary>Agent가 제안하는 ReviewRequest 상세 정보</summary>
public sealed class ProposedReviewRequest
{
    public string? Summary { get; init; }
    public IReadOnlyList<string>? Questions { get; init; }
    public IReadOnlyList<ReviewRequestOption>? Options { get; init; }
}
