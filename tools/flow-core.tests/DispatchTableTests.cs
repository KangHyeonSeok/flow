using FlowCore.Models;
using FlowCore.Runner;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

public class DispatchTableTests
{
    private static Spec MakeSpec(
        string id = "spec-001",
        FlowState state = FlowState.Draft,
        ProcessingStatus processingStatus = ProcessingStatus.Pending,
        RiskLevel riskLevel = RiskLevel.Low) => new()
    {
        Id = id, ProjectId = "proj-001", Title = "Test",
        State = state, ProcessingStatus = processingStatus,
        RiskLevel = riskLevel,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    // ── Decide tests ──

    [Fact]
    public void Draft_Pending_DispatchesSpecValidator()
    {
        var spec = MakeSpec(state: FlowState.Draft);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.SpecValidator);
        decision.AssignmentType.Should().Be(AssignmentType.AcPrecheck);
    }

    [Fact]
    public void Queued_Pending_Low_RuleOnlyToImplementation()
    {
        var spec = MakeSpec(state: FlowState.Queued, riskLevel: RiskLevel.Low);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.RuleOnly);
        decision.RuleOnlyEvent.Should().Be(FlowEvent.AssignmentStarted);
    }

    [Fact]
    public void Queued_Pending_Medium_RuleOnlyToArchitectureReview()
    {
        var spec = MakeSpec(state: FlowState.Queued, riskLevel: RiskLevel.Medium);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.RuleOnly);
        decision.RuleOnlyEvent.Should().Be(FlowEvent.AssignmentStarted);
    }

    [Fact]
    public void ArchitectureReview_Pending_DispatchesArchitect()
    {
        var spec = MakeSpec(state: FlowState.ArchitectureReview);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.Architect);
    }

    [Fact]
    public void ArchitectureReview_InProgress_WithActiveAssignment_Waits()
    {
        var spec = MakeSpec(state: FlowState.ArchitectureReview, processingStatus: ProcessingStatus.InProgress);
        var asg = new Assignment
        {
            Id = "asg-001", SpecId = "spec-001",
            AgentRole = AgentRole.Architect, Type = AssignmentType.ArchitectureReview,
            Status = AssignmentStatus.Running
        };
        var decision = DispatchTable.Decide(spec, [asg], []);
        decision.Kind.Should().Be(DispatchKind.Wait);
    }

    [Fact]
    public void Implementation_Pending_DispatchesDeveloper()
    {
        var spec = MakeSpec(state: FlowState.Implementation);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.Developer);
    }

    [Fact]
    public void TestGeneration_Pending_DispatchesTestGenerator()
    {
        var spec = MakeSpec(state: FlowState.TestGeneration);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.TestGenerator);
    }

    [Fact]
    public void Review_InReview_DispatchesSpecValidator()
    {
        var spec = MakeSpec(state: FlowState.Review, processingStatus: ProcessingStatus.InReview);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.SpecValidator);
        decision.AssignmentType.Should().Be(AssignmentType.SpecValidation);
    }

    [Fact]
    public void Review_UserReview_WithOpenRR_Waits()
    {
        var spec = MakeSpec(state: FlowState.Review, processingStatus: ProcessingStatus.UserReview);
        var rr = new ReviewRequest { Id = "rr-001", SpecId = "spec-001", Status = ReviewRequestStatus.Open };
        var decision = DispatchTable.Decide(spec, [], [rr]);
        decision.Kind.Should().Be(DispatchKind.Wait);
    }

    [Fact]
    public void Active_Done_Waits()
    {
        var spec = MakeSpec(state: FlowState.Active, processingStatus: ProcessingStatus.Done);
        var decision = DispatchTable.Decide(spec, [], []);
        decision.Kind.Should().Be(DispatchKind.Wait);
    }

    // ── ShouldExclude tests ──

    [Theory]
    [InlineData(FlowState.Failed, ProcessingStatus.Error)]
    [InlineData(FlowState.Completed, ProcessingStatus.Done)]
    public void ShouldExclude_TerminalStates(FlowState state, ProcessingStatus ps)
    {
        var spec = MakeSpec(state: state, processingStatus: ps);
        DispatchTable.ShouldExclude(spec, TimeProvider.System).Should().BeTrue();
    }

    [Fact]
    public void ShouldExclude_OnHold()
    {
        var spec = MakeSpec(state: FlowState.Implementation, processingStatus: ProcessingStatus.OnHold);
        DispatchTable.ShouldExclude(spec, TimeProvider.System).Should().BeTrue();
    }

    [Fact]
    public void ShouldExclude_RetryNotBefore_InFuture()
    {
        var spec = MakeSpec();
        spec.RetryCounters.RetryNotBefore = DateTimeOffset.UtcNow.AddMinutes(5);
        DispatchTable.ShouldExclude(spec, TimeProvider.System).Should().BeTrue();
    }

    [Fact]
    public void ShouldNotExclude_RetryNotBefore_InPast()
    {
        var spec = MakeSpec();
        spec.RetryCounters.RetryNotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        DispatchTable.ShouldExclude(spec, TimeProvider.System).Should().BeFalse();
    }

    // ── HasIncompleteUpstream tests ──

    [Fact]
    public void HasIncompleteUpstream_AllCompleted_ReturnsFalse()
    {
        var downstream = MakeSpec(id: "ds");
        downstream.Dependencies = new Dependency { DependsOn = ["us-1", "us-2"] };
        var allSpecs = new Dictionary<string, Spec>
        {
            ["us-1"] = MakeSpec(id: "us-1", state: FlowState.Active, processingStatus: ProcessingStatus.Done),
            ["us-2"] = MakeSpec(id: "us-2", state: FlowState.Completed, processingStatus: ProcessingStatus.Done)
        };

        DispatchTable.HasIncompleteUpstream(downstream, allSpecs).Should().BeFalse();
    }

    [Fact]
    public void HasIncompleteUpstream_OneInProgress_ReturnsTrue()
    {
        var downstream = MakeSpec(id: "ds");
        downstream.Dependencies = new Dependency { DependsOn = ["us-1"] };
        var allSpecs = new Dictionary<string, Spec>
        {
            ["us-1"] = MakeSpec(id: "us-1", state: FlowState.Implementation, processingStatus: ProcessingStatus.InProgress)
        };

        DispatchTable.HasIncompleteUpstream(downstream, allSpecs).Should().BeTrue();
    }

    // ── SortBacklog test ──

    [Fact]
    public void SortBacklog_OrdersByProgressThenUpdatedAt()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));

        var s1 = MakeSpec(id: "draft", state: FlowState.Draft);
        s1.UpdatedAt = fakeTime.GetUtcNow().AddMinutes(-10);

        var s2 = MakeSpec(id: "impl", state: FlowState.Implementation);
        s2.UpdatedAt = fakeTime.GetUtcNow().AddMinutes(-5);

        var s3 = MakeSpec(id: "review", state: FlowState.Review, processingStatus: ProcessingStatus.InReview);
        s3.UpdatedAt = fakeTime.GetUtcNow();

        var assignments = new Dictionary<string, IReadOnlyList<Assignment>>
        {
            ["draft"] = [],
            ["impl"] = [],
            ["review"] = []
        };

        var sorted = DispatchTable.SortBacklog([s1, s2, s3], assignments, fakeTime);
        sorted.Select(s => s.Id).Should().ContainInOrder("review", "impl", "draft");
    }
}
