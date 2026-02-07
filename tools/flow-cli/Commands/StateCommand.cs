using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("state", Description = "Query or set workflow state")]
    public void State(
        [Argument(Description = "Target state to transition to")] string? target = null,
        [Option("reason", Description = "Reason for state transition")] string? reason = null,
        [Option("force", Description = "Force invalid state transition")] bool force = false,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (featureName, context) = StateService.GetCurrentState();

            if (target == null)
            {
                // Query mode — return current state info
                var stateDef = StateDefLoader.GetState(context.Phase);
                var data = new Dictionary<string, object?>
                {
                    ["state"] = context.Phase,
                    ["feature_name"] = string.IsNullOrEmpty(featureName) ? null : featureName,
                    ["started_at"] = string.IsNullOrEmpty(context.StartedAt) ? null : context.StartedAt,
                    ["allowed_commands"] = stateDef?.AllowedCommands,
                    ["allowed_transitions"] = stateDef?.Transitions,
                    ["agent_instruction"] = stateDef?.AgentInstruction
                };

                if (!string.IsNullOrEmpty(featureName))
                    data["plan_path"] = PathResolver.GetPlanPath(featureName);

                // Include retry info for VALIDATING/RETRYING states
                if (context.Phase is "VALIDATING" or "RETRYING")
                {
                    data["retry_count"] = context.RetryCount;
                    data["max_retries"] = context.MaxRetries;
                }

                JsonOutput.Write(JsonOutput.Success("state", data, $"현재 상태: {context.Phase}"), pretty);
            }
            else
            {
                // Transition mode — change state
                target = target.ToUpperInvariant();
                if (string.IsNullOrEmpty(featureName))
                    throw new InvalidOperationException(
                        "No active feature. Use pop-backlog to start a task.");

                StateMachine.Transition(featureName, target, reason ?? "", force);

                var stateDef = StateDefLoader.GetState(target);
                var data = new Dictionary<string, object?>
                {
                    ["state"] = target,
                    ["previous_state"] = context.Phase,
                    ["feature_name"] = featureName,
                    ["agent_instruction"] = stateDef?.AgentInstruction
                };

                JsonOutput.Write(JsonOutput.Success("state", data,
                    $"상태 변경: {context.Phase} → {target}"), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("state", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
