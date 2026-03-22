namespace FlowCore.Models;

/// <summary>에픽 문서 — 사람이 작성하는 에픽 수준 문맥</summary>
public sealed class EpicDocument
{
    public required string ProjectId { get; init; }
    public required string EpicId { get; init; }
    public int Version { get; set; } = 1;
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public string? Problem { get; set; }
    public string? Goal { get; set; }
    public IReadOnlyList<string> Scope { get; set; } = [];
    public IReadOnlyList<string> NonGoals { get; set; } = [];
    public IReadOnlyList<string> SuccessCriteria { get; set; } = [];
    public IReadOnlyList<string> ChildSpecIds { get; set; } = [];
    public IReadOnlyList<string> Dependencies { get; set; } = [];
    public IReadOnlyList<EpicMilestone> Milestones { get; set; } = [];
    public IReadOnlyList<string> RelatedDocs { get; set; } = [];
    public string? Owner { get; set; }
    public string? Priority { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>에픽 뷰 — API가 조합한 읽기 모델</summary>
public sealed class EpicView
{
    public required string ProjectId { get; init; }
    public required string EpicId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public int DocumentVersion { get; init; }
    public string? Priority { get; init; }
    public string? Owner { get; init; }
    public string? Milestone { get; init; }
    public required EpicProgress Progress { get; init; }
    public required EpicNarrative Narrative { get; init; }
    public IReadOnlyList<EpicChildSpec> ChildSpecs { get; init; } = [];
    public IReadOnlyList<string> EpicDependsOn { get; init; } = [];
    public IReadOnlyList<string> RelatedDocs { get; init; } = [];
}

/// <summary>에픽 진행률</summary>
public sealed class EpicProgress
{
    public int TotalSpecs { get; init; }
    public int CompletedSpecs { get; init; }
    public int ActiveSpecs { get; init; }
    public int BlockedSpecs { get; init; }
    public double CompletionRatio { get; init; }
}

/// <summary>에픽 서술 섹션 (뷰용)</summary>
public sealed class EpicNarrative
{
    public string? Problem { get; init; }
    public string? Goal { get; init; }
    public IReadOnlyList<string> Scope { get; init; } = [];
    public IReadOnlyList<string> NonGoals { get; init; } = [];
    public IReadOnlyList<string> SuccessCriteria { get; init; } = [];
}

/// <summary>에픽 뷰 내 자식 스펙 요약</summary>
public sealed class EpicChildSpec
{
    public required string SpecId { get; init; }
    public required string Title { get; init; }
    public FlowState State { get; init; }
    public ProcessingStatus ProcessingStatus { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}

/// <summary>에픽 마일스톤</summary>
public sealed class EpicMilestone
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Status { get; set; } = "planned";
}
