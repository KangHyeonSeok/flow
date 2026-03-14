using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>Planner 더미 agent: 항상 DraftUpdated</summary>
public sealed class DummyPlanner : IAgentAdapter
{
    public AgentRole Role => AgentRole.Planner;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.DraftUpdated,
            Summary = "DummyPlanner → DraftUpdated"
        });
    }
}
