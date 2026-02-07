namespace FlowCLI.Core;

/// <summary>
/// Validates commands and transitions against state definitions.
/// </summary>
public class TransitionValidator
{
    private readonly StateDefinitionLoader _definitions;

    public TransitionValidator(StateDefinitionLoader definitions)
        => _definitions = definitions;

    /// <summary>Check if a command is allowed in the given state.</summary>
    public bool IsCommandAllowed(string state, string command)
    {
        var def = _definitions.GetState(state);
        return def?.AllowedCommands.Contains(command) ?? false;
    }

    /// <summary>Check if a state transition is allowed.</summary>
    public bool IsTransitionAllowed(string fromState, string toState)
    {
        var def = _definitions.GetState(fromState);
        return def?.Transitions.Contains(toState) ?? false;
    }

    /// <summary>Get all allowed transitions from a state.</summary>
    public string[] GetAllowedTransitions(string state)
        => _definitions.GetState(state)?.Transitions.ToArray() ?? [];

    /// <summary>Get all allowed commands in a state.</summary>
    public string[] GetAllowedCommands(string state)
        => _definitions.GetState(state)?.AllowedCommands.ToArray() ?? [];
}
