using FlowCLI.Core;
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

    private StateDefinitionLoader? _stateDefLoader;
    private StateDefinitionLoader StateDefLoader => _stateDefLoader ??= new StateDefinitionLoader(PathResolver);

    private StateService? _stateService;
    private StateService StateService => _stateService ??= new StateService(PathResolver);

    private StateMachine? _stateMachine;
    private StateMachine StateMachine => _stateMachine ??= new StateMachine(StateDefLoader, StateService);

    private TransitionValidator? _validator;
    private TransitionValidator Validator => _validator ??= new TransitionValidator(StateDefLoader);

    private FlowContext? _flowContext;
    private FlowContext FlowContext => _flowContext ??= new FlowContext(PathResolver, StateService);

    private BacklogService? _backlogService;
    private BacklogService BacklogService => _backlogService ??= new BacklogService(PathResolver);

    private DatabaseService? _databaseService;
    private DatabaseService DatabaseService => _databaseService ??= new DatabaseService(PathResolver);

    private EmbeddingBridge? _embeddingBridge;
    private EmbeddingBridge EmbeddingBridge => _embeddingBridge ??= new EmbeddingBridge(PathResolver);
}
