using FlowCore.Agents;
using FlowCore.Agents.Dummy;
using FlowCore.Fixtures;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

public class FlowRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly FixtureInitializer _initializer;
    private readonly RunnerConfig _config;
    private readonly FakeTimeProvider _time;

    public FlowRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-runner-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("fixture-project", _tempDir);
        _initializer = new FixtureInitializer(_store);
        _config = new RunnerConfig { PollIntervalSeconds = 1, MaxSpecsPerCycle = 20 };
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FlowRunner CreateRunner(IEnumerable<IAgentAdapter>? agents = null)
    {
        agents ??= new IAgentAdapter[]
        {
            new DummySpecValidator(),
            new DummyArchitect(),
            new DummyDeveloper(),
            new DummyTestValidator(),
            new DummyPlanner()
        };
        return new FlowRunner(_store, agents, _config, _time);
    }

    [Fact]
    public async Task RunOnce_EmptyStore_ReturnsZero()
    {
        var runner = CreateRunner();
        var count = await runner.RunOnceAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task TwoPass_ArchitectureReview_PendingToInProgress()
    {
        // Single spec at ArchitectureReview/Pending with no active assignment
        var spec = new Spec
        {
            Id = "test-2pass", ProjectId = "fixture-project", Title = "2-pass test",
            State = FlowState.ArchitectureReview, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.Medium,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("test-2pass");
        // After 2-pass + architect agent: should have moved past ArchitectureReview
        // DummyArchitect returns Passed, so it should be at Implementation/Pending
        updated!.State.Should().Be(FlowState.Implementation);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task DraftPending_NoTwoPass_DirectAgentCall()
    {
        var spec = new Spec
        {
            Id = "test-draft", ProjectId = "fixture-project", Title = "draft test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("test-draft");
        // AcPrecheckPassed → Queued/Pending, then rule-only AssignmentStarted → Implementation/Pending
        // Then 2-pass + Developer → TestValidation/Pending... etc
        // At minimum it should be past Draft
        updated!.State.Should().NotBe(FlowState.Draft);
    }

    [Fact]
    public async Task ReviewInReview_NoTwoPass_SpecValidatorRuns()
    {
        var spec = new Spec
        {
            Id = "test-review", ProjectId = "fixture-project", Title = "review test",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("test-review");
        // DummySpecValidator returns SpecValidationPassed → Active/Done
        updated!.State.Should().Be(FlowState.Active);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public async Task StaleAssignment_IsTimedOut()
    {
        var spec = new Spec
        {
            Id = "test-stale", ProjectId = "fixture-project", Title = "stale test",
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // Create a stale assignment (started 2 hours ago, timeout 1 hour)
        var asg = new Assignment
        {
            Id = "asg-stale", SpecId = "test-stale",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running,
            StartedAt = _time.GetUtcNow().AddHours(-2),
            TimeoutSeconds = 3600
        };
        await ((IAssignmentStore)_store).SaveAsync(asg);
        spec.Assignments = ["asg-stale"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        // After timeout → Error → Resumed → Pending → 2-pass + Developer...
        var updated = await _store.LoadAsync("test-stale");
        updated.Should().NotBeNull();
        // Should have progressed past the stale state
        // At minimum, the stale assignment should be failed
        var asgs = await ((IAssignmentStore)_store).LoadBySpecAsync("test-stale");
        asgs.Any(a => a.Id == "asg-stale" && a.Status == AssignmentStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public async Task UpstreamIncomplete_SkipsDownstream()
    {
        // Create upstream at Implementation (incomplete)
        var upstream = new Spec
        {
            Id = "test-upstream", ProjectId = "fixture-project", Title = "upstream",
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(upstream, 0);

        // Create downstream with dependency
        var downstream = new Spec
        {
            Id = "test-downstream", ProjectId = "fixture-project", Title = "downstream",
            State = FlowState.Queued, ProcessingStatus = ProcessingStatus.Pending,
            Dependencies = new Dependency { DependsOn = ["test-upstream"] },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(downstream, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        // Downstream should still be Queued/Pending (skipped due to upstream guard)
        var updated = await _store.LoadAsync("test-downstream");
        updated!.State.Should().Be(FlowState.Queued);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task RetryExceeded_GoesToFailed()
    {
        await _initializer.InitializeAsync();

        var runner = CreateRunner();
        // fixture-retry-exceeded: ArchitectureReview/Pending, retryCount=3
        // DummyArchitect returns ArchitectReviewRejected → retryCount becomes 4 > Max(3)
        for (int i = 0; i < 5; i++)
            await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("fixture-retry-exceeded");
        spec!.State.Should().Be(FlowState.Failed);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    [Fact]
    public async Task DependencyPair_DownstreamSkipped()
    {
        await _initializer.InitializeAsync();

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        // fixture-dep-downstream depends on fixture-dep-upstream which is Implementation/InProgress
        var downstream = await _store.LoadAsync("fixture-dep-downstream");
        downstream!.State.Should().Be(FlowState.Queued);
    }

    [Fact]
    public async Task ReviewRequestTimeout_TransitionsToFailed()
    {
        var spec = new Spec
        {
            Id = "test-rr-timeout", ProjectId = "fixture-project", Title = "rr timeout",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.UserReview,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var rr = new ReviewRequest
        {
            Id = "rr-expired", SpecId = "test-rr-timeout",
            CreatedBy = "spec-validator",
            CreatedAt = _time.GetUtcNow().AddDays(-2),
            Status = ReviewRequestStatus.Open,
            DeadlineAt = _time.GetUtcNow().AddHours(-1) // expired
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-expired"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("test-rr-timeout");
        updated!.State.Should().Be(FlowState.Failed);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    [Fact]
    public async Task DependencyCascade_UpstreamFailed_BlocksDownstream()
    {
        // upstream: Review/InReview → will pass → Active/Done
        // But we want to test cascade when upstream fails
        var upstream = new Spec
        {
            Id = "test-cascade-up", ProjectId = "fixture-project", Title = "cascade upstream",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.UserReview,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(upstream, 0);

        // expired review request → will cause timeout → Failed
        var rr = new ReviewRequest
        {
            Id = "rr-cascade", SpecId = "test-cascade-up",
            CreatedBy = "spec-validator",
            CreatedAt = _time.GetUtcNow().AddDays(-2),
            Status = ReviewRequestStatus.Open,
            DeadlineAt = _time.GetUtcNow().AddHours(-1)
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        upstream.ReviewRequestIds = ["rr-cascade"];
        await _store.SaveAsync(upstream, 1);

        // downstream depending on upstream
        var downstream = new Spec
        {
            Id = "test-cascade-down", ProjectId = "fixture-project", Title = "cascade downstream",
            State = FlowState.Queued, ProcessingStatus = ProcessingStatus.Pending,
            Dependencies = new Dependency { DependsOn = ["test-cascade-up"] },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(downstream, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        // Upstream should be Failed (review request timed out)
        var upResult = await _store.LoadAsync("test-cascade-up");
        upResult!.State.Should().Be(FlowState.Failed);

        // Downstream should have received DependencyFailed cascade → OnHold
        var downResult = await _store.LoadAsync("test-cascade-down");
        downResult!.ProcessingStatus.Should().Be(ProcessingStatus.OnHold);
    }
}
