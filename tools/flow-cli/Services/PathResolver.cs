namespace FlowCLI.Services;

/// <summary>
/// Resolves all standard paths in the Flow project structure.
/// Searches upward from CWD for the .flow directory to find project root.
/// </summary>
public class PathResolver
{
    public string ProjectRoot { get; }
    public string FlowRoot { get; }
    public string DocsDir { get; }
    public string SettingsPath { get; }

    /// <summary>.flow/config.json 경로</summary>
    public string ConfigPath { get; }

    public virtual string RagDbPath { get; }
    public string EmbedExePath { get; }

    /// <summary>.flow/broken-spec-diag.json 경로 (스펙 JSON 파싱 오류 진단 캐시)</summary>
    public string BrokenSpecDiagPath { get; }

    public PathResolver()
    {
        ProjectRoot = FindProjectRoot()
            ?? throw new InvalidOperationException(
                ".flow directory not found. Are you in a Flow project?");

        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        DocsDir = Path.Combine(ProjectRoot, "docs", "flow");
        SettingsPath = Path.Combine(FlowRoot, "settings.json");
        ConfigPath = Path.Combine(FlowRoot, "config.json");
        RagDbPath = Path.Combine(FlowRoot, "rag", "db", "local.db");
        var embedBin = OperatingSystem.IsWindows() ? "embed.exe" : "embed";
        EmbedExePath = Path.Combine(FlowRoot, "rag", "bin", embedBin);
        BrokenSpecDiagPath = Path.Combine(FlowRoot, "broken-spec-diag.json");
    }

    /// <summary>
    /// Protected constructor for testing. Accepts a project root path directly.
    /// </summary>
    protected PathResolver(string projectRoot)
    {
        ProjectRoot = projectRoot;
        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        DocsDir = Path.Combine(ProjectRoot, "docs", "flow");
        SettingsPath = Path.Combine(FlowRoot, "settings.json");
        ConfigPath = Path.Combine(FlowRoot, "config.json");
        RagDbPath = Path.Combine(FlowRoot, "rag", "db", "local.db");
        var embedBinTest = OperatingSystem.IsWindows() ? "embed.exe" : "embed";
        EmbedExePath = Path.Combine(FlowRoot, "rag", "bin", embedBinTest);
        BrokenSpecDiagPath = Path.Combine(FlowRoot, "broken-spec-diag.json");
    }

    /// <summary>
    /// 빌드 모듈 루트 디렉토리 (.flow/build/).
    /// </summary>
    public string BuildModulesDir => Path.Combine(FlowRoot, "build");

    /// <summary>
    /// 특정 플랫폼의 빌드 모듈 디렉토리 (.flow/build/{platform}/).
    /// </summary>
    public string GetBuildModulePath(string platform)
        => Path.Combine(BuildModulesDir, platform);

    /// <summary>
    /// 특정 플랫폼의 빌드 모듈 매니페스트 경로.
    /// </summary>
    public string GetBuildManifestPath(string platform)
        => Path.Combine(GetBuildModulePath(platform), "manifest.json");

    private static string? FindProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".flow")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }
}
