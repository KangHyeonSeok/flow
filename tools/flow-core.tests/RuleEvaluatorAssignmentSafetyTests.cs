using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>Finding 1: Active assignment safety — orphan assignment 방지 검증</summary>
public class RuleEvaluatorAssignmentSafetyTests
{
    // ── phase 전환 시 active assignment 거부 ──

    [Fact]
    public void AcPrecheckPassed_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var assignment = CreateAssignment(status: AssignmentStatus.Running,
            role: AgentRole.SpecValidator, type: AssignmentType.AcPrecheck);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.AcPrecheckPassed, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void ImplementationSubmitted_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var assignment = CreateAssignment(status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ImplementationSubmitted, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void ImplementationSubmitted_WithCompletedAssignment_Accepted()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var assignment = CreateAssignment(status: AssignmentStatus.Completed);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ImplementationSubmitted, assignments: [assignment]));

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public void ArchitectReviewPassed_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.InProgress);
        var assignment = CreateAssignment(status: AssignmentStatus.Running,
            role: AgentRole.Architect, type: AssignmentType.ArchitectureReview);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ArchitectReviewPassed, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void TestGenerationCompleted_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.TestGeneration, ProcessingStatus.InProgress);
        var assignment = CreateAssignment(status: AssignmentStatus.Running,
            role: AgentRole.TestGenerator, type: AssignmentType.TestGeneration);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.TestGenerationCompleted, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void SpecValidationPassed_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview);
        var assignment = CreateAssignment(status: AssignmentStatus.Running,
            role: AgentRole.SpecValidator, type: AssignmentType.SpecValidation);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.SpecValidationPassed, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void SpecCompleted_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.Active, ProcessingStatus.Done);
        var assignment = CreateAssignment(status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.SpecCompleted, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    [Fact]
    public void AssignmentStarted_Queued_WithExistingRunning_Rejected()
    {
        var spec = CreateSpec(FlowState.Queued, ProcessingStatus.Pending);
        var assignment = CreateAssignment(status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.AssignmentStarted, assignments: [assignment]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    // ── cancel/dependency 시 active assignment 대상 ID 포함 ──

    [Fact]
    public void CancelRequested_EmitsCancelWithTargetAssignmentIds()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg1 = CreateAssignment("asg-001", status: AssignmentStatus.Running);
        var asg2 = CreateAssignment("asg-002", status: AssignmentStatus.Queued);
        var asgDone = CreateAssignment("asg-003", status: AssignmentStatus.Completed);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.CancelRequested, assignments: [asg1, asg2, asgDone]));

        result.Accepted.Should().BeTrue();
        var cancelEffects = result.SideEffects
            .Where(e => e.Kind == SideEffectKind.CancelAssignment).ToList();
        cancelEffects.Should().HaveCount(2);
        cancelEffects.Select(e => e.TargetAssignmentId).Should()
            .BeEquivalentTo(["asg-001", "asg-002"]);
    }

    [Fact]
    public void CancelRequested_Review_ClosesOpenReviewRequestsWithIds()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.UserReview);
        var rr1 = CreateReviewRequest("rr-001", status: ReviewRequestStatus.Open);
        var rr2 = CreateReviewRequest("rr-002", status: ReviewRequestStatus.Answered);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.CancelRequested, reviewRequests: [rr1, rr2]));

        result.Accepted.Should().BeTrue();
        var closeEffects = result.SideEffects
            .Where(e => e.Kind == SideEffectKind.CloseReviewRequest).ToList();
        closeEffects.Should().HaveCount(1);
        closeEffects[0].TargetReviewRequestId.Should().Be("rr-001");
    }

    [Fact]
    public void DependencyBlocked_CancelsInFlightAssignments()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg = CreateAssignment("asg-running", status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.DependencyBlocked, assignments: [asg]));

        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.OnHold);
        result.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.CancelAssignment && e.TargetAssignmentId == "asg-running");
    }

    [Fact]
    public void DependencyFailed_CancelsInFlightAssignments()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg = CreateAssignment("asg-running", status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.DependencyFailed, assignments: [asg]));

        result.Accepted.Should().BeTrue();
        result.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.CancelAssignment && e.TargetAssignmentId == "asg-running");
    }

    [Fact]
    public void AssignmentTimedOut_FailsAssignmentWithTargetId()
    {
        var spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress);
        var asg = CreateAssignment("asg-timeout", status: AssignmentStatus.Running);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.AssignmentTimedOut, assignments: [asg]));

        result.Accepted.Should().BeTrue();
        result.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.FailAssignment && e.TargetAssignmentId == "asg-timeout");
    }

    // ── side effect에 specId 포함 검증 ──

    [Fact]
    public void CreateAssignment_SideEffect_ContainsSpecId()
    {
        var spec = CreateSpec(FlowState.Queued, ProcessingStatus.Pending, RiskLevel.Low, id: "spec-xyz");
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));

        result.Accepted.Should().BeTrue();
        var createEffect = result.SideEffects
            .First(e => e.Kind == SideEffectKind.CreateAssignment);
        createEffect.SpecId.Should().Be("spec-xyz");
    }

    [Fact]
    public void CreateReviewRequest_SideEffect_ContainsSpecId()
    {
        var counters = new RetryCounters { ArchitectReviewLoopCount = 3 };
        var spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.InProgress,
            id: "spec-abc", retryCounters: counters);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ArchitectReviewRejected));

        result.Accepted.Should().BeTrue();
        var reviewEffect = result.SideEffects
            .First(e => e.Kind == SideEffectKind.CreateReviewRequest);
        reviewEffect.SpecId.Should().Be("spec-abc");
        reviewEffect.Reason.Should().NotBeNullOrEmpty();
    }

    // ── DraftUpdated in ArchitectureReview with active assignment ──

    [Fact]
    public void DraftUpdated_ArchitectureReview_WithRunningAssignment_Rejected()
    {
        var spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.Pending);
        var asg = CreateAssignment(status: AssignmentStatus.Running,
            role: AgentRole.Planner, type: AssignmentType.Planning);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.DraftUpdated, assignments: [asg]));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.ActiveAssignmentExists);
    }

    // ── ReviewRequestTimedOut closes open review requests with IDs ──

    [Fact]
    public void ReviewRequestTimedOut_ClosesOpenReviewRequestsWithIds()
    {
        var spec = CreateSpec(FlowState.Review, ProcessingStatus.UserReview);
        var rr = CreateReviewRequest("rr-timeout", status: ReviewRequestStatus.Open);

        var result = RuleEvaluator.Evaluate(
            CreateInput(spec, FlowEvent.ReviewRequestTimedOut, reviewRequests: [rr]));

        result.Accepted.Should().BeTrue();
        result.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.CloseReviewRequest && e.TargetReviewRequestId == "rr-timeout");
    }
}
