namespace FlowCore.Backend;

/// <summary>백엔드 전체 설정 (JSON 역직렬화용)</summary>
public sealed class BackendConfig
{
    /// <summary>AgentRole(camelCase) → 백엔드 매핑</summary>
    public Dictionary<string, AgentBackendMapping> AgentBackends { get; init; } = new();

    /// <summary>백엔드 ID → 백엔드 정의</summary>
    public Dictionary<string, BackendDefinition> Backends { get; init; } = new();
}

/// <summary>개별 백엔드 정의</summary>
public sealed class BackendDefinition
{
    public required string Command { get; init; }
    public int IdleTimeoutSeconds { get; init; } = 300;
    public int HardTimeoutSeconds { get; init; } = 1800;
    public int MaxRetries { get; init; } = 2;
    public string? DefaultMode { get; init; }
    public List<string>? AllowedTools { get; init; }
}

/// <summary>역할별 백엔드 매핑</summary>
public sealed class AgentBackendMapping
{
    public required string Backend { get; init; }
}
