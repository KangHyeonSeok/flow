using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("to-blocked", Description = "Transition to BLOCKED state when unresolvable issue occurs")]
    public void ToBlocked(
        [Option("reason", Description = "Reason for blocking")] string reason = "",
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (context.Phase is "IDLE")
                throw new InvalidOperationException("Cannot transition to BLOCKED in IDLE state.");

            if (string.IsNullOrEmpty(featureName))
                throw new InvalidOperationException("No active feature.");

            var previousState = context.Phase;
            StateMachine.Transition(featureName, "BLOCKED", reason, force: true);

            JsonOutput.Write(JsonOutput.Success("to-blocked", new
            {
                feature_name = featureName,
                previous_state = previousState,
                state = "BLOCKED",
                reason
            }, $"차단 상태로 전이: {previousState} → BLOCKED"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("to-blocked", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
