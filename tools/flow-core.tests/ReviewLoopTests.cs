using FlowCore.Agents;
using FlowCore.Agents.Dummy;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

public class ReviewLoopTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly RunnerConfig _config;
    private readonly FakeTimeProvider _time;

    public ReviewLoopTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-review-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("test-project", _tempDir);
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

    private async Task<Spec> CreateSpecAtReviewInReview(string id)
    {
        var spec = new Spec
        {
            Id = id, ProjectId = "test-project", Title = id,
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);
        return (await _store.LoadAsync(id))!;
    }

    private ReviewResponse MakeApproveResponse() => new()
    {
        RespondedBy = "user",
        RespondedAt = _time.GetUtcNow(),
        Type = ReviewResponseType.ApproveOption,
        SelectedOptionId = "approve"
    };

    private ReviewResponse MakeRejectResponse() => new()
    {
        RespondedBy = "user",
        RespondedAt = _time.GetUtcNow(),
        Type = ReviewResponseType.RejectWithComment,
        Comment = "needs more work"
    };

    [Fact]
    public async Task Scenario1_HappyPath_SpecValidationPassed()
    {
        await CreateSpecAtReviewInReview("test-review-happy");
        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("test-review-happy");
        spec!.State.Should().Be(FlowState.Active);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public async Task Scenario2_SingleRound_UserReviewApprove()
    {
        await CreateSpecAtReviewInReview("fixture-review-needed");
        var runner = CreateRunner();

        // SpecValidator returns UserReviewRequested
        await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("fixture-review-needed");
        spec!.State.Should().Be(FlowState.Review);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        // Find the open RR
        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed");
        var openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);
        openRR.Summary.Should().NotBeNullOrEmpty();
        openRR.Options.Should().NotBeNull();

        // User approves
        await runner.SubmitReviewResponseAsync(
            "fixture-review-needed", openRR.Id, MakeApproveResponse());

        // Now Review/InReview → SpecValidator sees Answered RR → Passed
        spec = await _store.LoadAsync("fixture-review-needed");
        spec!.State.Should().Be(FlowState.Review);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.InReview);

        await runner.RunOnceAsync();

        spec = await _store.LoadAsync("fixture-review-needed");
        spec!.State.Should().Be(FlowState.Active);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public async Task Scenario3_RejectWithComment_ReworkRequested()
    {
        await CreateSpecAtReviewInReview("fixture-review-reject");
        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("fixture-review-reject");
        // SpecValidator returns ReworkRequested → Implementation/Pending
        spec!.State.Should().Be(FlowState.Implementation);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task Scenario4_MultiRound_Convergence()
    {
        await CreateSpecAtReviewInReview("fixture-review-multi");
        var runner = CreateRunner();

        // Round 1: UserReviewRequested
        await runner.RunOnceAsync();
        var spec = await _store.LoadAsync("fixture-review-multi");
        spec!.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-multi");
        var openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);
        await runner.SubmitReviewResponseAsync(
            "fixture-review-multi", openRR.Id, MakeApproveResponse());

        // Round 2: still needs more review (answeredRRs.Count < 2)
        await runner.RunOnceAsync();
        spec = await _store.LoadAsync("fixture-review-multi");
        spec!.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-multi");
        openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);
        await runner.SubmitReviewResponseAsync(
            "fixture-review-multi", openRR.Id, MakeApproveResponse());

        // Round 3: answeredRRs.Count >= 2 → converge → Passed
        await runner.RunOnceAsync();
        spec = await _store.LoadAsync("fixture-review-multi");
        spec!.State.Should().Be(FlowState.Active);
        spec.ProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public async Task Scenario5_UserReviewLoop_Exhausted()
    {
        var spec = new Spec
        {
            Id = "test-exhausted", ProjectId = "test-project", Title = "Exhausted",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            RetryCounters = new RetryCounters { UserReviewLoopCount = 3 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // DummySpecValidator returns SpecValidationPassed for generic IDs
        // But we need UserReviewRequested to trigger the counter check
        // Use fixture-review-needed to get UserReviewRequested
        spec = new Spec
        {
            Id = "fixture-review-needed-exhaust", ProjectId = "test-project", Title = "Exhaust",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            RetryCounters = new RetryCounters { UserReviewLoopCount = 3 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var result = await _store.LoadAsync("fixture-review-needed-exhaust");
        result!.State.Should().Be(FlowState.Failed);
        result.ProcessingStatus.Should().Be(ProcessingStatus.Error);
        result.RetryCounters.UserReviewLoopCount.Should().Be(4);
    }

    [Fact]
    public async Task Scenario6_ReviewRequestTimeout()
    {
        var spec = new Spec
        {
            Id = "test-rr-timeout", ProjectId = "test-project", Title = "RR Timeout",
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
            DeadlineAt = _time.GetUtcNow().AddHours(-1)
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-expired"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var result = await _store.LoadAsync("test-rr-timeout");
        result!.State.Should().Be(FlowState.Failed);
        result.ProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    [Fact]
    public async Task Scenario7_ReworkLoop_Exhausted()
    {
        var spec = new Spec
        {
            Id = "fixture-review-reject-exhaust", ProjectId = "test-project", Title = "Rework Exhaust",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            RetryCounters = new RetryCounters { ReworkLoopCount = 3 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var result = await _store.LoadAsync("fixture-review-reject-exhaust");
        // ReworkRequested + ReworkLoopCount > 3 → Failed
        result!.State.Should().Be(FlowState.Failed);
        result.ProcessingStatus.Should().Be(ProcessingStatus.Error);
    }

    [Fact]
    public async Task Scenario8_Supersede_OnlyOneOpenRR()
    {
        await CreateSpecAtReviewInReview("fixture-review-needed-sup");
        var runner = CreateRunner();

        // Round 1: creates first RR
        await runner.RunOnceAsync();
        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed-sup");
        rrs.Count(r => r.Status == ReviewRequestStatus.Open).Should().Be(1);
        var firstRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);

        // User responds
        await runner.SubmitReviewResponseAsync(
            "fixture-review-needed-sup", firstRR.Id, MakeApproveResponse());

        // SpecValidator runs again, but still needs review (count 0 answered visible)
        // Actually DummySpecValidator checks answeredRRs — first one is now Answered
        // So with "fixture-review-needed" prefix and answeredRRs.Count == 0 check,
        // it will pass. We need fixture-review-multi for supersede test.

        // Use a different approach: manually set up for supersede test
        var spec = new Spec
        {
            Id = "test-supersede", ProjectId = "test-project", Title = "Supersede Test",
            State = FlowState.Review, ProcessingStatus = ProcessingStatus.InReview,
            RetryCounters = new RetryCounters { UserReviewLoopCount = 1 },
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // Create an existing Open RR
        var existingRR = new ReviewRequest
        {
            Id = "rr-old", SpecId = "test-supersede",
            CreatedBy = "spec-validator",
            CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(existingRR);
        spec.ReviewRequestIds = ["rr-old"];
        await _store.SaveAsync(spec, 1);

        // Now simulate SpecValidationUserReviewRequested via RuleEvaluator
        // The rule should supersede "rr-old" and create a new RR
        var assignments = await ((IAssignmentStore)_store).LoadBySpecAsync("test-supersede");
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync("test-supersede");

        var ruleInput = new RuleInput
        {
            Spec = spec.ToSnapshot(),
            Event = FlowEvent.SpecValidationUserReviewRequested,
            Actor = ActorKind.SpecValidator,
            Assignments = assignments,
            ReviewRequests = reviewRequests,
            BaseVersion = spec.Version
        };

        var ruleOutput = FlowCore.Rules.RuleEvaluator.Evaluate(ruleInput);
        ruleOutput.Accepted.Should().BeTrue();

        // Check side effects include SupersedeReviewRequest for "rr-old"
        ruleOutput.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.SupersedeReviewRequest
            && e.TargetReviewRequestId == "rr-old");
        ruleOutput.SideEffects.Should().Contain(e =>
            e.Kind == SideEffectKind.CreateReviewRequest);
    }

    [Fact]
    public async Task Scenario9_PartialEditApprove_RecordedAndTriggersReeval()
    {
        await CreateSpecAtReviewInReview("fixture-review-needed-partial");
        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("fixture-review-needed-partial");
        spec!.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed-partial");
        var openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);

        // PartialEditApprove response
        var response = new ReviewResponse
        {
            RespondedBy = "user",
            RespondedAt = _time.GetUtcNow(),
            Type = ReviewResponseType.PartialEditApprove,
            Comment = "minor changes applied"
        };
        await runner.SubmitReviewResponseAsync(
            "fixture-review-needed-partial", openRR.Id, response);

        // Should be back to InReview
        spec = await _store.LoadAsync("fixture-review-needed-partial");
        spec!.ProcessingStatus.Should().Be(ProcessingStatus.InReview);

        // Verify response was recorded
        var updatedRR = await ((IReviewRequestStore)_store)
            .LoadAsync("fixture-review-needed-partial", openRR.Id);
        updatedRR!.Status.Should().Be(ReviewRequestStatus.Answered);
        updatedRR.Response!.Type.Should().Be(ReviewResponseType.PartialEditApprove);
    }

    [Fact]
    public async Task Scenario10_FailedSpec_PlannerReregistration_Archive()
    {
        var spec = new Spec
        {
            Id = "test-failed-rereg", ProjectId = "test-project", Title = "Failed Rereg",
            State = FlowState.Failed, ProcessingStatus = ProcessingStatus.Error,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var rr = new ReviewRequest
        {
            Id = "rr-failed", SpecId = "test-failed-rereg",
            CreatedBy = "runner",
            CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-failed"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();

        // User responds with "re-register"
        var response = new ReviewResponse
        {
            RespondedBy = "user",
            RespondedAt = _time.GetUtcNow(),
            Type = ReviewResponseType.ApproveOption,
            SelectedOptionId = "re-register"
        };

        await runner.SubmitReviewResponseAsync("test-failed-rereg", "rr-failed", response);

        // Original spec should be archived
        var original = await _store.LoadAsync("test-failed-rereg");
        original.Should().BeNull(); // moved to archive

        var archived = await _store.LoadArchivedAsync("test-failed-rereg");
        archived.Should().NotBeNull();
        archived!.State.Should().Be(FlowState.Archived);

        // New spec should exist with DerivedFrom
        var allSpecs = await _store.LoadAllAsync();
        var newSpec = allSpecs.FirstOrDefault(s => s.DerivedFrom == "test-failed-rereg");
        newSpec.Should().NotBeNull();
        newSpec!.State.Should().Be(FlowState.Draft);
        newSpec.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task Scenario11_ArchivedSpec_ExcludedFromLoadAll()
    {
        var spec = new Spec
        {
            Id = "test-to-archive", ProjectId = "test-project", Title = "To Archive",
            State = FlowState.Failed, ProcessingStatus = ProcessingStatus.Error,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner();

        // Discard response → archive without re-registration
        var rr = new ReviewRequest
        {
            Id = "rr-discard", SpecId = "test-to-archive",
            CreatedBy = "runner",
            CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-discard"];
        await _store.SaveAsync(spec, 1);

        var response = new ReviewResponse
        {
            RespondedBy = "user",
            RespondedAt = _time.GetUtcNow(),
            Type = ReviewResponseType.ApproveOption,
            SelectedOptionId = "discard"
        };

        await runner.SubmitReviewResponseAsync("test-to-archive", "rr-discard", response);

        var allSpecs = await _store.LoadAllAsync();
        allSpecs.Should().NotContain(s => s.Id == "test-to-archive");
    }

    [Fact]
    public async Task Scenario12_LoadArchivedAsync_RetrievesArchivedSpec()
    {
        var spec = new Spec
        {
            Id = "test-archived-load", ProjectId = "test-project", Title = "Archived Load",
            State = FlowState.Failed, ProcessingStatus = ProcessingStatus.Error,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var rr = new ReviewRequest
        {
            Id = "rr-arch-load", SpecId = "test-archived-load",
            CreatedBy = "runner",
            CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-arch-load"];
        await _store.SaveAsync(spec, 1);

        var runner = CreateRunner();
        var response = new ReviewResponse
        {
            RespondedBy = "user",
            RespondedAt = _time.GetUtcNow(),
            Type = ReviewResponseType.ApproveOption,
            SelectedOptionId = "discard"
        };

        await runner.SubmitReviewResponseAsync("test-archived-load", "rr-arch-load", response);

        // Should not be in active store
        var active = await _store.LoadAsync("test-archived-load");
        active.Should().BeNull();

        // Should be retrievable from archive
        var archived = await _store.LoadArchivedAsync("test-archived-load");
        archived.Should().NotBeNull();
        archived!.Id.Should().Be("test-archived-load");
        archived.State.Should().Be(FlowState.Archived);
    }

    // ── Regression tests for atomicity bugs ──

    [Fact]
    public async Task Regression_SubmitterDoesNotPersistRR_BeforeTransition()
    {
        // Issue 1: ReviewResponseSubmitter must NOT persist RR as Answered.
        // It returns the validated RR in-memory; the caller commits only after
        // the state transition succeeds. This prevents stranding.
        await CreateSpecAtReviewInReview("fixture-review-needed-cas");
        var runner = CreateRunner();
        await runner.RunOnceAsync();

        var spec = await _store.LoadAsync("fixture-review-needed-cas");
        spec!.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        var rrs = await ((IReviewRequestStore)_store).LoadBySpecAsync("fixture-review-needed-cas");
        var openRR = rrs.First(r => r.Status == ReviewRequestStatus.Open);

        // Call submitter directly — should NOT persist the RR change
        var submitter = new ReviewResponseSubmitter(_store);
        var result = await submitter.SubmitResponseAsync(
            "fixture-review-needed-cas", openRR.Id, MakeApproveResponse());

        result.Kind.Should().Be(SubmitResultKind.Success);
        result.ValidatedReviewRequest.Should().NotBeNull();
        result.ValidatedReviewRequest!.Status.Should().Be(ReviewRequestStatus.Answered,
            "in-memory RR should be Answered");

        // But the store should still have Open status
        var rrInStore = await ((IReviewRequestStore)_store).LoadAsync(
            "fixture-review-needed-cas", openRR.Id);
        rrInStore!.Status.Should().Be(ReviewRequestStatus.Open,
            "RR in store must remain Open until caller explicitly commits");

        // After CommitAsync, it becomes Answered
        await submitter.CommitAsync(result.ValidatedReviewRequest);
        var rrAfterCommit = await ((IReviewRequestStore)_store).LoadAsync(
            "fixture-review-needed-cas", openRR.Id);
        rrAfterCommit!.Status.Should().Be(ReviewRequestStatus.Answered);
    }

    [Fact]
    public async Task Regression_FailedReregistration_NoPlannerAgent_ReturnsFalse()
    {
        // Issue 2: If no Planner is registered, HandleFailedSpecReregistration
        // must return false, not silently succeed.
        var spec = new Spec
        {
            Id = "fixture-no-planner", ProjectId = "test-project", Title = "No Planner",
            State = FlowState.Failed, ProcessingStatus = ProcessingStatus.Error,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var rr = new ReviewRequest
        {
            Id = "rr-no-planner", SpecId = "fixture-no-planner",
            CreatedBy = "spec-validator",
            CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Open
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);
        spec.ReviewRequestIds = ["rr-no-planner"];
        await _store.SaveAsync(spec, 1);

        // Runner without Planner agent
        var runner = new FlowRunner(
            _store,
            new IAgentAdapter[]
            {
                new DummySpecValidator(),
                new DummyArchitect(),
                new DummyDeveloper(),
                new DummyTestValidator()
                // NO DummyPlanner
            },
            _config, _time);

        var response = new ReviewResponse
        {
            RespondedBy = "user", RespondedAt = _time.GetUtcNow(),
            Type = ReviewResponseType.ApproveOption,
            SelectedOptionId = "re-register"
        };

        var result = await runner.SubmitReviewResponseAsync(
            "fixture-no-planner", "rr-no-planner", response);
        result.Should().BeFalse("should fail when no Planner is registered");

        // RR should still be Open (not Answered) since operation failed
        var rrAfter = await ((IReviewRequestStore)_store).LoadAsync(
            "fixture-no-planner", "rr-no-planner");
        rrAfter!.Status.Should().Be(ReviewRequestStatus.Open,
            "RR must remain Open when re-registration fails");
    }

    [Fact]
    public async Task Regression_ArchiveAsync_VerifiesAllFiles()
    {
        // Issue 3: Archive must verify ALL files are copied, not just spec.json.
        var spec = new Spec
        {
            Id = "fixture-archive-verify", ProjectId = "test-project", Title = "Verify Test",
            State = FlowState.Archived, ProcessingStatus = ProcessingStatus.Done,
            CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow(), Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // Create assignment and RR files to ensure they're preserved
        var asg = new Assignment
        {
            Id = "asg-verify", SpecId = "fixture-archive-verify",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Completed
        };
        await ((IAssignmentStore)_store).SaveAsync(asg);

        var rr = new ReviewRequest
        {
            Id = "rr-verify", SpecId = "fixture-archive-verify",
            CreatedBy = "test", CreatedAt = _time.GetUtcNow(),
            Status = ReviewRequestStatus.Answered
        };
        await ((IReviewRequestStore)_store).SaveAsync(rr);

        var specDir = Path.Combine(_tempDir, "projects", "test-project", "specs", "fixture-archive-verify");
        var archiveDir = Path.Combine(_tempDir, "projects", "test-project", "specs-archived", "fixture-archive-verify");

        await _store.ArchiveAsync("fixture-archive-verify");

        // Source deleted
        Directory.Exists(specDir).Should().BeFalse("source should be deleted after archive");

        // All files preserved in archive
        File.Exists(Path.Combine(archiveDir, "spec.json")).Should().BeTrue("spec.json must be archived");
        File.Exists(Path.Combine(archiveDir, "assignments", "asg-verify.json"))
            .Should().BeTrue("assignment files must be archived");
        File.Exists(Path.Combine(archiveDir, "review-requests", "rr-verify.json"))
            .Should().BeTrue("review request files must be archived");
    }
}
