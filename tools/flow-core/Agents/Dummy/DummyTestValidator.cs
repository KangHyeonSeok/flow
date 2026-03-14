using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>TestValidator 더미 agent: 항상 TestValidationPassed</summary>
public sealed class DummyTestValidator : IAgentAdapter
{
    public AgentRole Role => AgentRole.TestValidator;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.TestValidationPassed,
            Summary = "DummyTestValidator → TestValidationPassed"
        });
    }
}
