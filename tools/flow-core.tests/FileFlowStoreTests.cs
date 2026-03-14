using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace FlowCore.Tests;

public class FileFlowStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;

    public FileFlowStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("test-project", _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static Spec MakeSpec(string id = "spec-001", int version = 1) => new()
    {
        Id = id, ProjectId = "test-project", Title = "테스트 스펙",
        State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        Version = version
    };

    // ── Spec CRUD ──

    [Fact]
    public async Task Spec_SaveAndLoad_RoundTrip()
    {
        var spec = MakeSpec();
        var result = await _store.SaveAsync(spec, 0);
        result.IsSuccess.Should().BeTrue();

        var loaded = await _store.LoadAsync("spec-001");
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("spec-001");
        loaded.Title.Should().Be("테스트 스펙");
        loaded.State.Should().Be(FlowState.Draft);
    }

    [Fact]
    public async Task Spec_LoadNonExistent_ReturnsNull()
    {
        var loaded = await _store.LoadAsync("no-such-spec");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Spec_CAS_ConflictDetected()
    {
        var spec = MakeSpec(version: 1);
        await _store.SaveAsync(spec, 0);

        var result = await _store.SaveAsync(spec, 999);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(SaveStatus.Conflict);
        result.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task Spec_CAS_CorrectVersion_Succeeds()
    {
        var spec = MakeSpec(version: 1);
        await _store.SaveAsync(spec, 0);

        var updated = MakeSpec(version: 2);
        var result = await _store.SaveAsync(updated, 1);
        result.IsSuccess.Should().BeTrue();

        var loaded = await _store.LoadAsync("spec-001");
        loaded!.Version.Should().Be(2);
    }

    [Fact]
    public async Task Spec_LoadAll()
    {
        await _store.SaveAsync(MakeSpec("spec-a"), 0);
        await _store.SaveAsync(MakeSpec("spec-b"), 0);
        await _store.SaveAsync(MakeSpec("spec-c"), 0);

        var all = await _store.LoadAllAsync();
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task Spec_Pruning_EmptyArraysRemoved()
    {
        var spec = MakeSpec();
        await _store.SaveAsync(spec, 0);

        var filePath = Path.Combine(_tempDir, "projects", "test-project", "specs", "spec-001", "spec.json");
        var json = await File.ReadAllTextAsync(filePath);
        json.Should().NotContain("\"assignments\"");
        json.Should().NotContain("\"reviewRequestIds\"");
        json.Should().NotContain("\"retryCounters\"");
    }

    [Fact]
    public async Task Spec_Pruning_NonEmptyFieldsKept()
    {
        var spec = MakeSpec();
        spec.Assignments = ["asg-001"];
        spec.RetryCounters = new RetryCounters { ReworkLoopCount = 2 };
        await _store.SaveAsync(spec, 0);

        var filePath = Path.Combine(_tempDir, "projects", "test-project", "specs", "spec-001", "spec.json");
        var json = await File.ReadAllTextAsync(filePath);
        json.Should().Contain("\"assignments\"");
        json.Should().Contain("\"retryCounters\"");
    }

    [Fact]
    public async Task Spec_DefaultsFilled_OnLoad()
    {
        var spec = MakeSpec();
        await _store.SaveAsync(spec, 0);

        var loaded = await _store.LoadAsync("spec-001");
        loaded!.Assignments.Should().BeEmpty();
        loaded.ReviewRequestIds.Should().BeEmpty();
        loaded.RetryCounters.Should().NotBeNull();
        loaded.RetryCounters.ReworkLoopCount.Should().Be(0);
    }

    // ── Assignment ──

    [Fact]
    public async Task Assignment_SaveAndLoad()
    {
        var asg = new Assignment
        {
            Id = "asg-001", SpecId = "spec-001",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation,
            Status = AssignmentStatus.Running,
            StartedAt = DateTimeOffset.UtcNow, TimeoutSeconds = 3600
        };

        IAssignmentStore store = _store;
        var result = await store.SaveAsync(asg);
        result.IsSuccess.Should().BeTrue();

        var loaded = await store.LoadAsync("spec-001", "asg-001");
        loaded.Should().NotBeNull();
        loaded!.AgentRole.Should().Be(AgentRole.Developer);
        loaded.Status.Should().Be(AssignmentStatus.Running);
        loaded.TimeoutSeconds.Should().Be(3600);
    }

    [Fact]
    public async Task Assignment_LoadBySpec()
    {
        IAssignmentStore store = _store;

        var asg1 = new Assignment
        {
            Id = "asg-001", SpecId = "spec-001",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation
        };
        var asg2 = new Assignment
        {
            Id = "asg-002", SpecId = "spec-001",
            AgentRole = AgentRole.TestValidator, Type = AssignmentType.TestValidation
        };
        await store.SaveAsync(asg1);
        await store.SaveAsync(asg2);

        var all = await store.LoadBySpecAsync("spec-001");
        all.Should().HaveCount(2);
    }

    // ── ReviewRequest ──

    [Fact]
    public async Task ReviewRequest_SaveAndLoad()
    {
        var rr = new ReviewRequest
        {
            Id = "rr-001", SpecId = "spec-001",
            CreatedBy = "validator", Status = ReviewRequestStatus.Open,
            Reason = "구현 방향 확인",
            Options =
            [
                new() { Id = "opt-a", Label = "안 A" },
                new() { Id = "opt-b", Label = "안 B", Description = "설명" }
            ]
        };

        IReviewRequestStore store = _store;
        var result = await store.SaveAsync(rr);
        result.IsSuccess.Should().BeTrue();

        var loaded = await store.LoadAsync("spec-001", "rr-001");
        loaded.Should().NotBeNull();
        loaded!.Options.Should().HaveCount(2);
        loaded.Options![0].Label.Should().Be("안 A");
    }

    // ── Activity ──

    [Fact]
    public async Task Activity_AppendAndLoadRecent()
    {
        IActivityStore store = _store;

        for (int i = 1; i <= 5; i++)
        {
            var evt = new ActivityEvent
            {
                EventId = $"evt-{i:D3}", SpecId = "spec-001",
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(i),
                Actor = "runner", Action = ActivityAction.DraftCreated,
                SourceType = "runner", BaseVersion = i,
                State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
                Message = $"Event {i}"
            };
            await store.AppendAsync(evt);
        }

        var recent = await store.LoadRecentAsync("spec-001", 3);
        recent.Should().HaveCount(3);
        // 최신순 (역순)
        recent[0].Message.Should().Be("Event 5");
        recent[1].Message.Should().Be("Event 4");
        recent[2].Message.Should().Be("Event 3");
    }

    [Fact]
    public async Task Activity_EmptySpec_ReturnsEmpty()
    {
        IActivityStore store = _store;
        var recent = await store.LoadRecentAsync("no-spec", 10);
        recent.Should().BeEmpty();
    }

    // ── DeleteSpec ──

    [Fact]
    public async Task DeleteSpec_RemovesSpecAndSideEffectFiles()
    {
        var spec = MakeSpec("spec-del");
        spec.Assignments = ["asg-del"];
        await _store.SaveAsync(spec, 0);

        var asg = new Assignment
        {
            Id = "asg-del", SpecId = "spec-del",
            AgentRole = AgentRole.Developer, Type = AssignmentType.Implementation
        };
        IAssignmentStore asgStore = _store;
        await asgStore.SaveAsync(asg);

        // 삭제
        await _store.DeleteSpecAsync("spec-del");

        var loaded = await _store.LoadAsync("spec-del");
        loaded.Should().BeNull();

        var asgs = await asgStore.LoadBySpecAsync("spec-del");
        asgs.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSpec_NonExistent_DoesNotThrow()
    {
        var act = () => _store.DeleteSpecAsync("no-such-spec");
        await act.Should().NotThrowAsync();
    }
}
