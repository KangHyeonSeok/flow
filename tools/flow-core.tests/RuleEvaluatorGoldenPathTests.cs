using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;
using static FlowCore.Tests.TestHelpers;

namespace FlowCore.Tests;

/// <summary>
/// 테스트 우선순위 1: 정상 경로 (초안 → 대기 → 테스트 생성 → 구현 → 검토 → 활성 → 완료)
/// Golden path scenario A
/// </summary>
public class RuleEvaluatorGoldenPathTests
{
    [Fact]
    public void GoldenPath_LowRisk_DraftToCompleted()
    {
        // 1. 초안: draft_created
        var spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.DraftCreated));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Draft);

        // 2. 초안 → 대기: ac_precheck_passed
        spec = CreateSpec(FlowState.Draft, ProcessingStatus.Pending);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AcPrecheckPassed));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Queued);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);

        // 3. 대기 → 테스트 생성: assignment_started (low risk)
        spec = CreateSpec(FlowState.Queued, ProcessingStatus.Pending, RiskLevel.Low, 2);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.TestGeneration);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);

        // 4. 테스트 생성: assignment_started → 처리중
        spec = CreateSpec(FlowState.TestGeneration, ProcessingStatus.Pending, version: 3);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.TestGeneration);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.InProgress);

        // 5. 테스트 생성 → 구현: test_generation_completed
        spec = CreateSpec(FlowState.TestGeneration, ProcessingStatus.InProgress, version: 4);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.TestGenerationCompleted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Implementation);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);

        // 6. 구현: assignment_started → 처리중
        spec = CreateSpec(FlowState.Implementation, ProcessingStatus.Pending, version: 5);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewProcessingStatus.Should().Be(ProcessingStatus.InProgress);

        // 7. 구현 → 검토: implementation_submitted
        spec = CreateSpec(FlowState.Implementation, ProcessingStatus.InProgress, version: 6);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.ImplementationSubmitted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Review);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.InReview);

        // 8. 검토 → 활성: spec_validation_passed
        spec = CreateSpec(FlowState.Review, ProcessingStatus.InReview, version: 7);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecValidationPassed));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Active);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Done);
        result.SideEffects.Should().Contain(e => e.ActivityAction == "spec_activated");

        // 9. 활성 → 완료: spec_completed
        spec = CreateSpec(FlowState.Active, ProcessingStatus.Done, version: 8);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.SpecCompleted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.Completed);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public void GoldenPath_HighRisk_WithArchitectReview()
    {
        // 대기 → 구현 검토: assignment_started (high risk)
        var spec = CreateSpec(FlowState.Queued, ProcessingStatus.Pending, RiskLevel.High);
        var result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.AssignmentStarted));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.ArchitectureReview);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);

        // 구현 검토 → 테스트 생성: architect_review_passed
        spec = CreateSpec(FlowState.ArchitectureReview, ProcessingStatus.InProgress, RiskLevel.High, 2);
        result = RuleEvaluator.Evaluate(CreateInput(spec, FlowEvent.ArchitectReviewPassed));
        result.Accepted.Should().BeTrue();
        result.Mutation!.NewState.Should().Be(FlowState.TestGeneration);
        result.Mutation.NewProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }
}
