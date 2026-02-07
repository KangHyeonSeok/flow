using System.Text.Json;
using FlowCLI.Models;
using FlowCLI.Utils;

namespace FlowCLI.Services;

/// <summary>
/// Reads and writes a single current_state.json state file.
/// Resolves the currently active feature from the current state file.
/// </summary>
public class StateService
{
    private readonly PathResolver _paths;

    public StateService(PathResolver paths) => _paths = paths;

    /// <summary>Load current_state.json.</summary>
    public ContextPhase? LoadContext()
    {
        var path = _paths.CurrentStatePath;
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ContextPhase>(json, JsonOutput.Read);
    }

    /// <summary>Save current_state.json.</summary>
    public void SaveContext(ContextPhase context)
    {
        var path = _paths.CurrentStatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(context, JsonOutput.Pretty);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Find the currently active feature.
    /// Priority: FLOW_FEATURE env var → current_state.json.feature_name.
    /// </summary>
    public string? FindActiveFeature()
    {
        var envFeature = Environment.GetEnvironmentVariable("FLOW_FEATURE");
        if (!string.IsNullOrEmpty(envFeature)) return envFeature;

        var context = LoadContext();
        if (context == null) return null;
        return string.IsNullOrEmpty(context.FeatureName) ? null : context.FeatureName;
    }

    /// <summary>
    /// Get the current state — returns active feature context or IDLE if none.
    /// </summary>
    public (string featureName, ContextPhase context) GetCurrentState()
    {
        var context = LoadContext();
        if (context == null)
            return ("", new ContextPhase { Phase = "IDLE" });

        var activeFeature = FindActiveFeature() ?? context.FeatureName;
        return (activeFeature ?? "", context);
    }
}
