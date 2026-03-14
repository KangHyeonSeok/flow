using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>테스트 우선순위 9-13: dependency, timeout, rollback</summary>
public class RuleEvaluatorDependencyAndTimeoutTests
{
    // ── 테스트 9: dependency cascade ──

    [Fact]
    public void DependencyBlocked_SetsOnHold()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyBlocked));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.OnHold);
    }

    [Fact]
    public void DependencyBlocked_CompletedSpec_Rejected()
    {
        var spec = CreateSpec(FlowState.Completed, ProcessingStatus.Done);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyBlocked));

        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public void DependencyFailed_SetsOnHoldAndCreatesReviewRequest()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyFailed));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.OnHold);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CreateReviewRequest);
    }

    // ── 테스트 10: 보류 복원 ──

    [Fact]
    public void DependencyResolved_NonReview_RestoresToPending()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.OnHold);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyResolved));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public void DependencyResolved_Review_RestoresToInReview()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.OnHold);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyResolved));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);
    }

    [Fact]
    public void DependencyResolved_NotOnHold_Rejected()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DependencyResolved));

        result.Accepted.Should().BeFalse();
    }

    // ── 테스트 11: assignment timeout ──

    [Fact]
    public void AssignmentTimedOut_SetsError()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg = CreateAssignment(status: AssignmentStatus.Running);
        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.AssignmentTimedOut, assignments: [asg]));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.Error);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.FailAssignment);
    }

    [Fact]
    public void AssignmentTimedOut_NotInProgress_Rejected()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentTimedOut));

        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public void AssignmentResumed_FromError_ToPending()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.Error);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentResumed));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    // ── 테스트 12: review request deadline 초과 ──

    [Fact]
    public void ReviewRequestTimedOut_GoesToFailed()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.UserReview);
        var rr = CreateReviewRequest(status: ReviewRequestStatus.Open);
        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ReviewRequestTimedOut, reviewRequests: [rr]));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Failed);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Error);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CloseReviewRequest);
    }

    [Fact]
    public void ReviewRequestTimedOut_NotUserReview_Rejected()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.ReviewRequestTimedOut));

        result.Accepted.Should().BeFalse();
    }

    // ── 테스트 13: 활성 상태 rollback ──

    [Fact]
    public void RollbackRequested_Active_GoesToReview()
    {
        var spec = CreateSpec(FlowState.Active, ProcessingStatus.Done);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.RollbackRequested));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Review);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);
        result.SideEffects.Should().Contain(e => e.Kind == SideEffectKind.CreateReviewRequest);
    }

    [Fact]
    public void RollbackRequested_NotActive_Rejected()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.RollbackRequested));

        result.Accepted.Should().BeFalse();
    }
}
