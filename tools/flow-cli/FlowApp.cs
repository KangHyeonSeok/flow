using FlowCLI.Models;
using FlowCLI.Services;

namespace FlowCLI;

/// <summary>
/// Main CLI application class with lazy-initialized services.
/// Command methods are defined in partial class files under Commands/.
/// </summary>
public partial class FlowApp
{
    private PathResolver? _pathResolver;
    private PathResolver PathResolver => _pathResolver ??= new PathResolver();

    private DatabaseService? _databaseService;
    private DatabaseService DatabaseService => _databaseService ??= new DatabaseService(PathResolver);

    private EmbeddingBridge? _embeddingBridge;
    private EmbeddingBridge EmbeddingBridge => _embeddingBridge ??= new EmbeddingBridge(PathResolver);

    private FlowConfigService? _flowConfigService;
    private FlowConfigService FlowConfigService => _flowConfigService ??= new FlowConfigService(PathResolver.ConfigPath);

    /// <summary>
    /// F-006-C1: Public entry point for the legacy compatibility layer.
    /// Accepts a FlowRequest constructed from legacy positional/option args
    /// and routes it through the same JSON dispatcher path as 'invoke'.
    /// </summary>
    public void DispatchLegacy(FlowRequest request, bool pretty = false)
        => RouteRequest(request, pretty, "legacy");
}
