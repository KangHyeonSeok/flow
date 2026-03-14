namespace FlowCore.Models;

/// <summary>Agent가 생성한 evidence 참조</summary>
public sealed class EvidenceRef
{
    /// <summary>evidence 유형 (예: "test-result", "build-log", "coverage", "screenshot")</summary>
    public required string Kind { get; init; }

    /// <summary>evidence 디렉토리 기준 상대 경로</summary>
    public required string RelativePath { get; init; }

    /// <summary>사람이 읽을 수 있는 요약</summary>
    public string? Summary { get; init; }
}

/// <summary>Evidence manifest (저장용)</summary>
public sealed class EvidenceManifest
{
    public required string SpecId { get; init; }
    public required string RunId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<EvidenceRef> Refs { get; init; } = [];
}
