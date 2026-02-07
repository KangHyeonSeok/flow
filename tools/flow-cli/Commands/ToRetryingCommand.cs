using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("to-retrying", Description = "Transition to RETRYING state on validation failure")]
    public void ToRetrying(
        [Option("error", Description = "Error message")] string error = "",
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (context.Phase is not "VALIDATING")
                throw new InvalidOperationException(
                    $"Cannot transition to RETRYING in {context.Phase} state. Must be VALIDATING.");

            if (string.IsNullOrEmpty(featureName))
                throw new InvalidOperationException("No active feature.");

            if (context.RetryCount >= context.MaxRetries)
                throw new InvalidOperationException(
                    $"Retry limit exceeded: {context.RetryCount}/{context.MaxRetries}. Use to-blocked.");

            var previousState = context.Phase;
            StateMachine.Transition(featureName, "RETRYING", error);

            var updated = StateService.LoadContext();
            var retryCount = updated?.RetryCount ?? context.RetryCount + 1;
            var maxRetries = updated?.MaxRetries ?? context.MaxRetries;

            JsonOutput.Write(JsonOutput.Success("to-retrying", new
            {
                feature_name = featureName,
                previous_state = previousState,
                state = "RETRYING",
                retry_count = retryCount,
                max_retries = maxRetries,
                error
            }, $"재시도 모드로 전이: {previousState} → RETRYING (시도 {retryCount}/{maxRetries})"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("to-retrying", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
