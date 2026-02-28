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
}
