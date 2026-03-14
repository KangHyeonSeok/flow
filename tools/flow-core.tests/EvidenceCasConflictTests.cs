using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Agents.Dummy;
using FlowCore.Backend;
using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FlowCore.Tests;

/// <summary>
/// CAS 충돌 시 evidence manifest가 저장되지 않음을 검증한다.
/// CasConflictStore로 spec 저장만 실패시키고, evidence 부재를 확인한다.
/// </summary>
public class EvidenceCasConflictTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _realStore;
    private readonly CasConflictStore _conflictStore;
    private readonly RunnerConfig _config;
    private readonly FakeTimeProvider _time;

    public EvidenceCasConflictTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-cas-conflict-test-{Guid.NewGuid():N}");
        _realStore = new FileFlowStore("test-project", _tempDir);
        _conflictStore = new CasConflictStore(_realStore);
        _config = new RunnerConfig { PollIntervalSeconds = 1, MaxSpecsPerCycle = 20 };
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CasConflict_EvidenceManifestNotSaved()
    {
        // Review/InReview spec → agent가 evidence 포함 응답 반환 → CAS 충돌 → evidence 미저장
        var spec = new Spec
        {
            Id = "cas-ev-001",
            ProjectId = "test-project",
            Title = "CAS Conflict Evidence Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _realStore.SaveAsync(spec, 0);

        var responseJson = """
            {
              "proposedEvent": "specValidationPassed",
              "summary": "모든 AC 충족",
              "evidenceRefs": [
                { "kind": "test-result", "relativePath": "test-output.xml", "summary": "42 tests passed" }
              ]
            }
            """;

        // CAS 충돌을 유발: spec SaveAsync가 Conflict를 반환하도록 설정
        _conflictStore.FailNextSpecSave = true;

        var runner = CreateRunner(new FakeBackend(responseJson));
        await runner.RunOnceAsync();

        // spec 상태는 변경되지 않아야 함 (CAS 실패이므로 디스크의 version 1 그대로)
        var updated = await _realStore.LoadAsync("cas-ev-001");
        updated.Should().NotBeNull();
        updated!.Version.Should().Be(1, "CAS conflict이면 spec이 저장되지 않음");

        // evidence manifest가 저장되지 않아야 함
        var manifests = await ((IEvidenceStore)_realStore).LoadBySpecAsync("cas-ev-001");
        manifests.Should().BeEmpty("CAS conflict 시 evidence는 저장되지 않아야 함");
    }

    [Fact]
    public async Task NoCasConflict_EvidenceManifestSaved()
    {
        // 정상 케이스: CAS 성공 → evidence 저장됨
        var spec = new Spec
        {
            Id = "cas-ev-002",
            ProjectId = "test-project",
            Title = "Normal Evidence Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _realStore.SaveAsync(spec, 0);

        var responseJson = """
            {
              "proposedEvent": "specValidationPassed",
              "summary": "모든 AC 충족",
              "evidenceRefs": [
                { "kind": "test-result", "relativePath": "test-output.xml", "summary": "42 tests passed" }
              ]
            }
            """;

        // CAS 충돌 없음
        _conflictStore.FailNextSpecSave = false;

        var runner = CreateRunner(new FakeBackend(responseJson));
        await runner.RunOnceAsync();

        // evidence manifest가 저장되어야 함
        var manifests = await ((IEvidenceStore)_realStore).LoadBySpecAsync("cas-ev-002");
        manifests.Should().HaveCount(1, "CAS 성공 시 evidence가 저장되어야 함");
        manifests[0].Refs.Should().HaveCount(1);
        manifests[0].Refs[0].Kind.Should().Be("test-result");
    }

    // ── Helpers ──

    private sealed class FakeBackend : ICliBackend
    {
        public string BackendId => "fake";
        private readonly string _responseJson;

        public FakeBackend(string responseJson) => _responseJson = responseJson;

        public Task<CliResponse> RunPromptAsync(
            string prompt, CliBackendOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(new CliResponse
            {
                ResponseText = _responseJson,
                Success = true,
                StopReason = CliStopReason.Completed
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private FlowRunner CreateRunner(ICliBackend specValidatorBackend)
    {
        var backendConfig = new BackendConfig
        {
            AgentBackends = new() { ["specValidator"] = new AgentBackendMapping { Backend = "fake" } },
            Backends = new() { ["fake"] = new BackendDefinition { Command = "test" } }
        };
        var registry = new BackendRegistry(backendConfig,
            new Dictionary<string, ICliBackend> { ["fake"] = specValidatorBackend });
        var cliValidator = new CliSpecValidator(registry, new PromptBuilder(), new OutputParser());

        return new FlowRunner(
            _conflictStore,
            new IAgentAdapter[]
            {
                cliValidator,
                new DummyArchitect(),
                new DummyDeveloper(),
                new DummyTestValidator(),
                new DummyPlanner()
            },
            _config, _time);
    }

    /// <summary>
    /// FileFlowStore를 래핑하여 spec SaveAsync에서 CAS 충돌을 유발할 수 있는 테스트 스토어.
    /// 다른 모든 메서드는 그대로 위임한다.
    /// </summary>
    private sealed class CasConflictStore : IFlowStore
    {
        private readonly FileFlowStore _inner;

        /// <summary>true이면 다음 spec SaveAsync에서 CAS 충돌 반환 (1회성)</summary>
        public bool FailNextSpecSave { get; set; }

        public CasConflictStore(FileFlowStore inner) => _inner = inner;

        // ── ISpecStore (SaveAsync만 가로챔) ──

        public Task<Spec?> LoadAsync(string specId, CancellationToken ct = default)
            => _inner.LoadAsync(specId, ct);

        public Task<IReadOnlyList<Spec>> LoadAllAsync(CancellationToken ct = default)
            => _inner.LoadAllAsync(ct);

        public Task<SaveResult> SaveAsync(Spec spec, int expectedVersion, CancellationToken ct = default)
        {
            if (FailNextSpecSave)
            {
                FailNextSpecSave = false;
                return Task.FromResult(SaveResult.ConflictAt(expectedVersion + 1));
            }
            return _inner.SaveAsync(spec, expectedVersion, ct);
        }

        public Task DeleteSpecAsync(string specId, CancellationToken ct = default)
            => _inner.DeleteSpecAsync(specId, ct);

        public Task ArchiveAsync(string specId, CancellationToken ct = default)
            => _inner.ArchiveAsync(specId, ct);

        public Task<Spec?> LoadArchivedAsync(string specId, CancellationToken ct = default)
            => _inner.LoadArchivedAsync(specId, ct);

        // ── IAssignmentStore ──

        public Task<Assignment?> LoadAsync(string specId, string assignmentId, CancellationToken ct = default)
            => ((IAssignmentStore)_inner).LoadAsync(specId, assignmentId, ct);

        public Task<IReadOnlyList<Assignment>> LoadBySpecAsync(string specId, CancellationToken ct = default)
            => ((IAssignmentStore)_inner).LoadBySpecAsync(specId, ct);

        public Task<SaveResult> SaveAsync(Assignment assignment, CancellationToken ct = default)
            => ((IAssignmentStore)_inner).SaveAsync(assignment, ct);

        // ── IReviewRequestStore ──

        Task<ReviewRequest?> IReviewRequestStore.LoadAsync(string specId, string reviewRequestId, CancellationToken ct)
            => ((IReviewRequestStore)_inner).LoadAsync(specId, reviewRequestId, ct);

        Task<IReadOnlyList<ReviewRequest>> IReviewRequestStore.LoadBySpecAsync(string specId, CancellationToken ct)
            => ((IReviewRequestStore)_inner).LoadBySpecAsync(specId, ct);

        Task<SaveResult> IReviewRequestStore.SaveAsync(ReviewRequest reviewRequest, CancellationToken ct)
            => ((IReviewRequestStore)_inner).SaveAsync(reviewRequest, ct);

        // ── IActivityStore ──

        public Task AppendAsync(ActivityEvent activityEvent, CancellationToken ct = default)
            => ((IActivityStore)_inner).AppendAsync(activityEvent, ct);

        public Task<IReadOnlyList<ActivityEvent>> LoadRecentAsync(string specId, int maxCount, CancellationToken ct = default)
            => ((IActivityStore)_inner).LoadRecentAsync(specId, maxCount, ct);

        // ── IEvidenceStore ──

        public Task SaveManifestAsync(EvidenceManifest manifest, CancellationToken ct = default)
            => ((IEvidenceStore)_inner).SaveManifestAsync(manifest, ct);

        public Task<EvidenceManifest?> LoadManifestAsync(string specId, string runId, CancellationToken ct = default)
            => ((IEvidenceStore)_inner).LoadManifestAsync(specId, runId, ct);

        Task<IReadOnlyList<EvidenceManifest>> IEvidenceStore.LoadBySpecAsync(string specId, CancellationToken ct)
            => ((IEvidenceStore)_inner).LoadBySpecAsync(specId, ct);

        public string GetEvidenceDir(string specId, string runId)
            => ((IEvidenceStore)_inner).GetEvidenceDir(specId, runId);
    }
}
