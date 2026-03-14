using FlowCore.Models;

namespace FlowCore.Backend;

/// <summary>AgentRole → ICliBackend 매핑 레지스트리</summary>
public sealed class BackendRegistry
{
    private readonly BackendConfig _config;
    private readonly IReadOnlyDictionary<string, ICliBackend> _backends;

    /// <summary>
    /// 외부에서 생성한 백엔드 인스턴스를 직접 주입.
    /// 테스트나 커스텀 백엔드 사용 시.
    /// </summary>
    public BackendRegistry(
        BackendConfig config,
        IReadOnlyDictionary<string, ICliBackend> backends)
    {
        _config = config;
        _backends = backends;
    }

    /// <summary>
    /// BackendConfig로부터 백엔드 인스턴스를 자동 생성.
    /// 알려진 백엔드 ID(claude-cli, copilot-acp)는 config의 Command/MaxRetries로 생성.
    /// </summary>
    public BackendRegistry(BackendConfig config)
    {
        _config = config;
        var backends = new Dictionary<string, ICliBackend>();
        foreach (var (id, def) in config.Backends)
        {
            var backend = CreateBackend(id, def);
            if (backend != null)
                backends[id] = backend;
        }
        _backends = backends;
    }

    /// <summary>role에 매핑된 백엔드를 반환. 매핑 없거나 백엔드 없으면 null.</summary>
    public ICliBackend? GetBackend(AgentRole role)
    {
        var key = RoleToKey(role);
        if (!_config.AgentBackends.TryGetValue(key, out var mapping))
            return null;
        _backends.TryGetValue(mapping.Backend, out var backend);
        return backend;
    }

    /// <summary>role에 매핑된 백엔드 정의(타임아웃/재시도 설정)를 반환.</summary>
    public BackendDefinition? GetDefinition(AgentRole role)
    {
        var key = RoleToKey(role);
        if (!_config.AgentBackends.TryGetValue(key, out var mapping))
            return null;
        _config.Backends.TryGetValue(mapping.Backend, out var def);
        return def;
    }

    /// <summary>AgentRole → camelCase 키 변환</summary>
    private static string RoleToKey(AgentRole role)
    {
        var name = role.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>알려진 백엔드 ID로 인스턴스 생성</summary>
    private static ICliBackend? CreateBackend(string id, BackendDefinition def)
    {
        return id switch
        {
            "claude-cli" => new ClaudeCliBackend(def.Command, def.MaxRetries),
            "copilot-acp" => new CopilotAcpBackend(def.Command, def.DefaultMode ?? "code"),
            _ => null // 알 수 없는 백엔드 ID는 무시
        };
    }
}
