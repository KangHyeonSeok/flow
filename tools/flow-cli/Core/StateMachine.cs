using FlowCLI.Models;
using FlowCLI.Services;

namespace FlowCLI.Core;

/// <summary>
/// State transition engine. Validates transitions against states.json rules
/// and updates current_state.json with state-specific logic.
/// </summary>
public class StateMachine
{
    private readonly StateDefinitionLoader _definitions;
    private readonly StateService _stateService;

    public StateMachine(StateDefinitionLoader definitions, StateService stateService)
    {
        _definitions = definitions;
        _stateService = stateService;
    }

    /// <summary>Check if a transition from one state to another is allowed.</summary>
    public bool CanTransition(string fromState, string toState)
    {
        var def = _definitions.GetState(fromState);
        return def?.Transitions.Contains(toState) ?? false;
    }

    /// <summary>
    /// Execute a state transition for a feature.
    /// Validates the transition unless force=true, then updates current_state.json.
    /// </summary>
    public void Transition(string featureName, string toState, string reason, bool force = false)
    {
        var context = _stateService.LoadContext()
            ?? throw new InvalidOperationException("No current state found. Use pop-backlog to start.");

        var fromState = context.Phase;

        if (!force && !CanTransition(fromState, toState))
        {
            var allowed = _definitions.GetState(fromState)?.Transitions ?? [];
            throw new InvalidOperationException(
                $"Invalid transition: {fromState} → {toState}. " +
                $"Allowed: [{string.Join(", ", allowed)}]. Use --force to override.");
        }

        context.Phase = toState;
        if (string.IsNullOrEmpty(context.FeatureName) && !string.IsNullOrEmpty(featureName))
            context.FeatureName = featureName;
        context.LastDecision = new LastDecision
        {
            Action = "phase_transition",
            Reason = reason,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        // State-specific logic
        switch (toState)
        {
            case "RETRYING":
                context.RetryCount++;
                break;
            case "IDLE":
                context.RetryCount = 0;
                context.RequiresHuman = false;
                break;
            case "BLOCKED":
                context.RequiresHuman = true;
                break;
        }

        _stateService.SaveContext(context);
    }
}
