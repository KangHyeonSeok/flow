using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("finalize", Description = "Finalize task and return to IDLE state")]
    public void FinalizeTask(
        [Option("reason", Description = "Reason for finalization")] string reason = "",
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (context.Phase is not ("COMPLETED" or "BLOCKED"))
                throw new InvalidOperationException(
                    $"Cannot finalize in {context.Phase} state. Must be COMPLETED or BLOCKED.");

            if (string.IsNullOrEmpty(featureName))
                throw new InvalidOperationException("No active feature.");

            var previousState = context.Phase;
            StateMachine.Transition(featureName, "IDLE", reason);

            // Update backlog completion info
            var updatedContext = StateService.LoadContext();
            if (updatedContext?.Backlog != null)
            {
                updatedContext.Backlog.Active = false;
                updatedContext.Backlog.CompletedAt = DateTime.UtcNow.ToString("o");
                updatedContext.Backlog.CompletedReason = reason;
                StateService.SaveContext(updatedContext);
            }

            JsonOutput.Write(JsonOutput.Success("finalize", new
            {
                feature_name = featureName,
                previous_state = previousState,
                state = "IDLE",
                reason
            }, $"작업 종료: {previousState} → IDLE"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("finalize", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
