using FlowCore.Models;

namespace FlowCore.Agents.Dummy;

/// <summary>AC precheck + spec validation 더미 agent</summary>
public sealed class DummySpecValidator : IAgentAdapter
{
    public AgentRole Role => AgentRole.SpecValidator;

    public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        FlowEvent proposedEvent;
        ProposedReviewRequest? proposedRR = null;

        if (input.Spec.State == FlowState.Draft)
        {
            // AC precheck: 항상 성공
            proposedEvent = FlowEvent.AcPrecheckPassed;
        }
        else if (input.Spec.State == FlowState.Review)
        {
            // Answered RR 이력 확인 (수렴 판단)
            var answeredRRs = input.ReviewRequests
                .Where(r => r.Status == ReviewRequestStatus.Answered)
                .ToList();

            if (input.Spec.Id.StartsWith("fixture-review-needed") && answeredRRs.Count == 0)
            {
                proposedEvent = FlowEvent.SpecValidationUserReviewRequested;
                proposedRR = MakeDummyReviewRequest("사용자 판단이 필요합니다.");
            }
            else if (input.Spec.Id.StartsWith("fixture-review-multi") && answeredRRs.Count < 2)
            {
                proposedEvent = FlowEvent.SpecValidationUserReviewRequested;
                proposedRR = MakeDummyReviewRequest($"추가 확인이 필요합니다 (round {answeredRRs.Count + 1}).");
            }
            else if (input.Spec.Id.StartsWith("fixture-review-reject"))
            {
                proposedEvent = FlowEvent.SpecValidationReworkRequested;
            }
            else if (input.Spec.Id.StartsWith("fixture-review-fail"))
            {
                proposedEvent = FlowEvent.SpecValidationFailed;
            }
            else
            {
                // 기본: 수렴 (Answered RR이 있거나 일반 spec)
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
            Summary = $"DummySpecValidator → {proposedEvent}",
            ProposedReviewRequest = proposedRR
        });
    }

    private static ProposedReviewRequest MakeDummyReviewRequest(string summary) => new()
    {
        Summary = summary,
        Questions = ["이 구현 방향이 적절합니까?"],
        Options =
        [
            new ReviewRequestOption { Id = "approve", Label = "승인", Description = "현재 방향으로 진행" },
            new ReviewRequestOption { Id = "reject", Label = "반려", Description = "피드백과 함께 재작업 요청" },
            new ReviewRequestOption { Id = "discard", Label = "폐기", Description = "이 스펙을 폐기" }
        ]
    };
}
