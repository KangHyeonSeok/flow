using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>테스트 우선순위 2-8: AC precheck, architect, rework, retry limits</summary>
public class RuleEvaluatorStateTransitionTests
{
    // ── 테스트 2: AC 프리패스 반려 ──

    [Fact]
    public void AcPrecheckRejected_StaysInDraft()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AcPrecheckRejected));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Draft);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CreateAssignment
            && e.AgentRole == AgentRole.Planner);
    }

    // ── 테스트 3: 테스트 부적합 ──

    [Fact]
    public void TestValidationRejected_BackToImplementation()
    {
        var spec = CreateSpec(FlowState.TestValidation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.TestValidationRejected));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    // ── 테스트 4: Architect 반려 후 Planner 보완 및 재검토 ──

    [Fact]
    public void ArchitectRejected_ThenDraftUpdated_ThenReReview()
    {
        // architect_review_rejected
        var spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.ArchitectReviewRejected));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.ArchitectureReview);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
        result.Mutation.NewRetryCounters!.ArchitectReviewLoopCount.Should().Be(1);

        // draft_updated → Architect 재검토
        spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.Pending, version: 2);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DraftUpdated));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.ArchitectureReview);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CreateAssignment
            && e.AgentRole == AgentRole.Architect);
    }

    // ── 테스트 5: Architect 반려 3회 초과 → 실패 ──

    [Fact]
    public void ArchitectRejected_ExceedsLimit_GoesToFailed()
    {
        var counters = new RetryCounters { ArchitectReviewLoopCount = 3 };
        var spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.InProgress,
            retryCounters: counters);

        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.ArchitectReviewRejected));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Failed);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Error);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CreateReviewRequest);
    }

    // ── 테스트 6: review request 루프 ──

    [Fact]
    public void UserReviewLoop_SubmitAndContinue()
    {
        // spec_validation_user_review_requested
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationUserReviewRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Review);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.UserReview);
        result.Mutation.NewRetryCounters!.UserReviewLoopCount.Should().Be(1);

        // user_review_submitted
        spec = CreateSpec(FlowState.Review, ProcessingStatus.UserReview, version: 2);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.UserReviewSubmitted));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);
    }

    // ── 테스트 7: review request 3회 초과 → 실패 ──

    [Fact]
    public void UserReviewLoop_ExceedsLimit_GoesToFailed()
    {
        var counters = new RetryCounters { UserReviewLoopCount = 3 };
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview,
            retryCounters: counters);

        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationUserReviewRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Failed);
    }

    // ── 테스트 8: 재작업 루프 3회 초과 → 실패 ──

    [Fact]
    public void ReworkLoop_ExceedsLimit_GoesToFailed()
    {
        var counters = new RetryCounters { ReworkLoopCount = 3 };
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview,
            retryCounters: counters);

        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationReworkRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Failed);
    }

    [Fact]
    public void ReworkLoop_WithinLimit_BackToImplementation()
    {
        var counters = new RetryCounters { ReworkLoopCount = 2 };
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview,
            retryCounters: counters);

        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationReworkRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewRetryCounters!.ReworkLoopCount.Should().Be(3);
    }
}
