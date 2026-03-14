namespace FlowCore.Models;

/// <summary>Rule evaluator 입력용 spec 스냅샷</summary>
public sealed class SpecSnapshot
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required FlowState State { get; set; }
    public required ProcessingStatus ProcessingStatus { get; set; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Low;
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public int Version { get; set; }
    public RetryCounters RetryCounters { get; set; } = new();
}

/// <summary>반복 제한 카운터</summary>
public sealed class RetryCounters
{
    public int UserReviewLoopCount { get; set; }
    public int ReworkLoopCount { get; set; }
    public int ArchitectReviewLoopCount { get; set; }

    public RetryCounters Clone() => new()
    {
        UserReviewLoopCount = UserReviewLoopCount,
        ReworkLoopCount = ReworkLoopCount,
        ArchitectReviewLoopCount = ArchitectReviewLoopCount
    };
}
