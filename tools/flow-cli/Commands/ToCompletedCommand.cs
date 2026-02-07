using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("to-completed", Description = "Transition to COMPLETED state after successful validation")]
    public void ToCompleted(
        [Option("validation", Description = "Validation result")] string validation = "",
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (context.Phase is not "VALIDATING")
                throw new InvalidOperationException(
                    $"Cannot transition to COMPLETED in {context.Phase} state. Must be VALIDATING.");

            if (string.IsNullOrEmpty(featureName))
                throw new InvalidOperationException("No active feature.");

            var previousState = context.Phase;
            StateMachine.Transition(featureName, "COMPLETED", validation);

            JsonOutput.Write(JsonOutput.Success("to-completed", new
            {
                feature_name = featureName,
                previous_state = previousState,
                state = "COMPLETED",
                validation
            }, $"작업 완료: {previousState} → COMPLETED"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("to-completed", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
