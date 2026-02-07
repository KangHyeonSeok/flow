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
    public string BacklogsDir { get; }
    public string ImplementsDir { get; }
    public string MetaDir { get; }
    public string CurrentStatePath { get; }
    public string SettingsPath { get; }
    public string StatesPath { get; }
    public string RagDbPath { get; }
    public string EmbedExePath { get; }

    public PathResolver()
    {
        ProjectRoot = FindProjectRoot()
            ?? throw new InvalidOperationException(
                ".flow directory not found. Are you in a Flow project?");

        FlowRoot = Path.Combine(ProjectRoot, ".flow");
        DocsDir = Path.Combine(ProjectRoot, "docs", "flow");
        BacklogsDir = Path.Combine(DocsDir, "backlogs");
        ImplementsDir = Path.Combine(DocsDir, "implements");
        MetaDir = Path.Combine(DocsDir, "meta");
        CurrentStatePath = Path.Combine(MetaDir, "current_state.json");
        SettingsPath = Path.Combine(FlowRoot, "settings.json");
        StatesPath = Path.Combine(FlowRoot, "states.json");
        RagDbPath = Path.Combine(FlowRoot, "rag", "db", "local.db");
        EmbedExePath = Path.Combine(FlowRoot, "rag", "bin", "embed.exe");
    }

    public string GetFeatureDir(string featureName)
        => Path.Combine(ImplementsDir, featureName);

    public string GetMetaDir(string featureName)
        => Path.Combine(MetaDir, featureName);

    public string GetPlanPath(string featureName)
        => Path.Combine(GetFeatureDir(featureName), "plan.md");

    public string GetResultPath(string featureName)
        => Path.Combine(GetFeatureDir(featureName), "result.md");

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
