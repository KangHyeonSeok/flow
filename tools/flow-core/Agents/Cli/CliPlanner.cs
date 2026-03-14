using FlowCore.Backend;
using FlowCore.Models;

namespace FlowCore.Agents.Cli;

/// <summary>CLI 백엔드 기반 Planner IAgentAdapter 구현 (경로 A)</summary>
public sealed class CliPlanner : IAgentAdapter
{
    private readonly BackendRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly OutputParser _outputParser;

    public AgentRole Role => AgentRole.Planner;

    public CliPlanner(
        BackendRegistry registry,
        PromptBuilder promptBuilder,
        OutputParser outputParser)
    {
        _registry = registry;
        _promptBuilder = promptBuilder;
        _outputParser = outputParser;
    }

    public async Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        var backend = _registry.GetBackend(AgentRole.Planner);
        if (backend == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "no backend configured for Planner"
            };
        }

        var definition = _registry.GetDefinition(AgentRole.Planner);
        var prompt = _promptBuilder.BuildPrompt(input, AgentRole.Planner);

        var options = new CliBackendOptions
        {
            WorkingDirectory = input.Assignment.Worktree?.Path,
            AllowFileEdits = false,
            AllowedTools = definition?.AllowedTools ?? ["Read", "Glob", "Grep"],
            IdleTimeout = TimeSpan.FromSeconds(definition?.IdleTimeoutSeconds ?? 300),
            HardTimeout = TimeSpan.FromSeconds(definition?.HardTimeoutSeconds ?? 1800)
        };

        var response = await backend.RunPromptAsync(prompt, options, ct);
        var output = _outputParser.Parse(response, input);

        if (output == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.RetryableFailure,
                BaseVersion = input.CurrentVersion,
                Message = "failed to parse backend response"
            };
        }

        return output;
    }
}
