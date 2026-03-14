namespace FlowCore.Models;

/// <summary>DependencyEvaluator 입력</summary>
public sealed class DependencyInput
{
    /// <summary>상태가 변경된 spec</summary>
    public required SpecSnapshot ChangedSpec { get; init; }

    /// <summary>변경 전 상태</summary>
    public required FlowState PreviousState { get; init; }

    /// <summary>변경 전 처리 상태</summary>
    public required ProcessingStatus PreviousProcessingStatus { get; init; }

    /// <summary>ChangedSpec을 dependsOn으로 참조하는 downstream spec 목록</summary>
    public required IReadOnlyList<SpecSnapshot> DownstreamSpecs { get; init; }

    /// <summary>
    /// downstream의 dependsOn에 포함된 모든 upstream spec의 현재 snapshot.
    /// ChangedSpec 자신도 포함해야 한다. DependencyResolved 판정 시
    /// 다른 upstream이 여전히 blocked인지 확인하는 데 사용한다.
    /// </summary>
    public IReadOnlyList<SpecSnapshot> AllUpstreamSpecs { get; init; } = [];
}

/// <summary>DependencyEvaluator가 downstream spec에 발행할 이벤트</summary>
public sealed class DependencyEffect
{
    public required string TargetSpecId { get; init; }
    public required FlowEvent Event { get; init; }
}

/// <summary>의존성 그래프의 순환 참조</summary>
public sealed class DependencyCycle
{
    public required IReadOnlyList<string> SpecIds { get; init; }
}
