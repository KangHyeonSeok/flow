using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>AC precheck + spec validation 더미 agent</summary>
public sealed class DummySpecValidator : IAgentAdapter
{
    public AgentRole Role => AgentRole.SpecValidator;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        FlowEvent proposedEvent;

        if (input.Spec.State == FlowState.Draft)
        {
            // AC precheck: 항상 성공
            proposedEvent = FlowEvent.AcPrecheckPassed;
        }
        else if (input.Spec.State == FlowState.Review)
        {
            // Spec validation
            if (input.Spec.Id == "fixture-review-needed")
            {
                proposedEvent = FlowEvent.SpecValidationUserReviewRequested;
            }
            else
            {
                proposedEvent = FlowEvent.SpecValidationPassed;
            }
        }
        else
        {
            return Task.FromResult(new AgentOutput
            {
                Result = AgentResult.NoOp,
                BaseVersion = input.CurrentVersion,
                Message = $"unexpected state {input.Spec.State} for SpecValidator"
            });
        }

        return Task.FromResult(new AgentOutput
        {
            Result = AgentResult.Success,
            BaseVersion = input.CurrentVersion,
            ProposedEvent = proposedEvent,
            Summary = $"DummySpecValidator → {proposedEvent}"
        });
    }
}
