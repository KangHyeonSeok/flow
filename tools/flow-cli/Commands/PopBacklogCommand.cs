using Cocona;
using FlowCLI.Models;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("pop-backlog", Description = "Pop next task from backlog queue")]
    public void PopBacklog(
        [Option("preview", Description = "Preview without popping")] bool preview = false,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var (_, context) = StateService.GetCurrentState();
            if (context.Phase != "IDLE")
                throw new InvalidOperationException(
                    $"Cannot pop backlog in {context.Phase} state. Must be IDLE.");

            if (preview)
            {
                var entry = BacklogService.Peek();
                if (entry == null)
                {
                    JsonOutput.Write(JsonOutput.Success("pop-backlog",
                        new { queue_remaining = 0 },
                        "큐가 비어있습니다."), pretty);
                    return;
                }

                JsonOutput.Write(JsonOutput.Success("pop-backlog", new
                {
                    feature_name = entry.FeatureName,
                    needs_review = entry.NeedsReview,
                    queue_remaining = BacklogService.GetQueue().Count
                }, $"다음 작업: {entry.FeatureName}"), pretty);
            }
            else
            {
                var entry = BacklogService.Pop();
                if (entry == null)
                {
                    JsonOutput.Write(JsonOutput.Success("pop-backlog",
                        new { queue_remaining = 0 },
                        "큐가 비어있습니다. 설계 에이전트에게 핸드오프하세요."), pretty);
                    return;
                }

                // Create current_state for the popped feature → EXECUTING
                var featureName = entry.FeatureName;
                var now = DateTime.UtcNow.ToString("o");
                var newContext = new ContextPhase
                {
                    Phase = "EXECUTING",
                    StartedAt = now,
                    FeatureName = featureName,
                    Backlog = new BacklogInfo
                    {
                        IsBacklog = true,
                        Active = true,
                        Source = "backlog-queue",
                        StartedAt = now
                    },
                    LastDecision = new LastDecision
                    {
                        Action = "phase_transition",
                        Reason = "pop-backlog",
                        Timestamp = now
                    }
                };

                StateService.SaveContext(newContext);

                JsonOutput.Write(JsonOutput.Success("pop-backlog", new
                {
                    feature_name = featureName,
                    state = "EXECUTING",
                    plan_path = PathResolver.GetPlanPath(featureName),
                    queue_remaining = BacklogService.GetQueue().Count
                }, $"작업 시작: {featureName}"), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("pop-backlog", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
