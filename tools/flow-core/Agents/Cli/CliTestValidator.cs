using FlowCore.Backend;
using FlowCore.Models;

namespace FlowCore.Agents.Cli;

/// <summary>CLI 백엔드 기반 TestValidator IAgentAdapter 구현 (worktree 필수)</summary>
public sealed class CliTestValidator : IAgentAdapter
{
    private readonly BackendRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly OutputParser _outputParser;

    public AgentRole Role => AgentRole.TestValidator;

    public CliTestValidator(
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
        // TestValidator는 worktree 없이 실행 불가
        if (input.Assignment.Worktree?.Path == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "TestValidator requires a worktree but none was assigned"
            };
        }

        var backend = _registry.GetBackend(AgentRole.TestValidator);
        if (backend == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "no backend configured for TestValidator"
            };
        }

        var definition = _registry.GetDefinition(AgentRole.TestValidator);
        var prompt = _promptBuilder.BuildPrompt(input, AgentRole.TestValidator);

        var options = new CliBackendOptions
        {
            WorkingDirectory = input.Assignment.Worktree.Path,
            AllowFileEdits = false,
            AllowedTools = definition?.AllowedTools ?? ["Read", "Glob", "Grep", "Bash"],
            IdleTimeout = TimeSpan.FromSeconds(definition?.IdleTimeoutSeconds ?? 300),
            HardTimeout = TimeSpan.FromSeconds(definition?.HardTimeoutSeconds ?? 900)
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
