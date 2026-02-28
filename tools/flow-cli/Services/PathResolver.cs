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
    public virtual string RagDbPath { get; }
    public string EmbedExePath { get; }

    public PathResolver()
    {
        ProjectRoot = FindProjectRoot()
            ?? throw new InvalidOperationException(
                ".flow directory not found. Are you in a Flow project?");

        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        DocsDir = Path.Combine(ProjectRoot, "docs", "flow");
        SettingsPath = Path.Combine(FlowRoot, "settings.json");
        RagDbPath = Path.Combine(FlowRoot, "rag", "db", "local.db");
        EmbedExePath = Path.Combine(FlowRoot, "rag", "bin", "embed.exe");
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
        RagDbPath = Path.Combine(FlowRoot, "rag", "db", "local.db");
        EmbedExePath = Path.Combine(FlowRoot, "rag", "bin", "embed.exe");
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
