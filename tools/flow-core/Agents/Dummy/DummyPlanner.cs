using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>Planner 더미 agent: DraftUpdated 또는 Failed spec 재등록</summary>
public sealed class DummyPlanner : IAgentAdapter
{
    public AgentRole Role => AgentRole.Planner;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        if (input.Spec.State == FlowState.Failed)
        {
            // Failed spec 재등록: 새 spec 생성 신호
            return Task.FromResult(new AgentOutput
            {
                Result = AgentResult.Success,
                BaseVersion = input.CurrentVersion,
                ProposedEvent = FlowEvent.DraftCreated,
                Summary = $"DummyPlanner → re-register from failed spec {input.Spec.Id}"
            });
        }

        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.DraftUpdated,
            Summary = "DummyPlanner → DraftUpdated"
        });
    }
}
