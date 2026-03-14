namespace FlowCore.Models;

/// <summary>Spec 유형</summary>
public enum SpecType
{
    Feature,
    Task
}

/// <summary>전체 Spec aggregate 모델</summary>
public sealed class Spec
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string Title { get; set; }
    public SpecType Type { get; set; } = SpecType.Task;
    public string? Problem { get; set; }
    public string? Goal { get; set; }
    public IReadOnlyList<AcceptanceCriterion>? AcceptanceCriteria { get; set; }
    public required FlowState State { get; set; }
    public required ProcessingStatus ProcessingStatus { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
    public Dependency Dependencies { get; set; } = new();
    public IReadOnlyList<string> Assignments { get; set; } = [];
    public IReadOnlyList<string> ReviewRequestIds { get; set; } = [];
    public IReadOnlyList<string> TestIds { get; set; } = [];
    public IReadOnlyList<TestDefinition>? Tests { get; set; }
    public RetryCounters RetryCounters { get; set; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }

    /// <summary>RuleEvaluator 입력용 스냅샷으로 변환</summary>
    public SpecSnapshot ToSnapshot() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        State = State,
        ProcessingStatus = ProcessingStatus,
        RiskLevel = RiskLevel,
        DependsOn = Dependencies.DependsOn,
        Version = Version,
        RetryCounters = RetryCounters.Clone()
    };
}
