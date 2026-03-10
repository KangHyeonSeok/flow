namespace FlowCLI.Services;

/// <summary>
/// Resolves all standard paths in the Flow project structure.
/// Searches upward from CWD for the .flow directory to find project root.
/// </summary>
public class PathResolver
{
    public string ProjectRoot { get; }
    public string ProjectFolderName { get; }
    public string FlowRoot { get; }
    public string SharedFlowRoot { get; }
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

        ProjectFolderName = ResolveProjectFolderName(ProjectRoot);
        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        SharedFlowRoot = GetSharedProjectFlowRoot(ProjectRoot);
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
        ProjectFolderName = ResolveProjectFolderName(ProjectRoot);
        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        SharedFlowRoot = GetSharedProjectFlowRoot(ProjectRoot);
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

    public static string GetSharedProjectFlowRoot(string projectRoot)
        => Path.Combine(GetUserHomeDirectory(), ".flow", ResolveProjectFolderName(projectRoot));

    private static string ResolveProjectFolderName(string projectRoot)
    {
        var current = projectRoot;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "flow.ps1")) || File.Exists(Path.Combine(current, "flow.sh")))
                return Path.GetFileName(current);

            current = Directory.GetParent(current)?.FullName;
        }

        return Path.GetFileName(Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }
    private static string GetUserHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        throw new InvalidOperationException("Unable to resolve the current user's home directory.");
    }

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
