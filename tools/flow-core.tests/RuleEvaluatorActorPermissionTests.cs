using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>Finding 4: Actor permission contract 검증</summary>
public class RuleEvaluatorActorPermissionTests
{
    [Theory]
    [InlineData(FlowEvent.DraftCreated, ActorKind.Developer)]
    [InlineData(FlowEvent.DraftCreated, ActorKind.Runner)]
    [InlineData(FlowEvent.DraftUpdated, ActorKind.Architect)]
    [InlineData(FlowEvent.AcPrecheckPassed, ActorKind.Planner)]
    [InlineData(FlowEvent.AcPrecheckPassed, ActorKind.Developer)]
    [InlineData(FlowEvent.ArchitectReviewPassed, ActorKind.Developer)]
    [InlineData(FlowEvent.ArchitectReviewPassed, ActorKind.SpecValidator)]
    [InlineData(FlowEvent.ImplementationSubmitted, ActorKind.Architect)]
    [InlineData(FlowEvent.ImplementationSubmitted, ActorKind.Runner)]
    [InlineData(FlowEvent.TestGenerationCompleted, ActorKind.Developer)]
    [InlineData(FlowEvent.SpecValidationPassed, ActorKind.Architect)]
    [InlineData(FlowEvent.UserReviewSubmitted, ActorKind.SpecValidator)]
    [InlineData(FlowEvent.SpecCompleted, ActorKind.Runner)]
    [InlineData(FlowEvent.CancelRequested, ActorKind.Developer)]
    [InlineData(FlowEvent.AssignmentStarted, ActorKind.Developer)]
    [InlineData(FlowEvent.AssignmentTimedOut, ActorKind.User)]
    [InlineData(FlowEvent.ReviewRequestTimedOut, ActorKind.User)]
    [InlineData(FlowEvent.RollbackRequested, ActorKind.Runner)]
    public void UnauthorizedActor_IsRejected(FlowEvent ev, ActorKind wrongActor)
    {
        // 이벤트에 맞는 valid state를 설정 (actor 검증이 state 검증보다 먼저 실행됨)
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, ev, actor: wrongActor));

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(RejectionReason.UnauthorizedActor);
    }

    [Theory]
    [InlineData(FlowEvent.DraftCreated, ActorKind.Planner)]
    [InlineData(FlowEvent.DraftUpdated, ActorKind.Planner)]
    [InlineData(FlowEvent.AcPrecheckPassed, ActorKind.SpecValidator)]
    [InlineData(FlowEvent.AssignmentStarted, ActorKind.Runner)]
    [InlineData(FlowEvent.ImplementationSubmitted, ActorKind.Developer)]
    [InlineData(FlowEvent.SpecCompleted, ActorKind.User)]
    [InlineData(FlowEvent.CancelRequested, ActorKind.User)]
    [InlineData(FlowEvent.DependencyBlocked, ActorKind.Runner)]
    [InlineData(FlowEvent.DependencyBlocked, ActorKind.SpecManager)]
    [InlineData(FlowEvent.AssignmentTimedOut, ActorKind.Runner)]
    public void AuthorizedActor_IsNotRejectedForActorReason(FlowEvent ev, ActorKind correctActor)
    {
        // state가 맞지 않아도 actor 검증은 통과해야 함 — RejectionReason이 UnauthorizedActor가 아니면 OK
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, ev, actor: correctActor));

        // actor 검증 통과 확인 (state 검증에서 reject될 수 있지만 actor reason은 아님)
        result.RejectionReason.Should().NotBe(RejectionReason.UnauthorizedActor);
    }
}
