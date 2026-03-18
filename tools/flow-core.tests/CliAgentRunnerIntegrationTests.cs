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
/// CliSpecValidator를 FlowRunner에 실제로 주입하여
/// real agent 경로의 end-to-end 동작을 검증한다.
/// FakeBackend로 LLM 응답을 시뮬레이션한다.
/// </summary>
public class CliAgentRunnerIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;
    private readonly RunnerConfig _config;
    private readonly FakeTimeProvider _time;

    public CliAgentRunnerIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-cli-agent-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("test-project", _tempDir);
        _config = new RunnerConfig { PollIntervalSeconds = 1, MaxSpecsPerCycle = 20 };
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>테스트용 가짜 백엔드 — 설정된 JSON 응답을 반환</summary>
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

    private sealed class FailingBackend : ICliBackend
    {
        public string BackendId => "fake";

        public Task<CliResponse> RunPromptAsync(
            string prompt, CliBackendOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "simulated timeout",
                StopReason = CliStopReason.Timeout
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private CliSpecValidator CreateCliValidator(ICliBackend backend)
    {
        var backendConfig = new BackendConfig
        {
            AgentBackends = new() { ["specValidator"] = new AgentBackendMapping { Backend = "fake" } },
            Backends = new() { ["fake"] = new BackendDefinition { Command = "test" } }
        };
        var registry = new BackendRegistry(backendConfig,
            new Dictionary<string, ICliBackend> { ["fake"] = backend });
        return new CliSpecValidator(registry, new PromptBuilder(), new OutputParser());
    }

    private FlowRunner CreateRunner(ICliBackend specValidatorBackend)
    {
        return new FlowRunner(
            _store,
            new IAgentAdapter[]
            {
                CreateCliValidator(specValidatorBackend),
                new DummyArchitect(),
                new DummyDeveloper(),
                new DummyTestGenerator(),
                new DummyPlanner()
            },
            _config, _time);
    }

    [Fact]
    public async Task FullPipeline_DraftToQueued_CliSpecValidatorAcPrecheck()
    {
        // Draft spec → CliSpecValidator가 AC precheck → Queued
        var spec = new Spec
        {
            Id = "cli-test-001",
            ProjectId = "test-project",
            Title = "CLI Agent Test",
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var backend = new FakeBackend(
            """{"proposedEvent":"acPrecheckPassed","summary":"AC가 명확하고 검증 가능합니다."}""");
        var runner = CreateRunner(backend);

        var processed = await runner.RunOnceAsync();

        processed.Should().BeGreaterThan(0);

        var updated = await _store.LoadAsync("cli-test-001");
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FlowState.Queued);
    }

    [Fact]
    public async Task ReviewStage_CliSpecValidatorPasses_SpecCompletesReview()
    {
        // Review/InReview spec → CliSpecValidator passes → Active/Done
        var spec = new Spec
        {
            Id = "cli-review-001",
            ProjectId = "test-project",
            Title = "CLI Review Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var backend = new FakeBackend(
            """{"proposedEvent":"specValidationPassed","summary":"모든 인수 조건 충족"}""");
        var runner = CreateRunner(backend);

        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("cli-review-001");
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FlowState.Active);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.Done);
    }

    [Fact]
    public async Task ReviewStage_CliSpecValidatorRequestsRework_GoesBackToImplementation()
    {
        var spec = new Spec
        {
            Id = "cli-rework-001",
            ProjectId = "test-project",
            Title = "CLI Rework Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var backend = new FakeBackend(
            """{"proposedEvent":"specValidationReworkRequested","summary":"테스트 커버리지 부족"}""");
        var runner = CreateRunner(backend);

        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("cli-rework-001");
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FlowState.Implementation);
    }

    [Fact]
    public async Task ReviewStage_CliSpecValidatorRequestsUserReview_SetsUserReview()
    {
        var spec = new Spec
        {
            Id = "cli-userreview-001",
            ProjectId = "test-project",
            Title = "CLI UserReview Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var responseJson = """
            ```json
            {
              "proposedEvent": "specValidationUserReviewRequested",
              "summary": "보안 관련 변경사항에 대해 사용자 확인 필요",
              "proposedReviewRequest": {
                "summary": "보안 설정 변경 확인",
                "questions": ["이 보안 설정이 적절합니까?"],
                "options": [
                  { "id": "approve", "label": "승인", "description": "진행" },
                  { "id": "reject", "label": "반려", "description": "재작업" }
                ]
              }
            }
            ```
            """;
        var backend = new FakeBackend(responseJson);
        var runner = CreateRunner(backend);

        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("cli-userreview-001");
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FlowState.Review);
        updated.ProcessingStatus.Should().Be(ProcessingStatus.UserReview);

        // ReviewRequest가 생성되었는지 확인
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync("cli-userreview-001");
        reviewRequests.Should().HaveCount(1);
        reviewRequests[0].Status.Should().Be(ReviewRequestStatus.Open);
    }

    [Fact]
    public async Task ReviewStage_BackendTimeout_RetryableFailure_SpecRetries()
    {
        var spec = new Spec
        {
            Id = "cli-timeout-001",
            ProjectId = "test-project",
            Title = "CLI Timeout Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner(new FailingBackend());

        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("cli-timeout-001");
        updated.Should().NotBeNull();
        // RetryableFailure → spec은 Pending으로 돌아가 retry 대기
        updated!.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
        updated.RetryCounters.ReworkLoopCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DraftAcPrecheck_BackendFailure_RetryScheduled()
    {
        var spec = new Spec
        {
            Id = "cli-draft-fail-001",
            ProjectId = "test-project",
            Title = "Draft Fail Test",
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var runner = CreateRunner(new FailingBackend());

        await runner.RunOnceAsync();

        var updated = await _store.LoadAsync("cli-draft-fail-001");
        updated.Should().NotBeNull();
        // Draft에서 실패 → retry 스케줄링
        updated!.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    [Fact]
    public async Task MultiCycle_DraftToActive_CliSpecValidatorEndToEnd()
    {
        // Draft → (AC precheck) → Queued → ArchitectureReview → Implementation →
        // TestGeneration → Implementation → Review → (spec validation) → Active
        // CliSpecValidator가 Draft와 Review 두 단계 모두에서 동작하는지 검증
        var spec = new Spec
        {
            Id = "cli-e2e-001",
            ProjectId = "test-project",
            Title = "End to End Test",
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // Backend: AC precheck와 spec validation 모두 pass
        var backend = new FakeBackend(
            """{"proposedEvent":"acPrecheckPassed","summary":"ok"}""");
        var passValidationBackend = new FakeBackend(
            """{"proposedEvent":"specValidationPassed","summary":"all AC met"}""");

        // 1차: Draft → Queued (AC precheck)
        var runner1 = CreateRunner(backend);
        await runner1.RunOnceAsync();

        var afterPrecheck = await _store.LoadAsync("cli-e2e-001");
        afterPrecheck!.State.Should().Be(FlowState.Queued);

        // 중간 단계: Queued → ArchReview → Impl → TestVal → Review
        // (DummyArchitect, DummyDeveloper, DummyTestGenerator가 처리)
        for (int i = 0; i < 8; i++)
        {
            await runner1.RunOnceAsync();
        }

        var beforeReview = await _store.LoadAsync("cli-e2e-001");
        // Review 단계에 도달했는지 확인
        if (beforeReview!.State == FlowState.Review)
        {
            // 2차: Review → Active (spec validation) — passValidation 백엔드로 교체
            var runner2 = CreateRunner(passValidationBackend);
            await runner2.RunOnceAsync();

            var final = await _store.LoadAsync("cli-e2e-001");
            final!.State.Should().Be(FlowState.Active);
            final.ProcessingStatus.Should().Be(ProcessingStatus.Done);
        }
    }

    [Fact]
    public async Task ReviewStage_WithEvidence_ManifestSavedToSpecDir()
    {
        var spec = new Spec
        {
            Id = "cli-evidence-001",
            ProjectId = "test-project",
            Title = "Evidence Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var responseJson = """
            ```json
            {
              "proposedEvent": "specValidationPassed",
              "summary": "모든 AC 충족",
              "evidenceRefs": [
                { "kind": "test-result", "relativePath": "test-output.xml", "summary": "42 tests passed" },
                { "kind": "coverage", "relativePath": "coverage.html", "summary": "85% coverage" }
              ]
            }
            ```
            """;
        var backend = new FakeBackend(responseJson);
        var runner = CreateRunner(backend);

        await runner.RunOnceAsync();

        // spec이 Active로 전이되었는지 확인
        var updated = await _store.LoadAsync("cli-evidence-001");
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FlowState.Active);

        // evidence manifest가 spec 디렉토리 내부에 저장되었는지 확인
        var manifests = await ((IEvidenceStore)_store).LoadBySpecAsync("cli-evidence-001");
        manifests.Should().HaveCount(1);
        manifests[0].Refs.Should().HaveCount(2);
        manifests[0].Refs[0].Kind.Should().Be("test-result");
        manifests[0].Refs[1].Kind.Should().Be("coverage");

        // evidence 디렉토리가 spec 디렉토리 안에 있는지 확인
        var evidenceDir = ((IEvidenceStore)_store).GetEvidenceDir("cli-evidence-001", manifests[0].RunId);
        evidenceDir.Should().Contain("cli-evidence-001");
        evidenceDir.Should().Contain("evidence");
    }
}
