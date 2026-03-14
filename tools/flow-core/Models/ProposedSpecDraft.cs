namespace FlowCore.Models;

/// <summary>Planner가 제안하는 spec 본문 수정/생성 payload</summary>
public sealed class ProposedSpecDraft
{
    public string? Title { get; init; }
    public SpecType? Type { get; init; }
    public string? Problem { get; init; }
    public string? Goal { get; init; }
    public IReadOnlyList<AcceptanceCriterionDraft>? AcceptanceCriteria { get; init; }
    public RiskLevel? RiskLevel { get; init; }
    public IReadOnlyList<string>? DependsOn { get; init; }
}

/// <summary>Planner가 제안하는 AC (Id는 runner가 부여)</summary>
public sealed class AcceptanceCriterionDraft
{
    public required string Text { get; init; }
    public bool Testable { get; init; } = true;
    public string? Notes { get; init; }
}
