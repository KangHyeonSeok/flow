using FlowCore.Backend;
using FlowCore.Models;

namespace FlowCore.Agents.Cli;

/// <summary>CLI 백엔드 기반 Developer IAgentAdapter 구현 (worktree 필수)</summary>
public sealed class CliDeveloper : IAgentAdapter
{
    private readonly BackendRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly OutputParser _outputParser;

    public AgentRole Role => AgentRole.Developer;

    public CliDeveloper(
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
        // Developer는 worktree 없이 실행 불가
        if (input.Assignment.Worktree?.Path == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "Developer requires a worktree but none was assigned"
            };
        }

        var backend = _registry.GetBackend(AgentRole.Developer);
        if (backend == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "no backend configured for Developer"
            };
        }

        var definition = _registry.GetDefinition(AgentRole.Developer);
        var prompt = _promptBuilder.BuildPrompt(input, AgentRole.Developer);

        var options = new CliBackendOptions
        {
            WorkingDirectory = input.Assignment.Worktree.Path,
            AllowFileEdits = true,
            AllowedTools = definition?.AllowedTools ?? ["Read", "Write", "Edit", "Glob", "Grep", "Bash"],
            IdleTimeout = TimeSpan.FromSeconds(definition?.IdleTimeoutSeconds ?? 600),
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
