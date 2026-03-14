using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

public class SideEffectExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly SideEffectExecutor _executor;
    private readonly FakeTimeProvider _time;

    public SideEffectExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-se-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("test-project", _tempDir);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));
        _executor = new SideEffectExecutor(_store, new RunnerConfig(), _time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<Spec> CreateAndSaveSpec(string id = "spec-001")
    {
        var spec = new Spec
        {
            Id = id, ProjectId = "test-project", Title = "Test",
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);
        return (await _store.LoadAsync(id))!;
    }

    [Fact]
    public async Task CreateAssignment_AddsIdToSpec()
    {
        var spec = await CreateAndSaveSpec();
        var effects = new List<SideEffect>
        {
            SideEffect.CreateAssignment(AgentRole.Developer, AssignmentType.Implementation)
        };

        var result = await _executor.ExecuteAsync(effects, spec, "run-001");

        result.CreatedAssignmentIds.Should().HaveCount(1);
        spec.Assignments.Should().HaveCount(1);
        spec.Assignments[0].Should().StartWith("asg-");
    }

    [Fact]
    public async Task CreateReviewRequest_AddsIdToSpec()
    {
        var spec = await CreateAndSaveSpec();
        var effects = new List<SideEffect>
        {
            SideEffect.CreateReviewRequest("test reason")
        };

        var result = await _executor.ExecuteAsync(effects, spec, "run-001");

        result.CreatedReviewRequestIds.Should().HaveCount(1);
        spec.ReviewRequestIds.Should().HaveCount(1);
        spec.ReviewRequestIds[0].Should().StartWith("rr-");
    }

    [Fact]
    public async Task CancelAssignment_UpdatesStatus()
    {
        var spec = await CreateAndSaveSpec();
        var asg = new Assignment
        {
            Id = "asg-test", SpecId = "spec-001",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running
        };
        await ((IAssignmentStore)_store).SaveAsync(asg);

        var effects = new List<SideEffect>
        {
            SideEffect.CancelAssignment("asg-test", "test cancel")
        };

        await _executor.ExecuteAsync(effects, spec, "run-001");

        var loaded = await ((IAssignmentStore)_store).LoadAsync("spec-001", "asg-test");
        loaded!.Status.Should().Be(AssignmentStatus.Cancelled);
        loaded.CancelReason.Should().Be("test cancel");
    }

    [Fact]
    public async Task FailAssignment_UpdatesStatus()
    {
        var spec = await CreateAndSaveSpec();
        var asg = new Assignment
        {
            Id = "asg-test", SpecId = "spec-001",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running
        };
        await ((IAssignmentStore)_store).SaveAsync(asg);

        var effects = new List<SideEffect>
        {
            SideEffect.FailAssignment("asg-test", "test fail")
        };

        await _executor.ExecuteAsync(effects, spec, "run-001");

        var loaded = await ((IAssignmentStore)_store).LoadAsync("spec-001", "asg-test");
        loaded!.Status.Should().Be(AssignmentStatus.Failed);
    }

    [Fact]
    public async Task CloseReviewRequest_UpdatesStatus()
    {
        var spec = await CreateAndSaveSpec();
        var rr = new ReviewRequest
        {
            Id = "rr-test", SpecId = "spec-001",
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);

        var effects = new List<SideEffect>
        {
            SideEffect.CloseReviewRequest("rr-test", "test close")
        };

        await _executor.ExecuteAsync(effects, spec, "run-001");

        var loaded = await ((IReviewRequestStore)_store).LoadAsync("spec-001", "rr-test");
        loaded!.Status.Should().Be(ReviewRequestStatus.Closed);
    }

    [Fact]
    public async Task LogActivity_CollectsActivityEvents()
    {
        var spec = await CreateAndSaveSpec();
        var effects = new List<SideEffect>
        {
            SideEffect.Log("test message")
        };

        var result = await _executor.ExecuteAsync(effects, spec, "run-001");

        result.ActivityEvents.Should().HaveCount(1);
        result.ActivityEvents[0].Message.Should().Be("test message");
        result.ActivityEvents[0].CorrelationId.Should().Be("run-001");
    }

    [Fact]
    public async Task MultipleEffects_ExecutedInOrder()
    {
        var spec = await CreateAndSaveSpec();
        var effects = new List<SideEffect>
        {
            SideEffect.CreateAssignment(AgentRole.Developer, AssignmentType.Implementation),
            SideEffect.CreateReviewRequest("test reason"),
            SideEffect.Log("test log")
        };

        var result = await _executor.ExecuteAsync(effects, spec, "run-001");

        result.CreatedAssignmentIds.Should().HaveCount(1);
        result.CreatedReviewRequestIds.Should().HaveCount(1);
        result.ActivityEvents.Should().HaveCount(1);
        spec.Assignments.Should().HaveCount(1);
        spec.ReviewRequestIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Rollback_CancelsCreatedAssignments()
    {
        var spec = await CreateAndSaveSpec();
        var effects = new List<SideEffect>
        {
            SideEffect.CreateAssignment(AgentRole.Developer, AssignmentType.Implementation),
            SideEffect.CreateReviewRequest("test reason")
        };

        var result = await _executor.ExecuteAsync(effects, spec, "run-001");

        spec.Assignments.Should().HaveCount(1);
        spec.ReviewRequestIds.Should().HaveCount(1);

        // Simulate CAS failure → rollback
        await _executor.RollbackCreatedFilesAsync(result, spec);

        // Assignments and RR IDs should be removed from spec
        spec.Assignments.Should().BeEmpty();
        spec.ReviewRequestIds.Should().BeEmpty();

        // Created assignment should be Cancelled
        var asg = await ((IAssignmentStore)_store).LoadAsync("spec-001", result.CreatedAssignmentIds[0]);
        asg!.Status.Should().Be(AssignmentStatus.Cancelled);
        asg.CancelReason.Should().Contain("rollback");

        // Created review request should be Closed
        var rr = await ((IReviewRequestStore)_store).LoadAsync("spec-001", result.CreatedReviewRequestIds[0]);
        rr!.Status.Should().Be(ReviewRequestStatus.Closed);
        rr.Resolution.Should().Contain("rollback");
    }
}
