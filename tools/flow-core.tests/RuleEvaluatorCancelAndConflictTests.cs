using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>테스트 우선순위 14-16: cancel, version conflict, phase transition initial status</summary>
public class RuleEvaluatorCancelAndConflictTests
{
    // ── 테스트 14: 중간 상태 취소 ──

    [Theory]
    [InlineData(FlowState.Draft)]
    [InlineData(FlowState.Queued)]
    [InlineData(FlowState.ArchitectureReview)]
    [InlineData(FlowState.Implementation)]
    [InlineData(FlowState.TestValidation)]
    [InlineData(FlowState.Review)]
    public void CancelRequested_MidState_GoesToFailed(FlowState state)
    {
        var spec = CreateSpec(state, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.CancelRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Failed);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    [Fact]
    public void CancelRequested_WithAssignment_CancelsAssignment()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg = CreateAssignment(status: AssignmentStatus.Running);
        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.CancelRequested, assignments: [asg]));

        result.Accepted.Should().BeTrue();
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CancelAssignment);
    }

    [Fact]
    public void CancelRequested_Review_ClosesReviewRequest()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.UserReview);
        var rr = CreateReviewRequest(status: ReviewRequestStatus.Open);
        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.CancelRequested, reviewRequests: [rr]));

        result.Accepted.Should().BeTrue();
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CloseReviewRequest);
    }

    [Theory]
    [InlineData(FlowState.Active)]
    [InlineData(FlowState.Failed)]
    [InlineData(FlowState.Completed)]
    public void CancelRequested_ForbiddenStates_Rejected(FlowState state)
    {
        var spec = CreateSpec(state, ProcessingStatus.Done);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.CancelRequested));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ForbiddenTransition);
    }

    // ── 테스트 15: version conflict ──

    [Fact]
    public void VersionConflict_Rejected()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending, version: 2);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DraftUpdated, baseVersion: 1));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ConflictError);
    }

    [Fact]
    public void VersionMatch_Accepted()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending, version: 3);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DraftUpdated, baseVersion: 3));

        result.Accepted.Should().BeTrue();
    }

    // ── 테스트 16: phase 전환 시 processingStatus 초기값 ──

    [Fact]
    public void PhaseTransition_DraftToQueued_PendingStatus()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AcPrecheckPassed));

        result.Mutation!.NewState.Should().Be(FlowState.Queued);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public void PhaseTransition_QueuedToImplementation_PendingStatus()
    {
        var spec = CreateSpec(FlowState.Queued, ProcessingStatus.Pending, RiskLevel.Low);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));

        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public void PhaseTransition_TestValidationToReview_InReviewStatus()
    {
        var spec = CreateSpec(FlowState.TestValidation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.TestValidationPassed));

        result.Mutation!.NewState.Should().Be(FlowState.Review);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);
    }

    [Fact]
    public void PhaseTransition_ReviewToActive_DoneStatus()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationPassed));

        result.Mutation!.NewState.Should().Be(FlowState.Active);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public void PhaseTransition_ActiveToReview_Rollback_InReviewStatus()
    {
        var spec = CreateSpec(FlowState.Active, ProcessingStatus.Done);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.RollbackRequested));

        result.Mutation!.NewState.Should().Be(FlowState.Review);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);
    }

    [Fact]
    public void PhaseTransition_CancelToFailed_ErrorStatus()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.CancelRequested));

        result.Mutation!.NewState.Should().Be(FlowState.Failed);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    // ── 추가: version 증가 검증 ──

    [Fact]
    public void AcceptedResult_IncrementsVersion()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending, version: 5);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AcPrecheckPassed, baseVersion: 5));

        result.Mutation!.NewVersion.Should().Be(6);
    }

    // ── 추가: retry counter 리셋 검증 ──

    [Fact]
    public void ForwardTransition_ResetsAllCounters()
    {
        var counters = new RetryCounters
        {
            UserReviewLoopCount = 2,
            ReworkLoopCount = 1,
            ArchitectReviewLoopCount = 1
        };
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview, retryCounters: counters);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationPassed));

        result.Mutation!.NewRetryCounters!.UserReviewLoopCount.Should().Be(0);
        result.Mutation.NewRetryCounters.ReworkLoopCount.Should().Be(0);
        result.Mutation.NewRetryCounters.ArchitectReviewLoopCount.Should().Be(0);
    }

    [Fact]
    public void BackwardTransition_RetainsCounters()
    {
        var counters = new RetryCounters { ReworkLoopCount = 2 };
        var spec = CreateSpec(FlowState.TestValidation, ProcessingStatus.InProgress, retryCounters: counters);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.TestValidationRejected));

        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewRetryCounters!.ReworkLoopCount.Should().Be(2);
    }

    // ── 추가: spec_completed 선행 조건 ──

    [Fact]
    public void SpecCompleted_WithOpenReviewRequest_Rejected()
    {
        var spec = CreateSpec(FlowState.Active, ProcessingStatus.Done);
        var openReview = new ReviewRequest
        {
            Id = "rr-001",
            SpecId = "spec-001",
            Status = ReviewRequestStatus.Open
        };
        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.SpecCompleted, reviewRequests: [openReview]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.MissingPrecondition);
    }

    [Fact]
    public void SpecCompleted_NotDoneStatus_Rejected()
    {
        var spec = CreateSpec(FlowState.Active, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecCompleted));

        result.Accepted.Should().BeFalse();
    }
}

/// <summary>금지된 전이 검증</summary>
public class RuleEvaluatorForbiddenTransitionTests
{
    [Theory]
    [InlineData(FlowState.Draft, FlowEvent.ImplementationSubmitted)]
    [InlineData(FlowState.Draft, FlowEvent.TestValidationPassed)]
    [InlineData(FlowState.Draft, FlowEvent.SpecValidationPassed)]
    [InlineData(FlowState.Queued, FlowEvent.ImplementationSubmitted)]
    [InlineData(FlowState.Queued, FlowEvent.SpecCompleted)]
    [InlineData(FlowState.Implementation, FlowEvent.SpecCompleted)]
    [InlineData(FlowState.Completed, FlowEvent.DraftUpdated)]
    [InlineData(FlowState.Failed, FlowEvent.AssignmentStarted)]
    public void ForbiddenTransitions_AreRejected(FlowState state, FlowEvent ev)
    {
        var spec = CreateSpec(state, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, ev));

        result.Accepted.Should().BeFalse();
    }
}
