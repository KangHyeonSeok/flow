namespace FlowCore.Models;

/// <summary>테스트 유형</summary>
public enum TestType
{
    Unit,
    E2E,
    User
}

/// <summary>테스트 실행 상태</summary>
public enum TestStatus
{
    NotRun,
    Passed,
    Failed,
    Skipped
}

/// <summary>테스트 정의</summary>
public sealed class TestDefinition
{
    public required string Id { get; init; }
    public required TestType Type { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> AcIds { get; init; } = [];
    public TestStatus Status { get; set; } = TestStatus.NotRun;
}
