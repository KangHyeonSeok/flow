namespace FlowCore.Backend;

/// <summary>CLI 기반 AI 백엔드 공통 인터페이스</summary>
public interface ICliBackend : IAsyncDisposable
{
    string BackendId { get; }
    Task<CliResponse> RunPromptAsync(string prompt, CliBackendOptions options, CancellationToken ct = default);
}

/// <summary>백엔드 실행 옵션</summary>
public sealed class CliBackendOptions
{
    public string? WorkingDirectory { get; init; }
    public bool AllowFileEdits { get; init; } = false;
    public IReadOnlyList<string>? AllowedTools { get; init; }
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(300);
    public TimeSpan HardTimeout { get; init; } = TimeSpan.FromSeconds(1800);
}

/// <summary>백엔드 응답</summary>
public sealed class CliResponse
{
    public required string ResponseText { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public CliStopReason StopReason { get; init; }
}

/// <summary>백엔드 실행 종료 사유</summary>
public enum CliStopReason
{
    Completed,
    Timeout,
    Cancelled,
    Error
}
