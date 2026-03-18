using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

public sealed class DummyTestGenerator : IAgentAdapter
{
    public AgentRole Role => AgentRole.TestGenerator;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.TestGenerationCompleted,
            Summary = "DummyTestGenerator → TestGenerationCompleted"
        });
    }
}
