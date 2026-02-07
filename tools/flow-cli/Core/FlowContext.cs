using FlowCLI.Models;
using FlowCLI.Services;

namespace FlowCLI.Core;

/// <summary>
/// Resolves the current flow context: active feature, paths, and state.
/// </summary>
public class FlowContext
{
    private readonly PathResolver _paths;
    private readonly StateService _stateService;

    public FlowContext(PathResolver paths, StateService stateService)
    {
        _paths = paths;
        _stateService = stateService;
    }

    public string ProjectRoot => _paths.ProjectRoot;
    public string FlowRoot => _paths.FlowRoot;

    /// <summary>Resolve current feature name and context phase.</summary>
    public (string featureName, ContextPhase context) Resolve()
        => _stateService.GetCurrentState();

    /// <summary>Get plan.md path if it exists.</summary>
    public string? GetPlanPath(string featureName)
    {
        var path = _paths.GetPlanPath(featureName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Get result.md path if it exists.</summary>
    public string? GetResultPath(string featureName)
    {
        var path = _paths.GetResultPath(featureName);
        return File.Exists(path) ? path : null;
    }
}
