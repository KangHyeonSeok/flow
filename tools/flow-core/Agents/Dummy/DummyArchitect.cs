using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>Architect 더미 agent: fixture-retry-exceeded만 Rejected, 나머지는 Passed</summary>
public sealed class DummyArchitect : IAgentAdapter
{
    public AgentRole Role => AgentRole.Architect;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        var proposedEvent = input.Spec.Id == "fixture-retry-exceeded"
            ? FlowEvent.ArchitectReviewRejected
            : FlowEvent.ArchitectReviewPassed;

        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = proposedEvent,
            Summary = $"DummyArchitect → {proposedEvent}"
        });
    }
}
