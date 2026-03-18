using System.Collections.Concurrent;

namespace FlowCore.Storage;

/// <summary>프로젝트별 IFlowStore 인스턴스를 생성/캐시한다.</summary>
public sealed class FlowStoreFactory
{
    private readonly string _flowHome;
    private readonly ConcurrentDictionary<string, IFlowStore> _cache = new();

    public FlowStoreFactory(string? flowHome = null)
    {
        _flowHome = flowHome
            ?? Environment.GetEnvironmentVariable("FLOW_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow");
    }

    public string FlowHome => _flowHome;

    public IFlowStore GetStore(string projectId)
        => _cache.GetOrAdd(projectId, pid => new FileFlowStore(pid, _flowHome));

    /// <summary>projects/ 디렉토리 내의 프로젝트 ID 목록을 반환한다.</summary>
    public string[] ListProjects()
    {
        var projectsDir = Path.Combine(_flowHome, "projects");
        if (!Directory.Exists(projectsDir))
            return [];
        return Directory.GetDirectories(projectsDir)
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Select(name => name!)
            .OrderBy(name => name)
            .ToArray();
    }
}
