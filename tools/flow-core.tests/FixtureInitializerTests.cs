using FlowCore.Fixtures;
using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace FlowCore.Tests;

public class FixtureInitializerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly FixtureInitializer _initializer;

    public FixtureInitializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-fixture-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("fixture-project", _tempDir);
        _initializer = new FixtureInitializer(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Initialize_Creates7Fixtures()
    {
        await _initializer.InitializeAsync();

        var specs = await _store.LoadAllAsync();
        specs.Should().HaveCount(7);
    }

    [Fact]
    public async Task Initialize_HappyPath_IsDraftLow()
    {
        await _initializer.InitializeAsync();

        var spec = await _store.LoadAsync("fixture-happy-path");
        spec.Should().NotBeNull();
        spec!.State.Should().Be(FlowState.Draft);
        spec.RiskLevel.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public async Task Initialize_ArchitectReview_IsMediumRisk()
    {
        await _initializer.InitializeAsync();

        var spec = await _store.LoadAsync("fixture-architect-review");
        spec.Should().NotBeNull();
        spec!.RiskLevel.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public async Task Initialize_ReviewNeeded_HasOpenReviewRequest()
    {
        await _initializer.InitializeAsync();

        var spec = await _store.LoadAsync("fixture-review-needed");
        spec.Should().NotBeNull();
        spec!.ReviewRequestIds.Should().HaveCount(1);

        IReviewRequestStore rrStore = _store;
        var rrs = await rrStore.LoadBySpecAsync("fixture-review-needed");
        rrs.Should().HaveCount(1);
        rrs[0].Status.Should().Be(ReviewRequestStatus.Open);
        rrs[0].Options.Should().HaveCount(2);
    }

    [Fact]
    public async Task Initialize_DependencyPair_HasDependency()
    {
        await _initializer.InitializeAsync();

        var downstream = await _store.LoadAsync("fixture-dep-downstream");
        downstream.Should().NotBeNull();
        downstream!.Dependencies.DependsOn.Should().Contain("fixture-dep-upstream");
    }

    [Fact]
    public async Task Initialize_StaleAssignment_HasRunningAssignment()
    {
        await _initializer.InitializeAsync();

        var spec = await _store.LoadAsync("fixture-stale-assignment");
        spec.Should().NotBeNull();
        spec!.Assignments.Should().HaveCount(1);

        IAssignmentStore asgStore = _store;
        var asgs = await asgStore.LoadBySpecAsync("fixture-stale-assignment");
        asgs.Should().HaveCount(1);
        asgs[0].Status.Should().Be(AssignmentStatus.Running);
    }

    [Fact]
    public async Task Initialize_RetryExceeded_Has3ArchitectLoops()
    {
        await _initializer.InitializeAsync();

        var spec = await _store.LoadAsync("fixture-retry-exceeded");
        spec.Should().NotBeNull();
        spec!.RetryCounters.ArchitectReviewLoopCount.Should().Be(3);
    }

    [Fact]
    public async Task Reset_RecreatesFixtures()
    {
        await _initializer.InitializeAsync();
        await _initializer.ResetAsync();

        var specs = await _store.LoadAllAsync();
        specs.Should().HaveCount(7);
    }

    [Fact]
    public async Task Initialize_Twice_ThrowsOnConflict()
    {
        await _initializer.InitializeAsync();

        // 두 번째 호출은 expectedVersion=0 이지만 이미 version=1이 있으므로 실패
        var act = () => _initializer.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Conflict*");
    }

    [Fact]
    public async Task Reset_ThenInitialize_NoOrphanFiles()
    {
        await _initializer.InitializeAsync();

        // reset → 삭제 후 재생성
        await _initializer.ResetAsync();

        // stale assignment fixture: assignment 파일이 정확히 1개
        IAssignmentStore asgStore = _store;
        var asgs = await asgStore.LoadBySpecAsync("fixture-stale-assignment");
        asgs.Should().HaveCount(1, "reset 후에는 이전 assignment 파일이 남으면 안 된다");

        // review request fixture: rr 파일이 정확히 1개
        IReviewRequestStore rrStore = _store;
        var rrs = await rrStore.LoadBySpecAsync("fixture-review-needed");
        rrs.Should().HaveCount(1, "reset 후에는 이전 review request 파일이 남으면 안 된다");
    }
}
