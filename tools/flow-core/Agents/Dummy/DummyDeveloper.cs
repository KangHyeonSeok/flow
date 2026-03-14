using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>Developer 더미 agent: 항상 ImplementationSubmitted</summary>
public sealed class DummyDeveloper : IAgentAdapter
{
    public AgentRole Role => AgentRole.Developer;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.ImplementationSubmitted,
            Summary = "DummyDeveloper → ImplementationSubmitted"
        });
    }
}
