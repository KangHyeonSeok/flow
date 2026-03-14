using FlowCore.Agents;
using FlowCore.Agents.Dummy;
using FlowCore.Fixtures;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

public class GoldenScenarioTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly RunnerConfig _config;
    private readonly FakeTimeProvider _time;

    public GoldenScenarioTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-golden-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("fixture-project", _tempDir);
        _config = new RunnerConfig { PollIntervalSeconds = 1, MaxSpecsPerCycle = 20 };
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FlowRunner CreateRunner() => new(
        _store,
        new IAgentAdapter[]
        {
            new DummySpecValidator(),
            new DummyArchitect(),
            new DummyDeveloper(),
            new DummyTestValidator(),
            new DummyPlanner()
        },
        _config, _time);

    /// <summary>activity log에서 StateTransitionCommitted 이벤트의 message를 시간순으로 반환</summary>
    private async Task<List<string>> GetCommittedTransitionMessages(string specId)
    {
        var activities = await ((IActivityStore)_store).LoadRecentAsync(specId, 200);
        return activities
            .Reverse()
            .Where(a => a.Action == ActivityAction.StateTransitionCommitted)
            .Select(a => a.Message)
            .ToList();
    }

    [Fact]
    public async Task GoldenScenario_HappyPath()
    {
        // fixture-happy-path: Draft/Pending, Low risk
        // Expected: Draft → Queued → Implementation → TestValidation → Review → Active
        var spec = new Spec
        {
            Id = "fixture-happy-path", ProjectId = "fixture-project",
            Title = "Happy Path", Type = SpecType.Task,
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.Low,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        for (int i = 0; i < 20; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-happy-path");
        final!.State.Should().Be(FlowState.Active);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Done);

        // Verify activity log contains key FlowEvent transitions in order
        var activities = await ((IActivityStore)_store).LoadRecentAsync("fixture-happy-path", 200);
        var committedActions = activities
            .Reverse()
            .Where(a => a.Action == ActivityAction.StateTransitionCommitted)
            .Select(a => a.Message)
            .ToList();

        // Happy path full sequence including 2-pass AssignmentStarted:
        //   AcPrecheckPassed → AssignmentStarted (Queued→Impl/Pending) →
        //   AssignmentStarted (Impl Pending→InProgress) → ImplementationSubmitted →
        //   AssignmentStarted (TestVal Pending→InProgress) → TestValidationPassed →
        //   SpecValidationPassed
        committedActions.Should().HaveCount(7);

        // Verify ordered sequence: key phase transitions appear in correct order
        var acIdx = committedActions.FindIndex(m => m.Contains("AcPrecheckPassed"));
        var implStartIdx = committedActions.FindIndex(m => m.Contains("AssignmentStarted") && m.Contains("Implementation/Pending"));
        var implInProgressIdx = committedActions.FindIndex(m => m.Contains("AssignmentStarted") && m.Contains("Implementation/InProgress"));
        var implSubmitIdx = committedActions.FindIndex(m => m.Contains("ImplementationSubmitted"));
        var testStartIdx = committedActions.FindIndex(m => m.Contains("AssignmentStarted") && m.Contains("TestValidation/InProgress"));
        var testPassIdx = committedActions.FindIndex(m => m.Contains("TestValidationPassed"));
        var specPassIdx = committedActions.FindIndex(m => m.Contains("SpecValidationPassed"));

        acIdx.Should().BeGreaterOrEqualTo(0);
        acIdx.Should().BeLessThan(implStartIdx);
        implStartIdx.Should().BeLessThan(implInProgressIdx);
        implInProgressIdx.Should().BeLessThan(implSubmitIdx);
        implSubmitIdx.Should().BeLessThan(testStartIdx);
        testStartIdx.Should().BeLessThan(testPassIdx);
        testPassIdx.Should().BeLessThan(specPassIdx);
    }

    [Fact]
    public async Task GoldenScenario_ArchitectReview()
    {
        // fixture-architect-review: Draft/Pending, Medium risk
        // Expected: Draft → Queued → ArchitectureReview → Implementation → TestValidation → Review → Active
        var spec = new Spec
        {
            Id = "fixture-architect-review", ProjectId = "fixture-project",
            Title = "Architect Review", Type = SpecType.Task,
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.Medium,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        for (int i = 0; i < 20; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-architect-review");
        final!.State.Should().Be(FlowState.Active);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Done);

        // Verify key transitions in order
        var committedMessages = await GetCommittedTransitionMessages("fixture-architect-review");
        committedMessages.Should().Contain(m => m.Contains("AcPrecheckPassed"));
        committedMessages.Should().Contain(m => m.Contains("ArchitectReviewPassed"));
        committedMessages.Should().Contain(m => m.Contains("ImplementationSubmitted"));
        committedMessages.Should().Contain(m => m.Contains("TestValidationPassed"));
        committedMessages.Should().Contain(m => m.Contains("SpecValidationPassed"));

        // ArchitectReviewPassed must come before ImplementationSubmitted
        var archIdx = committedMessages.FindIndex(m => m.Contains("ArchitectReviewPassed"));
        var implIdx = committedMessages.FindIndex(m => m.Contains("ImplementationSubmitted"));
        archIdx.Should().BeLessThan(implIdx);
    }

    [Fact]
    public async Task GoldenScenario_StaleAssignment()
    {
        // fixture-stale-assignment: Implementation/InProgress + stale assignment
        var spec = new Spec
        {
            Id = "fixture-stale-assignment", ProjectId = "fixture-project",
            Title = "Stale Assignment", Type = SpecType.Task,
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var asg = new Assignment
        {
            Id = "asg-stale-001", SpecId = "fixture-stale-assignment",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running,
            StartedAt = _time.GetUtcNow().AddHours(-2),
            TimeoutSeconds = 3600
        };
        await ((IAssignmentStore)_store).SaveAsync(asg);
        spec.Assignments = ["asg-stale-001"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        for (int i = 0; i < 20; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-stale-assignment");
        // Should recover from timeout and eventually reach Active/Done
        final!.State.Should().Be(FlowState.Active);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Done);

        // Original stale assignment should be Failed
        var staleAsg = await ((IAssignmentStore)_store).LoadAsync("fixture-stale-assignment", "asg-stale-001");
        staleAsg!.Status.Should().Be(AssignmentStatus.Failed);

        // Verify timeout → resume → completion sequence
        var committedMessages = await GetCommittedTransitionMessages("fixture-stale-assignment");
        committedMessages.Should().Contain(m => m.Contains("AssignmentTimedOut"));
        committedMessages.Should().Contain(m => m.Contains("AssignmentResumed"));
        committedMessages.Should().Contain(m => m.Contains("ImplementationSubmitted"));

        var timeoutIdx = committedMessages.FindIndex(m => m.Contains("AssignmentTimedOut"));
        var resumeIdx = committedMessages.FindIndex(m => m.Contains("AssignmentResumed"));
        var submitIdx = committedMessages.FindIndex(m => m.Contains("ImplementationSubmitted"));
        timeoutIdx.Should().BeLessThan(resumeIdx);
        resumeIdx.Should().BeLessThan(submitIdx);
    }

    [Fact]
    public async Task GoldenScenario_RetryExceeded()
    {
        // fixture-retry-exceeded: ArchitectureReview/Pending, retryCount=3, High risk
        var spec = new Spec
        {
            Id = "fixture-retry-exceeded", ProjectId = "fixture-project",
            Title = "Retry Exceeded", Type = SpecType.Task,
            State = FlowState.ArchitectureReview, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.High,
            RetryCounters = new RetryCounters { ArchitectReviewLoopCount = 3 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        for (int i = 0; i < 10; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-retry-exceeded");
        final!.State.Should().Be(FlowState.Failed);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Error);
        final.RetryCounters.ArchitectReviewLoopCount.Should().Be(4);

        // Verify ArchitectReviewRejected triggered the failure
        var committedMessages = await GetCommittedTransitionMessages("fixture-retry-exceeded");
        committedMessages.Should().Contain(m => m.Contains("ArchitectReviewRejected"));
    }

    [Fact]
    public async Task GoldenScenario_DependencyPair()
    {
        // Upstream at Implementation/InProgress, downstream Queued with dependency
        var upstream = new Spec
        {
            Id = "fixture-dep-upstream", ProjectId = "fixture-project",
            Title = "Upstream", Type = SpecType.Task,
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(upstream, 0);

        // Create a running assignment for upstream so it looks in-progress
        var upAsg = new Assignment
        {
            Id = "asg-up-001", SpecId = "fixture-dep-upstream",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running,
            StartedAt = _time.GetUtcNow(),
            TimeoutSeconds = 3600
        };
        await ((IAssignmentStore)_store).SaveAsync(upAsg);
        upstream.Assignments = ["asg-up-001"];
        await _store.SaveAsync(upstream, 1);

        var downstream = new Spec
        {
            Id = "fixture-dep-downstream", ProjectId = "fixture-project",
            Title = "Downstream", Type = SpecType.Task,
            State = FlowState.Queued, ProcessingStatus = ProcessingStatus.Pending,
            Dependencies = new Dependency { DependsOn = ["fixture-dep-upstream"] },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(downstream, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        // Downstream should still be Queued because upstream is not Active/Completed
        var downResult = await _store.LoadAsync("fixture-dep-downstream");
        downResult!.State.Should().Be(FlowState.Queued);
        downResult.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task GoldenScenario_ReviewRequestTimeout()
    {
        // Review/UserReview with an expired review request
        var spec = new Spec
        {
            Id = "fixture-rr-timeout", ProjectId = "fixture-project",
            Title = "RR Timeout", Type = SpecType.Task,
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.UserReview,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var rr = new ReviewRequest
        {
            Id = "rr-timeout-001", SpecId = "fixture-rr-timeout",
            CreatedBy = "spec-validator",
            CreatedAt = _time.GetUtcNow().AddDays(-2),
            Status = ReviewRequestStatus.Open,
            DeadlineAt = _time.GetUtcNow().AddHours(-1) // already expired
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-timeout-001"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-rr-timeout");
        final!.State.Should().Be(FlowState.Failed);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Error);

        // Verify the timeout was logged
        var committedMessages = await GetCommittedTransitionMessages("fixture-rr-timeout");
        committedMessages.Should().Contain(m => m.Contains("ReviewRequestTimedOut"));
    }

    [Fact]
    public async Task GoldenScenario_AgentBaseVersionMismatch()
    {
        // Agent returns a BaseVersion that doesn't match spec.Version
        var spec = new Spec
        {
            Id = "fixture-version-mismatch", ProjectId = "fixture-project",
            Title = "Version Mismatch", Type = SpecType.Task,
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // Create a SpecValidator that returns wrong BaseVersion
        var badAgent = new StaleVersionSpecValidator();
        var runner = new FlowRunner(
            _store,
            new IAgentAdapter[]
            {
                badAgent,
                new DummyArchitect(),
                new DummyDeveloper(),
                new DummyTestValidator(),
                new DummyPlanner()
            },
            _config, _time);

        await runner.RunOnceAsync();

        // Spec should still be at Draft because the agent's BaseVersion was wrong
        var final = await _store.LoadAsync("fixture-version-mismatch");
        final!.State.Should().Be(FlowState.Draft);

        // Verify EventRejected was logged
        var activities = await ((IActivityStore)_store).LoadRecentAsync("fixture-version-mismatch", 100);
        activities.Should().Contain(a =>
            a.Action == ActivityAction.EventRejected
            && a.Message.Contains("BaseVersion mismatch"));
    }

    [Fact]
    public async Task GoldenScenario_ReviewLoop_SingleRound()
    {
        // fixture-review-needed prefix → DummySpecValidator requests user review once
        // After approve response → SpecValidationPassed (converges)
        var spec = new Spec
        {
            Id = "fixture-review-needed-golden1", ProjectId = "fixture-project",
            Title = "Single Round Review", Type = SpecType.Task,
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.Low,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();

        // Run until Review/UserReview (spec validator will request user review)
        for (int i = 0; i < 20; i++)
            await runner.RunOnceAsync();

        var midSpec = await _store.LoadAsync("fixture-review-needed-golden1");
        midSpec!.State.Should().Be(FlowState.Review);
        midSpec.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        // Find the open RR and submit approve response
        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed-golden1");
        var openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);

        await runner.SubmitReviewResponseAsync(
            "fixture-review-needed-golden1", openRR.Id,
            new ReviewResponse
            {
                RespondedBy = "user", RespondedAt = _time.GetUtcNow(),
                Type = ReviewResponseType.ApproveOption,
                SelectedOptionId = "approve", Comment = "LGTM"
            });

        // Run again → SpecValidator sees answeredRRs.Count >= 1 → SpecValidationPassed → Active/Done
        for (int i = 0; i < 5; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-review-needed-golden1");
        final!.State.Should().Be(FlowState.Active);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Done);

        // Verify transition sequence includes UserReview phase
        var committedMessages = await GetCommittedTransitionMessages("fixture-review-needed-golden1");
        committedMessages.Should().Contain(m => m.Contains("SpecValidationUserReviewRequested"));
        committedMessages.Should().Contain(m => m.Contains("UserReviewSubmitted"));
        committedMessages.Should().Contain(m => m.Contains("SpecValidationPassed"));

        var reviewReqIdx = committedMessages.FindIndex(m => m.Contains("SpecValidationUserReviewRequested"));
        var reviewSubIdx = committedMessages.FindIndex(m => m.Contains("UserReviewSubmitted"));
        var passIdx = committedMessages.FindIndex(m => m.Contains("SpecValidationPassed"));
        reviewReqIdx.Should().BeLessThan(reviewSubIdx);
        reviewSubIdx.Should().BeLessThan(passIdx);
    }

    [Fact]
    public async Task GoldenScenario_ReviewLoop_MultiRound()
    {
        // fixture-review-multi prefix → DummySpecValidator needs 2 answered RRs to converge
        var spec = new Spec
        {
            Id = "fixture-review-multi-golden", ProjectId = "fixture-project",
            Title = "Multi Round Review", Type = SpecType.Task,
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            RiskLevel = RiskLevel.Low,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();

        // Run until first UserReview
        for (int i = 0; i < 20; i++)
            await runner.RunOnceAsync();

        var mid1 = await _store.LoadAsync("fixture-review-multi-golden");
        mid1!.State.Should().Be(FlowState.Review);
        mid1.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        // Round 1: submit approve
        var rrs1 = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-multi-golden");
        var open1 = rrs1.First(r => r.Status == ReviewRequestStatus.Open);
        await runner.SubmitReviewResponseAsync(
            "fixture-review-multi-golden", open1.Id,
            new ReviewResponse
            {
                RespondedBy = "user", RespondedAt = _time.GetUtcNow(),
                Type = ReviewResponseType.ApproveOption,
                SelectedOptionId = "approve", Comment = "Round 1 OK"
            });

        // Run → SpecValidator sees 1 answered, needs 2 → requests again
        for (int i = 0; i < 5; i++)
            await runner.RunOnceAsync();

        var mid2 = await _store.LoadAsync("fixture-review-multi-golden");
        mid2!.State.Should().Be(FlowState.Review);
        mid2.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        // Round 2: submit approve
        var rrs2 = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-multi-golden");
        var open2 = rrs2.First(r => r.Status == ReviewRequestStatus.Open);
        open2.Id.Should().NotBe(open1.Id, "should be a new RR, not the old one");

        // Old RR remains Answered (it was answered before the new one was created)
        var oldRR = rrs2.First(r => r.Id == open1.Id);
        oldRR.Status.Should().Be(ReviewRequestStatus.Answered);

        await runner.SubmitReviewResponseAsync(
            "fixture-review-multi-golden", open2.Id,
            new ReviewResponse
            {
                RespondedBy = "user", RespondedAt = _time.GetUtcNow(),
                Type = ReviewResponseType.ApproveOption,
                SelectedOptionId = "approve", Comment = "Round 2 OK"
            });

        // Run → SpecValidator sees 2 answered → SpecValidationPassed → Active/Done
        for (int i = 0; i < 5; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-review-multi-golden");
        final!.State.Should().Be(FlowState.Active);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Done);
        // Counters reset on SpecValidationPassed, so just verify we reached Active
    }

    [Fact]
    public async Task GoldenScenario_ReviewLoop_Exhausted()
    {
        // Start with UserReviewLoopCount = 3 → next review request triggers Failed
        var spec = new Spec
        {
            Id = "fixture-review-needed-exhaust", ProjectId = "fixture-project",
            Title = "Exhausted Review", Type = SpecType.Task,
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            RetryCounters = new RetryCounters { UserReviewLoopCount = 3 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();

        // SpecValidator on "fixture-review-needed-exhaust" with no answered RRs
        // → requests user review → RuleEvaluator sees count 3+1=4 > max → Failed
        for (int i = 0; i < 5; i++)
            await runner.RunOnceAsync();

        var final = await _store.LoadAsync("fixture-review-needed-exhaust");
        final!.State.Should().Be(FlowState.Failed);
        final.ProcessingStatus.Should().Be(ProcessingStatus.Error);
        final.RetryCounters.UserReviewLoopCount.Should().Be(4);

        // Verify a ReviewRequest was created for failed notification
        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed-exhaust");
        rrs.Should().NotBeEmpty("a notification RR should be created on failure");

        // Verify transition log
        var committedMessages = await GetCommittedTransitionMessages("fixture-review-needed-exhaust");
        committedMessages.Should().Contain(m => m.Contains("SpecValidationUserReviewRequested"));
    }

    /// <summary>Agent that always returns BaseVersion = 999 (wrong version)</summary>
    private sealed class StaleVersionSpecValidator : IAgentAdapter
    {
        public AgentRole Role => AgentRole.SpecValidator;

        public Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
        {
            return Task.FromResult(new AgentOutput
            {
                Result = AgentResult.Success,
                BaseVersion = 999, // stale version
                ProposedEvent = FlowEvent.AcPrecheckPassed,
                Summary = "stale version agent"
            });
        }
    }
}
