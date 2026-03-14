using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>Planner 더미 agent: DraftUpdated 또는 Failed spec 재등록 (ProposedSpec 포함)</summary>
public sealed class DummyPlanner : IAgentAdapter
{
    public AgentRole Role => AgentRole.Planner;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        // 원본 spec에서 ProposedSpec 생성 (dummy: 그대로 반환하되 contract 준수)
        var proposed = new ProposedSpecDraft
        {
            Title = input.Spec.Title,
            Type = input.Spec.Type,
            Problem = input.Spec.Problem,
            Goal = input.Spec.Goal,
            RiskLevel = input.Spec.RiskLevel,
            AcceptanceCriteria = input.Spec.AcceptanceCriteria?.Select(ac =>
                new AcceptanceCriterionDraft
                {
                    Text = ac.Text,
                    Testable = ac.Testable,
                    Notes = ac.Notes
                }).ToList(),
            DependsOn = input.Spec.Dependencies.DependsOn.Count > 0
                ? input.Spec.Dependencies.DependsOn.ToList()
                : null
        };

        if (input.Spec.State == FlowState.Failed)
        {
            return Task.FromResult(new AgentOutput
            {
                Result = AgentResult.Success,
                BaseVersion = input.CurrentVersion,
                ProposedEvent = FlowEvent.DraftCreated,
                Summary = $"DummyPlanner → re-register from failed spec {input.Spec.Id}",
                ProposedSpec = proposed
            });
        }

        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = FlowEvent.DraftUpdated,
            Summary = "DummyPlanner → DraftUpdated",
            ProposedSpec = proposed
        });
    }
}
