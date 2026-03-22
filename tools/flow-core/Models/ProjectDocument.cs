namespace FlowCore.Models;

/// <summary>프로젝트 문서 — 사람이 작성하는 상위 문맥</summary>
public sealed class ProjectDocument
{
    public required string ProjectId { get; init; }
    public int Version { get; set; } = 1;
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public string? Problem { get; set; }
    public IReadOnlyList<string> Goals { get; set; } = [];
    public IReadOnlyList<string> NonGoals { get; set; } = [];
    public IReadOnlyList<string> ContextAndConstraints { get; set; } = [];
    public IReadOnlyList<string> ArchitectureOverview { get; set; } = [];
    public IReadOnlyList<ProjectMilestone> Milestones { get; set; } = [];
    public IReadOnlyList<string> RelatedDocs { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>프로젝트 뷰 — API가 조합한 읽기 모델</summary>
public sealed class ProjectView
{
    public required string ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public int DocumentVersion { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
    public required ProjectStats Stats { get; init; }
    public required ProjectDocumentSection Document { get; init; }
    public IReadOnlyList<EpicSummary> Epics { get; init; } = [];
}

/// <summary>프로젝트 집계 통계</summary>
public sealed class ProjectStats
{
    public int SpecCount { get; init; }
    public int EpicCount { get; init; }
    public int ActiveEpicCount { get; init; }
    public int OpenReviewCount { get; init; }
    public int FailedSpecCount { get; init; }
    public int OnHoldSpecCount { get; init; }
}

/// <summary>프로젝트 문서 본문 섹션 (뷰용)</summary>
public sealed class ProjectDocumentSection
{
    public string? Problem { get; init; }
    public IReadOnlyList<string> Goals { get; init; } = [];
    public IReadOnlyList<string> NonGoals { get; init; } = [];
    public IReadOnlyList<string> ContextAndConstraints { get; init; } = [];
    public IReadOnlyList<string> ArchitectureOverview { get; init; } = [];
}

/// <summary>에픽 요약 (프로젝트 뷰 내 에픽 카드용)</summary>
public sealed class EpicSummary
{
    public required string EpicId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public string? Priority { get; init; }
    public string? Milestone { get; init; }
    public string? Owner { get; init; }
    public required EpicSpecCounts SpecCounts { get; init; }
}

/// <summary>에픽별 스펙 집계</summary>
public sealed class EpicSpecCounts
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Active { get; init; }
    public int Blocked { get; init; }
    public int Review { get; init; }
}

/// <summary>프로젝트 마일스톤</summary>
public sealed class ProjectMilestone
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Status { get; set; } = "planned";
}
