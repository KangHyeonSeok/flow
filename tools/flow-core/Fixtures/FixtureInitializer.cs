using FlowCore.Models;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Fixtures;

/// <summary>테스트/개발용 fixture spec 초기화 도구</summary>
public sealed class FixtureInitializer
{
    private readonly IFlowStore _store;
    private const string ProjectId = "fixture-project";

    private static readonly string[] FixtureIds =
    [
        "fixture-happy-path",
        "fixture-architect-review",
        "fixture-review-needed",
        "fixture-dep-upstream",
        "fixture-dep-downstream",
        "fixture-stale-assignment",
        "fixture-retry-exceeded"
    ];

    public FixtureInitializer(IFlowStore store)
    {
        _store = store;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await CreateHappyPath(ct);
        await CreateArchitectReview(ct);
        await CreateReviewNeeded(ct);
        await CreateDependencyPair(ct);
        await CreateStaleAssignment(ct);
        await CreateRetryExceeded(ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        // 기존 fixture를 삭제 후 재생성
        foreach (var id in FixtureIds)
            await _store.DeleteSpecAsync(id, ct);

        await InitializeAsync(ct);
    }

    private async Task CreateHappyPath(CancellationToken ct)
    {
        var spec = MakeSpec("fixture-happy-path", "정상 완료 단순 spec",
            FlowState.Draft, ProcessingStatus.Pending, RiskLevel.Low);
        await SaveSpecOrThrow(spec, ct);
    }

    private async Task CreateArchitectReview(CancellationToken ct)
    {
        var spec = MakeSpec("fixture-architect-review", "Architect review 필요 spec",
            FlowState.Draft, ProcessingStatus.Pending, RiskLevel.Medium);
        await SaveSpecOrThrow(spec, ct);
    }

    private async Task CreateReviewNeeded(CancellationToken ct)
    {
        var rrId = FlowId.New("rr");
        var spec = MakeSpec("fixture-review-needed", "Review request 필요 spec",
            FlowState.Review, ProcessingStatus.UserReview, RiskLevel.Low);
        spec.ReviewRequestIds = [rrId];
        await SaveSpecOrThrow(spec, ct);

        var rr = new ReviewRequest
        {
            Id = rrId,
            SpecId = spec.Id,
            CreatedBy = "spec-validator",
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = "구현 방향 확인 필요",
            Summary = "비활성 버튼의 커서 스타일을 선택해주세요.",
            Status = ReviewRequestStatus.Open,
            Options =
            [
                new() { Id = "opt-a", Label = "안 A", Description = "not-allowed cursor" },
                new() { Id = "opt-b", Label = "안 B", Description = "pointer 유지" }
            ]
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr, ct);
    }

    private async Task CreateDependencyPair(CancellationToken ct)
    {
        var upstream = MakeSpec("fixture-dep-upstream", "Dependency upstream spec",
            FlowState.Implementation, ProcessingStatus.InProgress, RiskLevel.Low);
        await SaveSpecOrThrow(upstream, ct);

        var downstream = MakeSpec("fixture-dep-downstream", "Dependency downstream spec",
            FlowState.Queued, ProcessingStatus.Pending, RiskLevel.Low);
        downstream.Dependencies = new Dependency { DependsOn = [upstream.Id] };
        await SaveSpecOrThrow(downstream, ct);
    }

    private async Task CreateStaleAssignment(CancellationToken ct)
    {
        var asgId = FlowId.New("asg");
        var spec = MakeSpec("fixture-stale-assignment", "Stale assignment 회수 필요 spec",
            FlowState.Implementation, ProcessingStatus.InProgress, RiskLevel.Low);
        spec.Assignments = [asgId];
        await SaveSpecOrThrow(spec, ct);

        var asg = new Assignment
        {
            Id = asgId,
            SpecId = spec.Id,
            AgentRole = AgentRole.Developer,
            Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            TimeoutSeconds = 3600
        };
        await ((IAssignmentStore)_store).SaveAsync(asg, ct);
    }

    private async Task CreateRetryExceeded(CancellationToken ct)
    {
        var spec = MakeSpec("fixture-retry-exceeded", "3회 초과 실패 spec",
            FlowState.ArchitectureReview, ProcessingStatus.InProgress, RiskLevel.High);
        spec.RetryCounters = new RetryCounters { ArchitectReviewLoopCount = 3 };
        await SaveSpecOrThrow(spec, ct);
    }

    private async Task SaveSpecOrThrow(Spec spec, CancellationToken ct)
    {
        var result = await _store.SaveAsync(spec, 0, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"Fixture spec '{spec.Id}' save failed: {result.Status} (currentVersion={result.CurrentVersion})");
    }

    private static Spec MakeSpec(string id, string title,
        FlowState state, ProcessingStatus processingStatus, RiskLevel riskLevel) => new()
    {
        Id = id,
        ProjectId = ProjectId,
        Title = title,
        Type = SpecType.Task,
        State = state,
        ProcessingStatus = processingStatus,
        RiskLevel = riskLevel,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };
}
