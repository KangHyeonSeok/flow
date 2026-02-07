using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("complete", Description = "Mark execution as complete, transition to validation")]
    public void Complete(
        [Option("summary", Description = "Summary of completed work")] string summary = "",
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (context.Phase is not ("EXECUTING" or "RETRYING"))
                throw new InvalidOperationException(
                    $"Cannot complete in {context.Phase} state. Must be EXECUTING or RETRYING.");

            if (string.IsNullOrEmpty(featureName))
                throw new InvalidOperationException("No active feature.");

            var previousState = context.Phase;
            StateMachine.Transition(featureName, "VALIDATING", summary);

            JsonOutput.Write(JsonOutput.Success("complete", new
            {
                feature_name = featureName,
                previous_state = previousState,
                state = "VALIDATING",
                summary
            }, $"검증 단계로 전이: {previousState} → VALIDATING"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("complete", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
