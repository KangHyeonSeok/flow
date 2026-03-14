using FlowCore.Models;
using FlowCore.Serialization;
using FluentAssertions;

namespace FlowCore.Tests;

public class SpecModelTests
{
    [Fact]
    public void ToSnapshot_ConvertsCorrectly()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            RiskLevel = RiskLevel.High, Version = 5,
            Dependencies = new Dependency { DependsOn = ["spec-002", "spec-003"] },
            RetryCounters = new RetryCounters { ReworkLoopCount = 2 },
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };

        var snapshot = spec.ToSnapshot();

        snapshot.Id.Should().Be("spec-001");
        snapshot.ProjectId.Should().Be("proj-001");
        snapshot.State.Should().Be(FlowState.Implementation);
        snapshot.ProcessingStatus.Should().Be(ProcessingStatus.InProgress);
        snapshot.RiskLevel.Should().Be(RiskLevel.High);
        snapshot.DependsOn.Should().BeEquivalentTo(["spec-002", "spec-003"]);
        snapshot.Version.Should().Be(5);
        snapshot.RetryCounters.ReworkLoopCount.Should().Be(2);
    }

    [Fact]
    public void ToSnapshot_ClonesRetryCounters()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RetryCounters = new RetryCounters { UserReviewLoopCount = 1 },
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };

        var snapshot = spec.ToSnapshot();
        snapshot.RetryCounters.UserReviewLoopCount = 99;

        spec.RetryCounters.UserReviewLoopCount.Should().Be(1);
    }

    [Fact]
    public void Pruner_RemovesEmptyArrays()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().NotContain("\"assignments\"");
        json.Should().NotContain("\"reviewRequestIds\"");
        json.Should().NotContain("\"testIds\"");
    }

    [Fact]
    public void Pruner_RemovesZeroRetryCounters()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().NotContain("\"retryCounters\"");
    }

    [Fact]
    public void Pruner_KeepsNonZeroRetryCounters()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RetryCounters = new RetryCounters { ReworkLoopCount = 2 },
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().Contain("\"retryCounters\"");
        json.Should().Contain("\"reworkLoopCount\": 2");
    }

    [Fact]
    public void Pruner_KeepsRetryNotBefore()
    {
        var notBefore = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RetryCounters = new RetryCounters { RetryNotBefore = notBefore },
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().Contain("\"retryCounters\"");
        json.Should().Contain("\"retryNotBefore\"");

        var restored = SpecPruner.Deserialize(json);
        restored.RetryCounters.RetryNotBefore.Should().Be(notBefore);
    }

    [Fact]
    public void Pruner_RemovesEmptyDependencies()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().NotContain("\"dependencies\"");
    }

    [Fact]
    public void Pruner_KeepsNonEmptyDependencies()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            Dependencies = new Dependency { DependsOn = ["spec-002"] },
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().Contain("\"dependencies\"");
        json.Should().Contain("\"spec-002\"");
    }

    [Fact]
    public void Pruner_KeepsNonEmptyAssignments()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            Assignments = ["asg-001"],
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = SpecPruner.Serialize(spec);
        json.Should().Contain("\"assignments\"");
        json.Should().Contain("\"asg-001\"");
    }

    [Fact]
    public void Pruner_RoundTrip()
    {
        var original = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "한국어 테스트",
            Type = SpecType.Feature,
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            RiskLevel = RiskLevel.High,
            Dependencies = new Dependency { DependsOn = ["spec-002"], Blocks = ["spec-003"] },
            Assignments = ["asg-001"],
            ReviewRequestIds = ["rr-001"],
            RetryCounters = new RetryCounters { ReworkLoopCount = 1 },
            CreatedAt = new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 3, 14, 11, 0, 0, TimeSpan.Zero),
            Version = 3
        };

        var json = SpecPruner.Serialize(original);
        var restored = SpecPruner.Deserialize(json);

        restored.Id.Should().Be(original.Id);
        restored.Title.Should().Be("한국어 테스트");
        restored.State.Should().Be(FlowState.Implementation);
        restored.Dependencies.DependsOn.Should().BeEquivalentTo(["spec-002"]);
        restored.Assignments.Should().BeEquivalentTo(["asg-001"]);
        restored.RetryCounters.ReworkLoopCount.Should().Be(1);
        restored.Version.Should().Be(3);
    }
}
