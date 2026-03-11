using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

/// <summary>
/// Flow Runner 핵심 서비스. 주기적으로 스펙 그래프를 폴링하고
/// 미구현/오류 스펙을 Copilot CLI로 자동 구현한다.
/// </summary>
public class RunnerService
{
    private const string ImplementationStage = "implementation";
    private const string TestValidationStage = "test-validation";
    private const string ReviewStage = "review";

    private readonly string _projectRoot;
    private readonly string _flowRoot;
    private readonly RunnerConfig _config;
    private readonly SpecStore _specStore;
    private readonly GitWorktreeService _git;
    private readonly CopilotService _copilot;
    private readonly RunnerLogService _log;
    private readonly BrokenSpecDiagService _diagService;
    private readonly string _instanceId;
    private readonly string _pidFilePath;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions SpecJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// Runner 내부용 메타데이터 키 (Copilot에게 불필요)
    private static readonly HashSet<string> PromptMetaExcludeKeys =
    [
        "runnerInstanceId", "runnerStartedAt", "implementationPlan",
        "runnerProcessId", "retryNotBefore", "lastErrorType",
        "selectionReason", "lastCompletedAt", "lastCompletedBy",
        "lastReviewAt", "lastReviewBy", "worktreePath", "worktreeBranch",
        "runnerStage",
        "review", "reviewDisposition", "plannerState", "questionStatus",
        "lastError", "lastErrorAt", "lastVerifiedAt", "lastVerifiedBy",
        "verificationSource", "lastAnsweredAt",
    ];

    /// <summary>
    /// Copilot 프롬프트용 스펙 JSON 생성.
    /// 구현에 불필요한 runner 내부 메타데이터, 빈 배열, 타임스탬프를 제거하여 토큰을 절약한다.
    /// </summary>
    private static string BuildSpecPromptJson(SpecNode spec)
    {

        var node = JsonSerializer.SerializeToNode(spec, SpecJsonOpts)!.AsObject();

        // 구현에 불필요한 최상위 필드 제거
        node.Remove("schemaVersion");
        node.Remove("createdAt");
        node.Remove("updatedAt");

        // 항상 비어있거나 불필요한 관계 필드 제거 (빈 배열만)
        RemoveIfEmptyArray(node, "supersedes");
        RemoveIfEmptyArray(node, "supersededBy");
        RemoveIfEmptyArray(node, "mutates");
        RemoveIfEmptyArray(node, "mutatedBy");
        RemoveIfEmptyArray(node, "activity");
        RemoveIfEmptyArray(node, "githubRefs");
        RemoveIfEmptyArray(node, "docLinks");
        RemoveIfEmptyArray(node, "evidence");
        RemoveIfEmptyArray(node, "codeRefs");
        RemoveIfEmptyArray(node, "tags");

        // 조건 내 불필요한 필드 제거
        if (node["conditions"] is JsonArray conditions)
        {
            foreach (var cond in conditions)
            {
                if (cond is not JsonObject condObj) { continue; }
                condObj.Remove("nodeType"); // 항상 "condition"으로 자명함
                RemoveIfEmptyArray(condObj, "evidence");
                RemoveIfEmptyArray(condObj, "tests");
                RemoveIfEmptyArray(condObj, "codeRefs");
                RemoveIfEmptyArray(condObj, "githubRefs");
                RemoveIfEmptyArray(condObj, "docLinks");
            }
        }

        // metadata: runner 내부 키 제거, 비어있으면 통째로 제거
        if (node["metadata"] is JsonObject metadata)
        {
            foreach (var key in PromptMetaExcludeKeys)
            {
                metadata.Remove(key);
            }
            if (metadata.Count == 0)
            {
                node.Remove("metadata");
            }
        }

        return node.ToJsonString(SpecJsonOpts);

        static void RemoveIfEmptyArray(JsonObject obj, string key)
        {
            if (obj[key] is JsonArray arr && arr.Count == 0) { obj.Remove(key); }
        }
    }

    public RunnerService(string projectRoot, RunnerConfig config, bool echoLogsToConsole = true)
    {
        _projectRoot = projectRoot;
        _flowRoot = Path.Combine(projectRoot, ".flow");
        _config = config;

        _instanceId = $"runner-{Environment.ProcessId}-{DateTime.UtcNow:HHmmss}";
        _pidFilePath = Path.Combine(_flowRoot, _config.PidFile);

        _log = new RunnerLogService(_flowRoot, _config.LogDir, _instanceId, echoLogsToConsole);

        // 사용자 홈의 프로젝트별 스펙 디렉터리를 직접 사용
        _specStore = new SpecStore(projectRoot);
        _log.Info("init", $"로컬 스펙 모드: {_specStore.SpecsDir}");

        // F-025: 손상 스펙 진단 서비스 초기화
        _diagService = new BrokenSpecDiagService(_flowRoot, _log);
        _specStore.SetDiagService(_diagService);

        _git = new GitWorktreeService(projectRoot, _flowRoot, _config.WorktreeDir, _config.MainBranch, _log);
        _copilot = new CopilotService(_config, _log);

    }

    public string InstanceId => _instanceId;
    public RunnerLogService Log => _log;

    public RunnerQueuePlan GetQueuePlan()
    {
        var selection = EvaluateTargetSpecs(updateSelectionMetadata: false, logSelection: false);
        var stagedSpecs = FindSequentialStageSpecs()
            .Select(spec => new RunnerStageSpec
            {
                SpecId = spec.Id,
                Title = spec.Title,
                Status = spec.Status,
                Stage = NormalizeRunnerStage(GetMetadataString(spec.Metadata, "runnerStage")),
                LastCompletedAt = GetMetadataString(spec.Metadata, "lastCompletedAt"),
                WorktreePath = GetMetadataString(spec.Metadata, "worktreePath")
            })
            .ToList();

        return new RunnerQueuePlan
        {
            TargetStatuses = _config.TargetStatuses,
            TotalCandidates = selection.ReadySpecs.Count + selection.BlockedSpecs.Count,
            ReadyCount = selection.ReadySpecs.Count,
            BlockedCount = selection.BlockedSpecs.Count,
            StagedCount = stagedSpecs.Count,
            ReviewReadyCount = stagedSpecs.Count,
            NextSpecId = selection.ReadySpecs.FirstOrDefault()?.SpecId,
            ReadySpecs = selection.ReadySpecs,
            BlockedSpecs = selection.BlockedSpecs,
            StagedSpecs = stagedSpecs,
            ReviewReadySpecs = stagedSpecs
        };
    }

    /// <summary>
    /// Runner를 단일 사이클로 실행한다 (한 번만 스캔하고 처리).
    /// </summary>
    public async Task<List<SpecWorkResult>> RunOnceAsync()
    {
        var results = new List<SpecWorkResult>();

        _log.Info("cycle", "=== Runner 사이클 시작 ===");

        // 1. 이전 크래시 복구
        RecoverFromCrash();

        // 1.5. F-025-C3: 손상 스펙 fresh scan → 우선 복구 시도
        var brokenRepaired = await RepairBrokenSpecsAsync();
        results.AddRange(brokenRepaired);

        // 1.6. needs-review 상태의 자동 확정 후보를 먼저 처리한다.
        var autoVerified = await AutoVerifyReviewedSpecsAsync();
        results.AddRange(autoVerified);

        // 1.7. 이전 사이클에서 구현 완료된 staged 작업은 구현 배치와 분리된 stage로 처리한다.
        // 현재 사이클에서 막 구현된 스펙은 다음 poll/reschedule에서 후속 검증으로 넘겨 개발 단계 throughput을 우선한다.
        var stagedSpecs = FindSequentialStageSpecs()
            .Take(_config.MaxConcurrentSpecs)
            .ToList();

        // 2. 구현 대상 스펙 탐색
        var targets = FindTargetSpecs();
        if (targets.Count == 0 && stagedSpecs.Count == 0)
        {
            _log.Info("scan", "구현 대상 스펙 없음");
            return results;
        }

        if (targets.Count > 0)
        {
            _log.Info("scan", $"구현 대상 스펙 {targets.Count}개 발견: {string.Join(", ", targets.Select(s => s.Id))}");
        }

        if (stagedSpecs.Count > 0)
        {
            var stagedLabels = stagedSpecs
                .Select(s => $"{s.Id}({NormalizeRunnerStage(GetMetadataString(s.Metadata, "runnerStage"))})");
            _log.Info("review-stage", $"후속 검증/리뷰 스펙 {stagedSpecs.Count}개 발견: {string.Join(", ", stagedLabels)}");
        }

        // 3. 각 스펙 처리 (maxConcurrent 만큼)
        var batch = targets.Take(_config.MaxConcurrentSpecs).ToList();
        foreach (var spec in batch)
        {
            var result = await ProcessSpecAsync(spec);
            results.Add(result);
        }

        // 4. test-validation/review stage 처리
        foreach (var spec in stagedSpecs)
        {
            var result = await ProcessSequentialStageSpecAsync(spec);
            results.Add(result);
        }

        _log.Info("cycle", $"=== Runner 사이클 완료: {results.Count(r => r.Success)} 성공 / {results.Count(r => !r.Success)} 실패 ===");

        return results;
    }

    /// <summary>
    /// Runner를 데몬 모드로 실행한다 (주기적 폴링).
    /// 처리할 스펙이 없으면 30초마다 재확인하고, 스펙이 처리된 경우 설정된 주기(PollIntervalMinutes)만큼 대기한다.
    /// F-031: 상태 전환 직후 기본 poll 대기 없이 즉시 다음 후보를 재평가한다.
    /// </summary>
    public async Task RunDaemonAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        WritePidFile();
        _log.Info("daemon", $"데몬 시작 (PID: {Environment.ProcessId}, 구현 주기: {_config.PollIntervalMinutes}분, 유휴 재확인: 30초, 최대 즉시 재스케줄: {_config.MaxReschedulesPerPoll}회)");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var allResults = new List<SpecWorkResult>();

                // 메인 전체 사이클 실행 (sync + auto-verify + repair + github + 후보 탐색 + 처리)
                List<SpecWorkResult> latestBatch = [];
                try
                {
                    latestBatch = await RunOnceAsync();
                    allResults.AddRange(latestBatch);
                }
                catch (Exception ex)
                {
                    _log.Error("daemon", $"사이클 실행 중 오류: {ex.Message}");
                }

                // F-031-C1/C5: 상태 전환이 발생한 경우 즉시 재스케줄 루프
                int rescheduleCount = 0;
                while (!_cts.Token.IsCancellationRequested &&
                       latestBatch.Any(r => r.TriggeredReschedule))
                {
                    // C5: cycle 상한 도달 시 busy-wait 방지를 위해 idle 대기로 전환
                    if (rescheduleCount >= _config.MaxReschedulesPerPoll)
                    {
                        _log.Warn("reschedule",
                            $"즉시 재스케줄 사이클 상한 도달 ({_config.MaxReschedulesPerPoll}회) — idle 대기 정책 전환 (F-031-C5)");
                        break;
                    }

                    rescheduleCount++;
                    _log.Info("reschedule",
                        $"상태 전환 감지 → 즉시 재스케줄 시작 (사이클 {rescheduleCount}/{_config.MaxReschedulesPerPoll}, F-031-C1)");

                    // C2: 최신 스펙 그래프로 후보 재평가 (stale 캐시 미사용)
                    List<SpecWorkResult> rescheduleResults = [];
                    try
                    {
                        rescheduleResults = await RunRescheduleAsync();
                        allResults.AddRange(rescheduleResults);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("reschedule", $"즉시 재스케줄 사이클 오류: {ex.Message}");
                        break;
                    }

                    // C4: 후속 후보 없으면 idle 대기로 전환하며 사유 기록
                    if (!rescheduleResults.Any(r => r.TriggeredReschedule))
                    {
                        string skipReason = rescheduleResults.Count == 0 || !rescheduleResults.Any(r => r.Action is "implement" or "auto-verify" or "repair")
                            ? "처리 가능한 후속 후보 없음"
                            : "추가 상태 전환 없음 — 현재 사이클 처리 완료";
                        _log.Info("reschedule",
                            $"즉시 재스케줄 종료 — {skipReason} (총 {rescheduleCount}회 완료, idle 대기 정책 전환, F-031-C4)");
                        RecordRescheduleSkipReason(skipReason, rescheduleCount);
                    }

                    latestBatch = rescheduleResults;
                }

                // 전체 사이클에서 처리된 스펙이 있으면 설정된 주기 대기, 없으면 30초 후 재확인
                var delay = allResults.Count > 0
                    ? TimeSpan.FromMinutes(_config.PollIntervalMinutes)
                    : TimeSpan.FromSeconds(30);

                if (allResults.Count == 0)
                    _log.Info("daemon", $"구현 대상 스펙 없음 — 30초 후 재확인");

                try
                {
                    await Task.Delay(delay, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
            }

            RemovePidFile();
            _log.Info("daemon", "데몬 종료");
        }
    }

    /// <summary>
    /// F-031: 상태 전환 후 즉시 재스케줄 경량 사이클.
    /// git sync 없이 최신 in-memory 스펙 그래프로 다음 후보를 재평가한다 (C2).
    /// auto-verify → 후보 탐색 → 처리 순으로 실행한다 (C3: review handoff 메타데이터 보존).
    /// </summary>
    private async Task<List<SpecWorkResult>> RunRescheduleAsync()
    {
        var results = new List<SpecWorkResult>();

        var stagedSpecs = FindSequentialStageSpecs()
            .Take(_config.MaxConcurrentSpecs)
            .ToList();

        // C2: 최신 스펙 그래프 기반 후보 탐색 (stale 캐시/이전 선택 결과 재사용 금지)
        var targets = FindTargetSpecs();
        if (targets.Count == 0 && stagedSpecs.Count == 0)
        {
            // C4: 후보 없음 사유를 선택 메타데이터에 기록
            LogNoRescheduleCandidates();
            return results;
        }

        _log.Info("reschedule", $"즉시 재스케줄 후보 구현 {targets.Count}개 / staged {stagedSpecs.Count}개 발견 — 처리 시작");

        var batch = targets.Take(_config.MaxConcurrentSpecs).ToList();
        foreach (var spec in batch)
        {
            if (_cts?.Token.IsCancellationRequested == true) break;
            var result = await ProcessSpecAsync(spec);
            results.Add(result);
        }

        foreach (var spec in stagedSpecs)
        {
            if (_cts?.Token.IsCancellationRequested == true) break;
            var result = await ProcessSequentialStageSpecAsync(spec);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// F-031-C4: 즉시 재스케줄이 불가한 사유를 로그와 selection metadata에 기록한다.
    /// 대기열 스펙이 있으나 의존성 미충족/사용자 입력 필요로 처리 불가한 경우 포함.
    /// </summary>
    private void LogNoRescheduleCandidates()
    {
        var allSpecs = _specStore.GetAll();
        var queuedSpecs = allSpecs.Where(s => _config.TargetStatuses.Contains(s.Status)).ToList();

        string skipReason;
        if (queuedSpecs.Count == 0)
        {
            skipReason = "대기열에 처리 대상 스펙 없음";
        }
        else
        {
            var blockedByDeps = queuedSpecs.Count(s =>
            {
                var completedIds = allSpecs
                    .Where(x => x.Status is "verified" or "done")
                    .Select(x => x.Id)
                    .ToHashSet();
                var allIds = allSpecs.Select(x => x.Id).ToHashSet();
                return s.Dependencies.Any(dep => allIds.Contains(dep) && !completedIds.Contains(dep));
            });
            // 미해결 질문이 남은 스펙은 "needs-review" 상태로 별도 카운팅
            var needsInput = allSpecs.Count(s =>
                string.Equals(s.Status, "needs-review", StringComparison.OrdinalIgnoreCase) && HasOpenQuestions(s));
            skipReason = $"대기열 스펙 {queuedSpecs.Count}개 중 처리 가능 후보 없음 " +
                         $"(의존성 미충족: {blockedByDeps}개, 사용자 입력 대기(needs-review): {needsInput}개)";
        }

        _log.Info("reschedule", $"즉시 재스케줄 후보 없음 — {skipReason} (F-031-C4)");

        // C4: selection metadata에 skip 사유 기록 (첫 번째 대기 스펙에 기록)
        var firstQueued = queuedSpecs.OrderBy(s => s.Id).FirstOrDefault();
        if (firstQueued != null)
        {
            firstQueued.Metadata ??= new Dictionary<string, object>();
            firstQueued.Metadata["lastRescheduleSkipReason"] = skipReason;
            firstQueued.Metadata["lastRescheduleSkipAt"] = DateTime.UtcNow.ToString("o");
            _specStore.Update(firstQueued);
        }
    }

    /// <summary>
    /// F-031-C4: 재스케줄 종료 사유를 runner 로그와 selection metadata에 기록한다.
    /// </summary>
    private void RecordRescheduleSkipReason(string reason, int completedCycles)
    {
        var allSpecs = _specStore.GetAll();
        var firstQueued = allSpecs
            .Where(s => _config.TargetStatuses.Contains(s.Status))
            .OrderBy(s => s.Id)
            .FirstOrDefault();

        if (firstQueued != null)
        {
            firstQueued.Metadata ??= new Dictionary<string, object>();
            firstQueued.Metadata["lastRescheduleSkipReason"] = reason;
            firstQueued.Metadata["lastRescheduleSkipAt"] = DateTime.UtcNow.ToString("o");
            firstQueued.Metadata["lastRescheduleCompletedCycles"] = completedCycles;
            _specStore.Update(firstQueued);
        }
    }

    /// <summary>
    /// 단일 스펙을 처리한다.
    /// </summary>
    private async Task<SpecWorkResult> ProcessSpecAsync(SpecNode spec)
    {
        var result = new SpecWorkResult
        {
            SpecId = spec.Id,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Action = "implement"
        };

        try
        {
            // 1. 스펙 상태를 working으로 변경
            MarkSpecWorking(spec);

            // 2. worktree 생성
            var (wtSuccess, worktreePath, branchName) = await _git.CreateWorktreeAsync(spec.Id);
            result.WorktreePath = worktreePath;
            result.BranchName = branchName;

            if (!wtSuccess)
            {
                result.Success = false;
                result.ErrorMessage = "Worktree 생성 실패";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, null, null, "execution-crash", "execution-crash", "developer", "implementation",
                    GetRetryNotBefore("execution-crash"), "execution-crash");
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            // 3. Copilot으로 구현 시도
            var specJson = BuildSpecPromptJson(spec);
            var previousReview = BuildPreviousReviewSection(spec);
            var copilotResult = await _copilot.ImplementSpecAsync(spec.Id, specJson, worktreePath, previousReview);

            if (!copilotResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = copilotResult.ErrorMessage ?? "Copilot 구현 실패";
                var copilotFailureType = ResolveCopilotFailureType(copilotResult);
                var disposition = copilotFailureType ?? "execution-crash";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, disposition, disposition, "developer", "implementation",
                    GetRetryNotBefore(disposition), disposition);
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            // 4. 변경사항 커밋
            var committed = await _git.CommitChangesAsync(spec.Id, $"[runner] Implement {spec.Id}: {spec.Title}");
            if (!committed)
            {
                result.Success = false;
                result.ErrorMessage = "변경사항 커밋 실패";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, "missing-evidence", "missing-evidence", "developer", "implementation");
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            // 5. 메인 브랜치로 머지
            // 머지 전: runner가 main 작업 디렉토리에 기록한 spec 파일의 uncommitted 변경사항을 버린다.
            // worktree 브랜치도 같은 spec 파일을 수정·커밋하면 git이 머지를 거부하기 때문.
            // runner는 머지 성공 후 즉시 status/metadata를 재작성하므로 이 변경사항은 복원된다.
            var specRelPath = Path.GetRelativePath(_projectRoot, Path.Combine(_specStore.SpecsDir, $"{spec.Id}.json"))
                .Replace('\\', '/');
            await _git.DiscardLocalChangesAsync(specRelPath);
            var (mergeSuccess, hasConflict) = await _git.MergeToMainAsync(spec.Id, _config.MainBranch);

            if (!mergeSuccess && hasConflict)
            {
                // 충돌 발생 → Copilot으로 해결 시도
                _log.Info("merge-resolve", "Copilot으로 머지 충돌 해결 시도", spec.Id);
                var resolveResult = await _copilot.ResolveMergeConflictAsync(spec.Id, _projectRoot);

                if (resolveResult.Success)
                {
                    var mergeCommitted = await _git.CommitMergeResolutionAsync(spec.Id);
                    if (mergeCommitted)
                    {
                        mergeSuccess = true;
                        _log.Info("merge-resolve", "충돌 해결 및 머지 완료", spec.Id);
                    }
                }

                if (!mergeSuccess)
                {
                    // 충돌 해결 실패 → 머지 중단
                    await _git.AbortMergeAsync();
                    result.Success = false;
                    result.ErrorMessage = "머지 충돌 해결 실패";
                    MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, "execution-crash", "execution-crash", "developer", "implementation",
                        GetRetryNotBefore("execution-crash"), "execution-crash");
                    result.Action = "requeue";
                    result.TriggeredReschedule = true;
                    return FinalizeResult(result);
                }
            }
            else if (!mergeSuccess)
            {
                result.Success = false;
                result.ErrorMessage = "머지 실패";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, "execution-crash", "execution-crash", "developer", "implementation",
                    GetRetryNotBefore("execution-crash"), "execution-crash");
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            // 6. 성공 → test-validation stage로 handoff하고 다음 구현 사이클로 넘긴다.
            spec = _specStore.Get(spec.Id) ?? spec;
            await PrepareSpecForSequentialValidationAsync(spec, worktreePath, branchName);

            // 7. 프로젝트 변경사항 push
            await _git.PushAsync(_config.RemoteName, _config.MainBranch);

            result.Success = true;
            result.Action = "handoff-review";
            result.TriggeredReschedule = true;
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("process", $"스펙 처리 중 예외: {ex.Message}", spec.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            // worktree가 존재하는 경우 경로를 메타데이터에 보존
            var exWorktreePath = _git.GetWorktreePath(spec.Id);
            var exWorktreeBranch = _git.GetBranchName(spec.Id);
            var exWorktreeExists = Directory.Exists(exWorktreePath);
            MarkSpecQueuedForRetry(spec, ex.Message,
                exWorktreeExists ? exWorktreePath : null,
                exWorktreeExists ? exWorktreeBranch : null,
                "execution-crash",
                "execution-crash",
                "developer",
                "implementation",
                GetRetryNotBefore("execution-crash"),
                "execution-crash");
            result.Action = "requeue";
            result.TriggeredReschedule = true;

            return FinalizeResult(result);
        }
    }

    /// <summary>
    /// 구현 대상 스펙을 탐색하고, readiness 그룹별로 이슈 연관도 점수 기반 재정렬을 수행한다 (F-015).
    /// 손상 스펙 복구는 RepairBrokenSpecsAsync()에서 별도로 최우선 처리한다 (F-025-C3).
    /// </summary>
    private List<SpecNode> FindTargetSpecs()
    {
        return EvaluateTargetSpecs(updateSelectionMetadata: true, logSelection: true)
            .ReadySpecs
            .Select(candidate => _specStore.Get(candidate.SpecId))
            .Where(spec => spec != null)
            .Cast<SpecNode>()
            .ToList();
    }

    private List<SpecNode> FindSequentialStageSpecs()
        => _specStore.GetAll()
            .Where(spec => string.Equals(spec.Status, "working", StringComparison.OrdinalIgnoreCase))
            .Where(spec => !string.Equals(NormalizeRunnerStage(GetMetadataString(spec.Metadata, "runnerStage")), ImplementationStage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(spec => GetRunnerStageOrder(NormalizeRunnerStage(GetMetadataString(spec.Metadata, "runnerStage"))))
            .ThenBy(spec => ParseIsoDate(GetMetadataString(spec.Metadata, "lastCompletedAt")) ?? DateTime.MaxValue)
            .ThenBy(spec => spec.Id)
            .ToList();

    private RunnerQueuePlan EvaluateTargetSpecs(bool updateSelectionMetadata, bool logSelection)
    {
        var allSpecs = _specStore.GetAll();
        var allSpecIds = allSpecs.Select(s => s.Id).ToHashSet();
        var completedIds = allSpecs
            .Where(s => s.Status is "verified" or "done")
            .Select(s => s.Id)
            .ToHashSet();

        var candidates = allSpecs
            .Where(s => s.Status != "working" && _config.TargetStatuses.Contains(s.Status))
            .ToList();

        var ready = new List<SpecNode>();
        var blocked = new List<RunnerBlockedSpec>();

        foreach (var spec in candidates)
        {
            var openQuestionCount = CountOpenQuestions(spec);
            var unmetDependencies = spec.Dependencies
                .Where(dep => allSpecIds.Contains(dep) && !completedIds.Contains(dep))
                .ToArray();
            var retryNotBefore = spec.Metadata == null ? null : GetMetadataIsoDate(spec.Metadata, "retryNotBefore");

            if (retryNotBefore.HasValue && retryNotBefore.Value > DateTime.UtcNow)
            {
                blocked.Add(new RunnerBlockedSpec
                {
                    SpecId = spec.Id,
                    Title = spec.Title,
                    Status = spec.Status,
                    Reason = "retry-cooldown",
                    UnmetDependencies = unmetDependencies,
                    OpenQuestionCount = openQuestionCount,
                    RetryNotBefore = retryNotBefore.Value.ToString("o")
                });
                continue;
            }

            if (openQuestionCount > 0)
            {
                blocked.Add(new RunnerBlockedSpec
                {
                    SpecId = spec.Id,
                    Title = spec.Title,
                    Status = spec.Status,
                    Reason = unmetDependencies.Length > 0 ? "open-questions-and-unmet-dependencies" : "open-questions",
                    UnmetDependencies = unmetDependencies,
                    OpenQuestionCount = openQuestionCount,
                    RetryNotBefore = retryNotBefore?.ToString("o")
                });
                continue;
            }

            if (unmetDependencies.Length > 0)
            {
                blocked.Add(new RunnerBlockedSpec
                {
                    SpecId = spec.Id,
                    Title = spec.Title,
                    Status = spec.Status,
                    Reason = "unmet-dependencies",
                    UnmetDependencies = unmetDependencies,
                    OpenQuestionCount = 0,
                    RetryNotBefore = retryNotBefore?.ToString("o")
                });
                continue;
            }

            ready.Add(spec);
        }

        var sortedReady = ready
            .OrderBy(s => GetMetadataInt(s.Metadata ?? new Dictionary<string, object>(), "implementationAttempts"))
            .ThenByDescending(s => GetIssuePriorityScore(s))
            .ThenBy(s => s.Dependencies.Count)
            .ThenBy(s => s.Id)
            .Select((spec, index) => new RunnerQueueCandidate
            {
                SpecId = spec.Id,
                Title = spec.Title,
                Status = spec.Status,
                Rank = index + 1,
                IssuePriorityScore = GetIssuePriorityScore(spec),
                IsFallback = GetIssuePriorityScore(spec) == 0.0,
                DependencyCount = spec.Dependencies.Count,
                Dependencies = spec.Dependencies.ToArray()
            })
            .ToList();

        if (logSelection)
        {
            if (blocked.Count > 0)
                _log.Info("queue-priority", $"처리 불가 스펙 {blocked.Count}개 제외 (의존성 미충족 또는 사용자 입력 필요)");

            LogQueueSelection(sortedReady, updateSelectionMetadata);
        }

        return new RunnerQueuePlan
        {
            TargetStatuses = _config.TargetStatuses,
            TotalCandidates = candidates.Count,
            ReadyCount = sortedReady.Count,
            BlockedCount = blocked.Count,
            NextSpecId = sortedReady.FirstOrDefault()?.SpecId,
            ReadySpecs = sortedReady,
            BlockedSpecs = blocked
        };
    }

    /// <summary>
    /// F-015-C2: metadata에서 issuePriorityScore를 읽어 반환한다.
    /// 연동 비활성화 또는 이슈 없는 경우 0.0 반환 (C4: starvation 없이 fallback 처리).
    /// </summary>
    private static double GetIssuePriorityScore(SpecNode spec)
    {
        if (spec.Metadata == null) return 0.0;
        if (!spec.Metadata.TryGetValue("issuePriorityScore", out var val)) return 0.0;
        if (val is System.Text.Json.JsonElement je && je.TryGetDouble(out var d)) return d;
        if (val is double dbl) return dbl;
        if (double.TryParse(val?.ToString(), out var dp)) return dp;
        return 0.0;
    }

    /// <summary>
    /// metadata.questions에서 open 상태 질문이 남아 있는지 확인한다.
    /// </summary>
    private static bool HasOpenQuestions(SpecNode spec)
        => SpecReviewEvaluator.HasOpenQuestions(spec);

    private static int CountOpenQuestions(SpecNode spec)
        => SpecReviewEvaluator.CountOpenQuestions(spec);

    /// <summary>
    /// F-015-C6: 큐 정렬 결과를 로그에 기록하고, 선택된 스펙의 metadata.selectionReason을 갱신한다.
    /// </summary>
    private void LogQueueSelection(List<RunnerQueueCandidate> rankedSpecs, bool updateSelectionMetadata)
    {
        if (rankedSpecs.Count == 0) return;

        var topN = rankedSpecs.Take(Math.Min(5, rankedSpecs.Count)).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($"큐 우선순위 재정렬 결과 (상위 {topN.Count}/{rankedSpecs.Count}개):");

        for (int i = 0; i < topN.Count; i++)
        {
            var spec = topN[i];
            var label = spec.IsFallback ? "fallback(no-issue-signal)" : $"score={spec.IssuePriorityScore:F1}";
            sb.Append($" [{i + 1}]{spec.SpecId}({label})");
        }

        _log.Info("queue-priority", sb.ToString());

        if (!updateSelectionMetadata)
            return;

        var selected = _specStore.Get(rankedSpecs[0].SpecId);
        if (selected == null)
            return;

        selected.Metadata ??= new Dictionary<string, object>();
        selected.Metadata["selectionReason"] = new Dictionary<string, object>
        {
            ["selectedAt"] = DateTime.UtcNow.ToString("o"),
            ["issuePriorityScore"] = rankedSpecs[0].IssuePriorityScore,
            ["isFallback"] = rankedSpecs[0].IsFallback,
            ["rank"] = 1,
            ["totalCandidates"] = rankedSpecs.Count
        };
        _specStore.Update(selected);
    }

    // ── 스펙 상태 변경 ─────────────────────────────────────────

    /// <summary>
    /// 스펙을 working 상태로 마킹한다.
    /// </summary>
    private void MarkSpecWorking(SpecNode spec)
    {
        var prevStatus = spec.Status;
        spec.Status = "working";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["runnerInstanceId"] = _instanceId;
        spec.Metadata["runnerProcessId"] = Environment.ProcessId;
        spec.Metadata["runnerStartedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["runnerStage"] = ImplementationStage;
        spec.Metadata.Remove("retryNotBefore");
        spec.Metadata.Remove("lastErrorType");
        spec.Metadata["implementationPlan"] = new
        {
            triggeredFrom = prevStatus,
            approach = $"AI Runner가 '{spec.Id}: {spec.Title}' 구현을 시작합니다.",
            startedAt = DateTime.UtcNow.ToString("o"),
        };
        AppendActivity(spec,
            role: "developer",
            summary: $"스펙 '{spec.Id}' 구현 사이클을 시작했다.",
            outcome: "handoff",
            kind: "implementation",
            actor: _instanceId,
            model: _config.CopilotModel,
            statusFrom: prevStatus,
            statusTo: "working");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → working", spec.Id);
    }

    /// <summary>
    /// 구현 완료 메타데이터를 기록하고 test-validation 입력을 준비한다.
    /// </summary>
    private async Task PrepareSpecForSequentialValidationAsync(SpecNode spec, string? worktreePath = null, string? worktreeBranch = null)
    {
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastCompletedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastCompletedBy"] = _instanceId;
        spec.Metadata["runnerStage"] = TestValidationStage;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");
        spec.Metadata.Remove("lastErrorType");
        spec.Metadata.Remove("retryNotBefore");
        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("verificationSource");

        if (worktreePath != null)
        {
            spec.Metadata["worktreePath"] = worktreePath;
            spec.Metadata["worktreeBranch"] = worktreeBranch ?? _git.GetBranchName(spec.Id);
        }

        AppendActivity(spec,
            role: "developer",
            summary: $"구현 단계를 완료하고 test-validation stage로 전달한다.",
            outcome: "handoff",
            kind: "implementation",
            actor: _instanceId,
            model: _config.CopilotModel);

        _specStore.Update(spec);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 자동 재시도를 위해 스펙을 queued로 되돌린다.
    /// </summary>
    private void MarkSpecQueuedForRetry(
        SpecNode spec,
        string error,
        string? worktreePath = null,
        string? worktreeBranch = null,
        string reviewDisposition = "test-failed",
        string? reviewReason = "test-failed",
        string activityRole = "system",
        string activityKind = "recovery",
        DateTime? retryNotBefore = null,
        string? errorType = null)
    {
        var prevStatus = spec.Status;
        spec.Status = "queued";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastError"] = error;
        spec.Metadata["lastErrorAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastErrorType"] = string.IsNullOrWhiteSpace(errorType) ? reviewDisposition : errorType;
        spec.Metadata["runnerInstanceId"] = _instanceId;
        spec.Metadata.Remove("runnerProcessId");
        spec.Metadata["reviewDisposition"] = reviewDisposition;
        spec.Metadata.Remove("runnerStage");
        if (!string.IsNullOrWhiteSpace(reviewReason))
        {
            spec.Metadata["reviewReason"] = reviewReason;
        }
        else
        {
            spec.Metadata.Remove("reviewReason");
        }
        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("verificationSource");
        spec.Metadata.Remove("questionStatus");
        spec.Metadata.Remove("implementationPlan");

        if (retryNotBefore.HasValue)
        {
            spec.Metadata["retryNotBefore"] = retryNotBefore.Value.ToString("o");
        }
        else
        {
            spec.Metadata.Remove("retryNotBefore");
        }

        var attempts = GetMetadataInt(spec.Metadata, "implementationAttempts") + 1;
        spec.Metadata["implementationAttempts"] = attempts;

        if (worktreePath != null)
        {
            spec.Metadata["worktreePath"] = worktreePath;
            spec.Metadata["worktreeBranch"] = worktreeBranch ?? _git.GetBranchName(spec.Id);
        }

        var conditionUpdates = ResetConditionsForRequeue(spec);
        AppendActivity(spec,
            role: activityRole,
            summary: $"자동 재시도를 위해 스펙을 queued로 되돌린다.",
            outcome: "requeue",
            kind: activityKind,
            comment: error,
            actor: _instanceId,
            model: activityRole == "developer" ? _config.CopilotModel : null,
            statusFrom: prevStatus,
            statusTo: "queued",
            issues: BuildActivityIssues(reviewDisposition),
            conditionUpdates: conditionUpdates);

        _specStore.Update(spec);
        var cooldownSuffix = retryNotBefore.HasValue
            ? $", retryNotBefore={retryNotBefore.Value:o}"
            : string.Empty;
        _log.Warn("status", $"스펙 상태 변경: {prevStatus} → queued ({reviewDisposition}: {error}{cooldownSuffix})", spec.Id);
    }

    private async Task MarkSpecNeedsReviewAsync(SpecNode spec, string summary, string? worktreePath, string? worktreeBranch)
    {
        var prevStatus = spec.Status;
        spec.Status = "needs-review";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata.Remove("runnerStage");
        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("verificationSource");
        spec.Metadata.Remove("lastErrorType");
        spec.Metadata.Remove("retryNotBefore");

        if (worktreePath != null)
        {
            spec.Metadata["worktreePath"] = worktreePath;
            spec.Metadata["worktreeBranch"] = worktreeBranch ?? _git.GetBranchName(spec.Id);
        }

        NormalizeConditionsForUserReview(spec, HasOpenQuestions(spec));
        AppendActivity(spec,
            role: "tester",
            summary: summary,
            outcome: "needs-review",
            kind: "verification",
            actor: GetMetadataString(spec.Metadata, "lastReviewBy") ?? _instanceId,
            model: "gpt-5-mini",
            statusFrom: prevStatus,
            statusTo: "needs-review");

        _specStore.Update(spec);
        await CleanupWorktreeFromMetadataAsync(spec);
        spec.Metadata.Remove("worktreePath");
        spec.Metadata.Remove("worktreeBranch");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → needs-review ({summary})", spec.Id);
    }

    private void MarkSpecVerified(SpecNode spec, string verificationSource)
    {
        var prevStatus = spec.Status;
        spec.Status = "verified";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata.Remove("runnerStage");
        spec.Metadata["lastVerifiedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastVerifiedBy"] = _instanceId;
        spec.Metadata["verificationSource"] = verificationSource;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");
        spec.Metadata.Remove("lastErrorType");
        spec.Metadata.Remove("retryNotBefore");
        spec.Metadata.Remove("worktreePath");
        spec.Metadata.Remove("worktreeBranch");
        spec.Metadata.Remove("reviewReason");
        AppendActivity(spec,
            role: "tester",
            summary: "모든 condition이 통과해 feature를 verified로 확정했다.",
            outcome: "verified",
            kind: "verification",
            actor: GetMetadataString(spec.Metadata, "lastReviewBy") ?? _instanceId,
            model: "gpt-5-mini",
            statusFrom: prevStatus,
            statusTo: "verified");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → verified (모든 컨디션 충족, 자동 검증 완료)", spec.Id);
    }

    private void MarkSpecDone(SpecNode spec, string prevStatus, string reason)
    {
        spec.Status = "done";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata.Remove("runnerStage");
        spec.Metadata["lastDoneAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastDoneBy"] = _instanceId;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");
        spec.Metadata.Remove("lastErrorType");
        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("retryNotBefore");
        spec.Metadata.Remove("questionStatus");
        spec.Metadata.Remove("requiresUserInput");
        spec.Metadata.Remove("worktreePath");
        spec.Metadata.Remove("worktreeBranch");
        spec.Metadata.Remove("reviewReason");
        AppendActivity(spec,
            role: "tester",
            summary: "질문이 없고 모든 검증이 끝나 task를 done으로 확정했다.",
            outcome: "done",
            kind: "verification",
            actor: GetMetadataString(spec.Metadata, "lastReviewBy") ?? _instanceId,
            model: "gpt-5-mini",
            statusFrom: prevStatus,
            statusTo: "done");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → done ({reason})", spec.Id);
    }

    /// <summary>
    /// 스펙 메타데이터에 기록된 worktree를 정리한다. verified/done/queued 전환 시 호출.
    /// </summary>
    private async Task CleanupWorktreeFromMetadataAsync(SpecNode spec)
    {
        if (spec.Metadata == null) return;
        if (!spec.Metadata.TryGetValue("worktreePath", out var pathObj)) return;
        var path = pathObj?.ToString();
        if (string.IsNullOrEmpty(path)) return;

        _log.Info("worktree-cleanup", $"검토 완료 후 worktree 정리: {path}", spec.Id);
        try
        {
            await _git.RemoveWorktreeAsync(spec.Id);
        }
        catch (Exception ex)
        {
            _log.Warn("worktree-cleanup", $"worktree 정리 실패: {ex.Message}", spec.Id);
        }
    }

    private async Task<SpecNode?> RunSequentialValidationAsync(SpecNode spec, string worktreePath, string branchName, SpecWorkResult result)
    {
        var specJson = BuildSpecPromptJson(spec);
        var reviewContext = BuildReviewContext(spec);
        var reviewResult = await _copilot.ReviewSpecAsync(spec, specJson, reviewContext, _projectRoot, _instanceId);

        if (!reviewResult.Success)
        {
            result.Success = false;
            result.ErrorMessage = reviewResult.ErrorMessage ?? "검토 분석 실패";
            var reviewFailureType = ResolveCopilotFailureType(reviewResult);
            var disposition = reviewFailureType ?? "missing-evidence";
            MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, disposition, disposition, "system", "validation",
                GetRetryNotBefore(disposition), disposition);
            return null;
        }

        var reviewedSpec = _specStore.Get(spec.Id);
        if (!HasPersistedReviewResult(reviewedSpec))
        {
            result.Success = false;
            result.ErrorMessage = "spec-append-review 결과가 스펙에 반영되지 않았습니다.";
            _log.Warn("review", result.ErrorMessage, spec.Id);
            MarkSpecQueuedForRetry(spec, result.ErrorMessage, worktreePath, branchName, "missing-evidence", "missing-evidence", "system", "validation");
            return null;
        }

        reviewedSpec!.Metadata ??= new Dictionary<string, object>();
        var evaluation = SpecReviewEvaluator.Evaluate(reviewedSpec);
        var hasOpenQuestions = HasOpenQuestions(reviewedSpec);
        var reviewDisposition = GetMetadataString(reviewedSpec.Metadata, "reviewDisposition") ?? "missing-evidence";
        var reviewReason = GetMetadataString(reviewedSpec.Metadata, "reviewReason");

        if (string.Equals(reviewedSpec.Status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupWorktreeFromMetadataAsync(reviewedSpec);
            MarkSpecVerified(reviewedSpec, GetMetadataString(reviewedSpec.Metadata, "verificationSource") ?? "copilot-cli-review");
            return reviewedSpec;
        }

        if (string.Equals(reviewedSpec.Status, "done", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupWorktreeFromMetadataAsync(reviewedSpec);
            MarkSpecDone(reviewedSpec, "working", "sequential tester finalization");
            return reviewedSpec;
        }

        if (hasOpenQuestions || evaluation.RequiresManualVerification || string.Equals(reviewDisposition, "user-test-required", StringComparison.OrdinalIgnoreCase))
        {
            await MarkSpecNeedsReviewAsync(reviewedSpec,
                hasOpenQuestions ? "사용자 질문 응답이 필요하다." : "사용자 수동 테스트 또는 확인이 필요하다.",
                worktreePath,
                branchName);
            return reviewedSpec;
        }

        MarkSpecQueuedForRetry(reviewedSpec,
            result.ErrorMessage ?? reviewReason ?? reviewDisposition,
            worktreePath,
            branchName,
            reviewDisposition,
            string.IsNullOrWhiteSpace(reviewReason) ? reviewDisposition : reviewReason,
            "tester",
            "verification");
        return reviewedSpec;
    }

    private async Task<SpecWorkResult> ProcessSequentialStageSpecAsync(SpecNode spec)
    {
        var stage = NormalizeRunnerStage(GetMetadataString(spec.Metadata, "runnerStage"));
        return string.Equals(stage, ReviewStage, StringComparison.OrdinalIgnoreCase)
            ? await ProcessReviewStageSpecAsync(spec)
            : await ProcessTestValidationStageSpecAsync(spec);
    }

    private async Task<SpecWorkResult> ProcessTestValidationStageSpecAsync(SpecNode spec)
    {
        var result = new SpecWorkResult
        {
            SpecId = spec.Id,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Action = "test-validation"
        };

        try
        {
            var worktreePath = GetMetadataString(spec.Metadata, "worktreePath") ?? _git.GetWorktreePath(spec.Id);
            var branchName = GetMetadataString(spec.Metadata, "worktreeBranch") ?? _git.GetBranchName(spec.Id);

            if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
            {
                result.Success = false;
                result.ErrorMessage = "test-validation stage에서 worktree를 찾지 못해 자동 재시도 대기열로 되돌립니다.";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, null, null, "execution-crash", "execution-crash", "system", "validation",
                    GetRetryNotBefore("execution-crash"), "execution-crash");
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            spec.Metadata ??= new Dictionary<string, object>();
            spec.Metadata["runnerStage"] = ReviewStage;
            spec.Metadata["lastValidatedAt"] = DateTime.UtcNow.ToString("o");
            AppendActivity(spec,
                role: "tester",
                summary: "test-validation 단계를 마치고 review stage로 전달한다.",
                outcome: "handoff",
                kind: "verification",
                actor: _instanceId,
                model: "gpt-5-mini");
            _specStore.Update(spec);

            result.Success = true;
            result.Action = "handoff-review";
            result.TriggeredReschedule = false;
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("test-validation", $"test-validation 처리 중 예외: {ex.Message}", spec.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            MarkSpecQueuedForRetry(spec, ex.Message,
                GetMetadataString(spec.Metadata, "worktreePath"),
                GetMetadataString(spec.Metadata, "worktreeBranch"),
                "execution-crash",
                "execution-crash",
                "system",
                "validation",
                GetRetryNotBefore("execution-crash"),
                "execution-crash");
            result.Action = "requeue";
            result.TriggeredReschedule = true;
            return FinalizeResult(result);
        }
    }

    private async Task<SpecWorkResult> ProcessReviewStageSpecAsync(SpecNode spec)
    {
        var result = new SpecWorkResult
        {
            SpecId = spec.Id,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Action = "review"
        };

        try
        {
            var worktreePath = GetMetadataString(spec.Metadata, "worktreePath") ?? _git.GetWorktreePath(spec.Id);
            var branchName = GetMetadataString(spec.Metadata, "worktreeBranch") ?? _git.GetBranchName(spec.Id);

            if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
            {
                result.Success = false;
                result.ErrorMessage = "review stage worktree가 없어 자동 재시도 대기열로 되돌립니다.";
                MarkSpecQueuedForRetry(spec, result.ErrorMessage, null, null, "execution-crash", "execution-crash", "system", "validation",
                    GetRetryNotBefore("execution-crash"), "execution-crash");
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            var finalizedSpec = await RunSequentialValidationAsync(spec, worktreePath, branchName, result);
            if (finalizedSpec == null)
            {
                result.Action = "requeue";
                result.TriggeredReschedule = true;
                return FinalizeResult(result);
            }

            result.Success = !string.Equals(finalizedSpec.Status, "queued", StringComparison.OrdinalIgnoreCase);
            result.Action = finalizedSpec.Status switch
            {
                "verified" => "verify",
                "done" => "done",
                "needs-review" => "needs-review",
                _ => "requeue"
            };
            result.TriggeredReschedule = true;
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("review-stage", $"review stage 처리 중 예외: {ex.Message}", spec.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            MarkSpecQueuedForRetry(spec, ex.Message,
                GetMetadataString(spec.Metadata, "worktreePath"),
                GetMetadataString(spec.Metadata, "worktreeBranch"),
                "execution-crash",
                "execution-crash",
                "system",
                "validation",
                GetRetryNotBefore("execution-crash"),
                "execution-crash");
            result.Action = "requeue";
            result.TriggeredReschedule = true;
            return FinalizeResult(result);
        }
    }

    private async Task<List<SpecWorkResult>> AutoVerifyReviewedSpecsAsync()
    {
        var results = new List<SpecWorkResult>();
        var reviewedSpecs = _specStore.GetAll()
            .Where(spec => spec.Status == "needs-review")
            .OrderBy(spec => spec.Id)
            .ToList();

        if (reviewedSpecs.Count == 0)
        {
            return results;
        }

        foreach (var spec in reviewedSpecs)
        {
            var verifiedAtUtc = DateTime.UtcNow;
            var promotedConditions = SpecReviewEvaluator.PromoteVerifiedConditionsFromArtifacts(
                spec,
                _instanceId,
                "runner-review-artifacts",
                verifiedAtUtc);
            var hasOpenQuestions = HasOpenQuestions(spec);
            SpecReviewEvaluator.NormalizeConditionReviewStates(spec, hasOpenQuestions);
            var decision = SpecReviewEvaluator.ResolveDecision(spec, hasOpenQuestions);

            if (!decision.IsFinal)
            {
                var currentDisposition = spec.Metadata != null && spec.Metadata.TryGetValue("reviewDisposition", out var rawDisposition)
                    ? rawDisposition?.ToString()
                    : null;
                var currentReason = spec.Metadata != null && spec.Metadata.TryGetValue("reviewReason", out var rawReason)
                    ? rawReason?.ToString()
                    : null;
                var shouldPersist = promotedConditions > 0
                    || !string.Equals(currentDisposition, decision.ReviewDisposition, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(currentReason, decision.ReviewReason, StringComparison.OrdinalIgnoreCase);

                if (shouldPersist)
                {
                    spec.Metadata ??= new Dictionary<string, object>();
                    spec.Metadata["reviewDisposition"] = decision.ReviewDisposition;
                    if (!string.IsNullOrWhiteSpace(decision.ReviewReason))
                    {
                        spec.Metadata["reviewReason"] = decision.ReviewReason;
                    }
                    else
                    {
                        spec.Metadata.Remove("reviewReason");
                    }
                    _specStore.Update(spec);
                }

                if (promotedConditions > 0)
                {
                    _log.Info("condition-auto-verify",
                        $"review loop가 테스트/evidence를 확인해 condition {promotedConditions}건을 verified로 승격", spec.Id);
                }

                continue;
            }

            var timestamp = DateTime.UtcNow.ToString("o");
            await CleanupWorktreeFromMetadataAsync(spec);
            spec.Metadata ??= new Dictionary<string, object>();
            spec.Metadata["reviewDisposition"] = decision.ReviewDisposition;
            spec.Metadata.Remove("reviewReason");

            if (string.Equals(decision.SpecStatus, "done", StringComparison.OrdinalIgnoreCase))
            {
                spec.Metadata["verificationSource"] = "runner-review-pass";
                MarkSpecDone(spec, "needs-review", "review loop 최종 판정 완료");
            }
            else
            {
                MarkSpecVerified(spec, "runner-review-pass");
            }

            results.Add(new SpecWorkResult
            {
                SpecId = spec.Id,
                Success = true,
                Action = string.Equals(decision.SpecStatus, "done", StringComparison.OrdinalIgnoreCase)
                    ? "auto-complete-task"
                    : "auto-verify",
                StartedAt = timestamp,
                CompletedAt = timestamp,
                TriggeredReschedule = true // F-031-C1: needs-review → verified 전환
            });
        }

        return results;
    }

    /// <summary>
    /// F-025-C3/C4: 손상 스펙 JSON을 최우선으로 복구 시도한다.
    /// 진단 캐시와 fresh scan 결과를 합쳐 미해결 항목을 처리하며,
    /// 복구 성공 시 resolved, 실패 시 escalated로 마킹한다.
    /// </summary>
    private async Task<List<SpecWorkResult>> RepairBrokenSpecsAsync()
    {
        var results = new List<SpecWorkResult>();

        // Fresh scan: specsDir에서 파싱 불가 파일을 직접 탐색 (F-025-C3)
        var freshBroken = _diagService.ScanAndUpdate(_specStore.SpecsDir);

        // 진단 캐시의 미해결 항목과 합산
        var unresolved = _diagService.GetUnresolved(_specStore.SpecsDir);

        if (unresolved.Count == 0)
            return results;

        _log.Info("repair", $"손상 스펙 {unresolved.Count}개 발견 — 일반 queued 스펙보다 최우선 처리 (F-025-C3)");

        foreach (var record in unresolved)
        {
            // 무한 재투입 방지 (F-025-C4): 복구 시도 횟수 초과 시 escalated로 승격
            const int maxRepairAttempts = 3;
            if (record.RepairAttempts >= maxRepairAttempts)
            {
                var escalateReason = $"복구 시도 {record.RepairAttempts}회 초과 — 수동 검토 필요";
                _diagService.MarkEscalated(record.SpecId, escalateReason);
                _log.Warn("repair", $"손상 스펙 수동 검토 승격: {record.SpecId} ({escalateReason})", record.SpecId);
                results.Add(new SpecWorkResult
                {
                    SpecId = record.SpecId,
                    Success = false,
                    Action = "repair-escalated",
                    ErrorMessage = escalateReason,
                    StartedAt = DateTime.UtcNow.ToString("o"),
                    CompletedAt = DateTime.UtcNow.ToString("o"),
                });
                continue;
            }

            var repairResult = await AttemptRepairAsync(record);
            results.Add(repairResult);
        }

        return results;
    }

    /// <summary>
    /// F-025-C4: 단일 손상 스펙 JSON 복구를 시도한다.
    /// 최근 정상 백업, 캐시 진단, 기존 파일 내용을 근거로 최소 수정 원칙 적용.
    /// 복구 후 spec-validate 수준 재검증을 수행한다.
    /// </summary>
    private async Task<SpecWorkResult> AttemptRepairAsync(BrokenSpecDiagRecord record)
    {
        var result = new SpecWorkResult
        {
            SpecId = record.SpecId,
            Action = "repair",
            StartedAt = DateTime.UtcNow.ToString("o"),
        };

        _log.Info("repair", $"손상 스펙 복구 시도 #{record.RepairAttempts + 1}: {record.SpecId} (오류: {record.ErrorMessage})", record.SpecId);

        // 복구 시도 횟수 증가 (캐시 갱신은 선택 근거와 함께 기록)
        UpdateRepairAttemptCount(record);

        try
        {
            // F-025-C4: 최근 정상 백업 확인 (SpecStore.Backup 경로)
            var backupDir = Path.Combine(Path.GetDirectoryName(record.FilePath)!, ".backup");
            string? backupContent = FindLatestBackup(backupDir, record.SpecId);

            // 기존 손상 파일 내용 읽기 (복구 프롬프트에 포함)
            string? damagedContent = null;
            try { damagedContent = File.ReadAllText(record.FilePath); } catch { /* ignore */ }

            // Copilot에 복구 요청 (최소 수정 원칙)
            var repairPrompt = BuildRepairPrompt(record, damagedContent, backupContent);
            var copilotResult = await _copilot.RepairSpecAsync(
                record.SpecId, repairPrompt, _specStore.SpecsDir
            );

            if (!copilotResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = copilotResult.ErrorMessage ?? "Copilot 복구 실패";
                _log.Error("repair", $"복구 실패: {record.SpecId} — {result.ErrorMessage}", record.SpecId);
                return FinalizeResult(result);
            }

            // F-025-C4: 복구 후 spec-validate 수준 재검증
            var validator = new SpecValidator();
            SpecNode? repaired = null;
            try
            {
                var repairedJson = File.ReadAllText(record.FilePath);
                repaired = JsonSerializer.Deserialize<SpecNode>(repairedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                // 복구 후에도 파싱 실패 → 무한 재투입 방지 (F-025-C4)
                var failReason = $"복구 후 재검증 파싱 실패: {ex.Message}";
                _diagService.RecordBroken(record.FilePath, ex);
                result.Success = false;
                result.ErrorMessage = failReason;
                _log.Error("repair", $"{record.SpecId}: {failReason}", record.SpecId);
                return FinalizeResult(result);
            }

            if (repaired != null)
            {
                var validation = validator.ValidateSpec(repaired);
                if (!validation.IsValid)
                {
                    var failReason = $"복구 후 재검증 실패: {string.Join("; ", validation.Errors.Select(e => e.Message))}";
                    result.Success = false;
                    result.ErrorMessage = failReason;
                    _log.Warn("repair", $"{record.SpecId}: {failReason}", record.SpecId);
                    return FinalizeResult(result);
                }
            }

            // F-025-C5: 복구 성공 → resolved로 마킹
            _diagService.MarkResolved(record.FilePath);
            result.Success = true;
            _log.Info("repair", $"손상 스펙 복구 성공: {record.SpecId}", record.SpecId);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _log.Error("repair", $"복구 예외: {record.SpecId} — {ex.Message}", record.SpecId);
        }

        return FinalizeResult(result);
    }

    /// <summary>진단 캐시에서 복구 시도 횟수를 증가시키고 selectionReason을 기록한다 (F-025-C3).</summary>
    private void UpdateRepairAttemptCount(BrokenSpecDiagRecord record)
    {
        // 진단 캐시를 직접 수정 (BrokenSpecDiagService 재활용)
        try
        {
            if (!File.Exists(_diagService.DiagCachePath)) return;
            var json = File.ReadAllText(_diagService.DiagCachePath);
            var cache = JsonSerializer.Deserialize<BrokenSpecDiagCache>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cache == null) return;

            var r = cache.Records.Find(rec => rec.SpecId == record.SpecId && rec.Status == "unresolved");
            if (r != null)
            {
                r.RepairAttempts++;
                r.LastCheckedAt = DateTime.UtcNow.ToString("o");
                File.WriteAllText(_diagService.DiagCachePath,
                    JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            record.RepairAttempts = r?.RepairAttempts ?? record.RepairAttempts + 1;
        }
        catch { /* ignore */ }
    }

    /// <summary>백업 디렉토리에서 가장 최근 정상 백업 내용을 반환한다.</summary>
    private static string? FindLatestBackup(string backupDir, string specId)
    {
        if (!Directory.Exists(backupDir)) return null;
        var backups = Directory.GetDirectories(backupDir)
            .OrderDescending()
            .ToList();
        foreach (var dir in backups)
        {
            var file = Path.Combine(dir, $"{specId}.json");
            if (File.Exists(file))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    // 정상적으로 파싱되는지 확인
                    JsonSerializer.Deserialize<object>(content);
                    return content;
                }
                catch { /* 백업도 손상된 경우 다음 백업 시도 */ }
            }
        }
        return null;
    }

    /// <summary>Copilot에 전달할 복구 프롬프트를 구성한다 (최소 수정 원칙, F-025-C4).</summary>
    private static string BuildRepairPrompt(
        BrokenSpecDiagRecord record, string? damagedContent, string? backupContent)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"스펙 파일 '{record.SpecId}.json'이 JSON 파싱 오류로 손상되었습니다.");
        sb.AppendLine($"오류: {record.ErrorMessage}");
        if (record.Line.HasValue)
            sb.AppendLine($"위치: line {record.Line}, column {record.Column}");
        sb.AppendLine();
        sb.AppendLine("다음 원칙으로 최소한의 수정만 적용하여 파일을 복구하세요:");
        sb.AppendLine("- JSON 구문 오류만 수정 (콘텐츠 변경 금지)");
        sb.AppendLine("- 최근 백업이 있으면 백업을 기준으로 복구");
        sb.AppendLine("- 복구 불가한 경우 유효한 최소 스펙 JSON 구조로 대체");
        sb.AppendLine($"- 추가 컨텍스트가 필요하면 상위 경로에서 flow.ps1를 찾아 `flow.ps1 spec-get {record.SpecId}`로 최신 스펙을 다시 조회하되, 현재 파일이 손상되어 명령이 실패할 수 있음을 감안할 것");
        sb.AppendLine();

        if (damagedContent != null)
        {
            sb.AppendLine("=== 현재 손상된 파일 내용 ===");
            sb.AppendLine(damagedContent.Length > 2000
                ? damagedContent[..2000] + "\n... (truncated)"
                : damagedContent);
            sb.AppendLine();
        }

        if (backupContent != null)
        {
            sb.AppendLine("=== 최근 정상 백업 내용 ===");
            sb.AppendLine(backupContent.Length > 2000
                ? backupContent[..2000] + "\n... (truncated)"
                : backupContent);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 비정상 종료 복구: working 상태의 스펙을 needs-review로 전환한다.</summary>
    private int RecoverFromCrash()
    {
        var allSpecs = _specStore.GetAll();
        var staleSpecs = allSpecs.Where(s => s.Status == "working").ToList();

        if (staleSpecs.Count == 0) return 0;

        _log.Warn("recovery", $"비정상 종료된 작업 {staleSpecs.Count}개 발견, 복구 중...");

        var recovered = 0;
        foreach (var spec in staleSpecs)
        {
            var prevInstanceId = spec.Metadata?.ContainsKey("runnerInstanceId") == true
                ? spec.Metadata["runnerInstanceId"]?.ToString()
                : "unknown";
            var prevProcessId = spec.Metadata == null ? 0 : GetMetadataInt(spec.Metadata, "runnerProcessId");

            if (prevProcessId == Environment.ProcessId && prevInstanceId == _instanceId)
            {
                continue;
            }

            if (prevProcessId > 0 && IsProcessRunning(prevProcessId))
            {
                _log.Info("recovery", $"working 스펙 유지: {spec.Id} (이전 runner PID {prevProcessId}가 아직 실행 중)", spec.Id);
                continue;
            }

            // 현재 인스턴스가 아닌 이전 인스턴스의 작업만 복구
            if (prevInstanceId != _instanceId || prevProcessId != Environment.ProcessId)
            {
                // worktree가 존재하면 경로를 메타데이터에 보존 (검토 에이전트가 확인 가능)
                var crashWorktreePath = _git.GetWorktreePath(spec.Id);
                var crashWorktreeExists = Directory.Exists(crashWorktreePath);
                MarkSpecQueuedForRetry(spec, $"Runner 인스턴스 비정상 종료 (이전 인스턴스: {prevInstanceId})",
                    crashWorktreeExists ? crashWorktreePath : null,
                    crashWorktreeExists ? _git.GetBranchName(spec.Id) : null,
                    "execution-crash",
                    "execution-crash",
                    "system",
                    "recovery",
                    GetRetryNotBefore("execution-crash"),
                    "execution-crash");
                recovered++;
            }
        }

        return recovered;
    }

    private static string? GetMetadataString(Dictionary<string, object>? metadata, string key)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value.ToString();
    }

    private static string NormalizeRunnerStage(string? stage)
        => stage?.Trim() switch
        {
            null or "" => ImplementationStage,
            "review-ready" => TestValidationStage,
            _ => stage.Trim()
        };

    private static int GetRunnerStageOrder(string stage)
        => NormalizeRunnerStage(stage) switch
        {
            TestValidationStage => 0,
            ReviewStage => 1,
            _ => 2
        };

    private static List<string> BuildActivityIssues(string disposition)
        => disposition switch
        {
            "test-failed" => ["test-failed"],
            "user-test-required" => ["user-test-required"],
            "open-question" => ["user-input-required"],
            "execution-crash" => ["execution-crash"],
            "rate-limited" => ["rate-limited"],
            "transport-error" => ["transport-error"],
            "review-verified" => [],
            "review-done" => [],
            _ => ["missing-evidence"]
        };

    private DateTime? GetRetryNotBefore(string disposition)
    {
        var cooldownSeconds = disposition switch
        {
            "rate-limited" => _config.RateLimitCooldownSeconds,
            "transport-error" => _config.TransportErrorCooldownSeconds,
            "execution-crash" => _config.ExecutionCrashCooldownSeconds,
            _ => 0
        };

        return cooldownSeconds > 0
            ? DateTime.UtcNow.AddSeconds(cooldownSeconds)
            : null;
    }

    private static string? ResolveCopilotFailureType(CopilotResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FailureCategory))
        {
            return result.FailureCategory;
        }

        return result.TimedOut ? "transport-error" : null;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static List<SpecConditionUpdate> ResetConditionsForRequeue(SpecNode spec)
    {
        var updates = new List<SpecConditionUpdate>();
        foreach (var condition in spec.Conditions)
        {
            condition.Status = "draft";
            condition.Metadata ??= new Dictionary<string, object>();
            condition.Metadata.Remove("reviewReason");
            condition.Metadata.Remove("requiresManualVerification");
            condition.Metadata.Remove("manualVerificationReason");
            condition.Metadata.Remove("manualVerificationItems");
            condition.Metadata.Remove("manualVerificationStatus");
            condition.Metadata.Remove("lastVerifiedAt");
            condition.Metadata.Remove("lastVerifiedBy");
            condition.Metadata.Remove("verificationSource");
            updates.Add(new SpecConditionUpdate
            {
                ConditionId = condition.Id,
                Status = "draft",
                Reason = "reset-for-requeue",
                Comment = "자동 재시도를 위해 condition 상태를 초기화했다."
            });
        }

        return updates;
    }

    private static void NormalizeConditionsForUserReview(SpecNode spec, bool hasOpenQuestions)
    {
        foreach (var condition in spec.Conditions)
        {
            if (string.Equals(condition.Status, "verified", StringComparison.OrdinalIgnoreCase))
            {
                condition.Metadata?.Remove("reviewReason");
                continue;
            }

            condition.Metadata ??= new Dictionary<string, object>();
            if (HasConditionManualVerificationRequirement(condition))
            {
                condition.Status = "needs-review";
                condition.Metadata["reviewReason"] = "user-test-required";
                continue;
            }

            if (hasOpenQuestions)
            {
                condition.Status = "needs-review";
                condition.Metadata["reviewReason"] = "open-question";
                continue;
            }

            condition.Status = "draft";
            condition.Metadata.Remove("reviewReason");
        }
    }

    private static bool HasConditionManualVerificationRequirement(SpecCondition condition)
    {
        if (condition.Metadata == null)
        {
            return false;
        }

        return (TryGetMetadataBool(condition.Metadata, "requiresManualVerification") &&
                GetMetadataBooleanValue(condition.Metadata, "requiresManualVerification"))
               || (condition.Metadata.TryGetValue("manualVerificationItems", out var items) && items != null && items.ToString() != "[]");
    }

    private static bool TryGetMetadataBool(Dictionary<string, object> metadata, string key)
        => metadata.ContainsKey(key);

    private static bool GetMetadataBooleanValue(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value == null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static void AppendActivity(
        SpecNode spec,
        string role,
        string summary,
        string outcome,
        string? kind = null,
        string? comment = null,
        string? actor = null,
        string? model = null,
        string? statusFrom = null,
        string? statusTo = null,
        List<string>? issues = null,
        List<SpecConditionUpdate>? conditionUpdates = null,
        List<string>? relatedIds = null)
    {
        spec.Activity ??= new List<SpecActivityEntry>();
        spec.Activity.Add(new SpecActivityEntry
        {
            At = DateTime.UtcNow.ToString("o"),
            Role = role,
            Actor = actor,
            Model = model,
            Summary = summary,
            Comment = comment,
            Outcome = outcome,
            Kind = kind,
            Issues = issues ?? new List<string>(),
            ConditionUpdates = conditionUpdates ?? new List<SpecConditionUpdate>(),
            RelatedIds = relatedIds ?? new List<string>(),
            StatusChange = !string.IsNullOrWhiteSpace(statusFrom) && !string.IsNullOrWhiteSpace(statusTo)
                ? new SpecActivityStatusChange { From = statusFrom!, To = statusTo! }
                : null
        });
    }

    /// <summary>PID 파일 기록</summary>
    private void WritePidFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_pidFilePath)!);
        var instance = new RunnerInstance
        {
            InstanceId = _instanceId,
            ProcessId = Environment.ProcessId,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = "running"
        };
        File.WriteAllText(_pidFilePath,
            JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>PID 파일 삭제</summary>
    private void RemovePidFile()
    {
        try { if (File.Exists(_pidFilePath)) File.Delete(_pidFilePath); } catch { }
    }

    /// <summary>실행 중인 Runner 인스턴스 정보 가져오기</summary>
    public static RunnerInstance? GetRunningInstance(string flowRoot, string pidFile = "runner.pid")
    {
        var pidPath = Path.Combine(flowRoot, pidFile);
        if (!File.Exists(pidPath)) return null;

        try
        {
            var json = File.ReadAllText(pidPath);
            var instance = JsonSerializer.Deserialize<RunnerInstance>(json);
            if (instance == null) return null;

            // 프로세스가 실제로 실행 중인지 확인
            try
            {
                Process.GetProcessById(instance.ProcessId);
                return instance;
            }
            catch
            {
                // 프로세스가 없으면 stale PID 파일
                instance.Status = "crashed";
                return instance;
            }
        }
        catch
        {
            return null;
        }
    }

    private async Task<SpecWorkResult?> ReviewNextSpecAsync(CancellationToken cancellationToken)
    {
        var candidate = FindNextReviewCandidate();
        if (candidate == null)
        {
            return null;
        }

        var result = new SpecWorkResult
        {
            SpecId = candidate.Id,
            Success = false,
            Action = "review",
            StartedAt = DateTime.UtcNow.ToString("o")
        };

        _log.Info("review-loop", $"검토 대기 스펙 분석 시작: {candidate.Id}", candidate.Id);

        var specJson = BuildSpecPromptJson(candidate);
        var reviewContext = BuildReviewContext(candidate);
        var reviewResult = await _copilot.ReviewSpecAsync(candidate, specJson, reviewContext, _projectRoot, _instanceId);

        if (!reviewResult.Success)
        {
            result.ErrorMessage = reviewResult.ErrorMessage ?? "검토 분석 실패";
            _log.Warn("review-loop", $"검토 분석 실패: {result.ErrorMessage}", candidate.Id);
            return FinalizeResult(result);
        }

        var reviewedSpec = _specStore.Get(candidate.Id);
        if (!HasPersistedReviewResult(reviewedSpec))
        {
            result.ErrorMessage = "spec-append-review 결과가 스펙에 반영되지 않았습니다.";
            _log.Warn("review-loop", result.ErrorMessage, candidate.Id);
            return FinalizeResult(result);
        }

        // 검토 완료 후 verified/done 전환된 경우 worktree 정리 (needs-review handoff는 보존)
        if (string.Equals(reviewedSpec!.Status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reviewedSpec.Status, "done", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupWorktreeFromMetadataAsync(reviewedSpec);
        }

        result.Success = true;
        result.TriggeredReschedule = !HasOpenQuestions(reviewedSpec!)
            && !string.Equals(reviewedSpec.Status, "needs-review", StringComparison.OrdinalIgnoreCase);
        return FinalizeResult(result);
    }

    private SpecNode? FindNextReviewCandidate()
        => _specStore.GetAll()
            .Where(spec => string.Equals(spec.Status, "needs-review", StringComparison.OrdinalIgnoreCase)
                && !IsTaskSpec(spec)
                && !HasOpenQuestions(spec)
                && HasPendingReviewWork(spec))
            .OrderBy(spec => ParseIsoDate(spec.UpdatedAt) ?? DateTime.MaxValue)
            .ThenBy(spec => spec.Id)
            .FirstOrDefault();

    private static bool HasPendingReviewWork(SpecNode spec)
    {
        if (spec.Metadata == null)
        {
            return true;
        }

        var lastCompletedAt = GetMetadataIsoDate(spec.Metadata, "lastCompletedAt");
        var lastReviewAt = GetMetadataIsoDate(spec.Metadata, "lastReviewAt");

        if (!lastReviewAt.HasValue)
        {
            return true;
        }

        return !lastCompletedAt.HasValue || lastCompletedAt > lastReviewAt;
    }

    private static bool IsTaskSpec(SpecNode spec)
        => string.Equals(spec.NodeType, "task", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseIsoDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static SpecReviewAnalysis ParseReviewAnalysis(string? output, SpecNode spec)
    {
        if (TryParseReviewAnalysis(output, out var analysis))
        {
            return analysis;
        }

        var lastError = spec.Metadata != null && spec.Metadata.TryGetValue("lastError", out var errorValue)
            ? errorValue?.ToString()
            : null;

        return new SpecReviewAnalysis
        {
            Summary = string.IsNullOrWhiteSpace(lastError)
                ? "자동 검토 결과를 구조화하지 못해 review handoff 상태를 유지합니다."
                : $"자동 검토 파싱 실패. 마지막 오류: {lastError}",
            FailureReasons = string.IsNullOrWhiteSpace(lastError)
                ? ["Copilot 검토 결과를 JSON으로 해석하지 못했습니다."]
                : [$"마지막 오류: {lastError}"],
            Alternatives = ["구현 로그와 변경 파일을 확인한 뒤 재시도합니다."],
            SuggestedAttempts = ["Copilot 검토 프롬프트를 다시 실행합니다.", "원인 로그를 확인한 뒤 spec을 다시 queued로 올리기 전에 review 사유를 정리합니다."],
            RequiresUserInput = false,
            AdditionalInformationRequests = []
        };
    }

    internal static bool TryParseReviewAnalysis(string? output, out SpecReviewAnalysis analysis)
    {
        analysis = new SpecReviewAnalysis();
        var json = ExtractFirstJsonObject(output);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        return TryParseReviewAnalysisJson(json, out analysis, out _);
    }

    internal static bool TryParseReviewAnalysisJson(string? json, out SpecReviewAnalysis analysis, out string? errorMessage)
    {
        analysis = new SpecReviewAnalysis();
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "리뷰 JSON이 비어 있습니다.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "리뷰 JSON 루트는 객체여야 합니다.";
                return false;
            }

            analysis = CreateReviewAnalysis(root);
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"리뷰 JSON 파싱 실패 (line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}): {ex.Message}";
            return false;
        }
    }

    internal static void ApplyReviewAnalysis(SpecNode spec, SpecReviewAnalysis analysis, string reviewerId, DateTime reviewedAtUtc, int maxAttempts = 3)
    {
        const string RetryLimitQuestionId = "retry-limit-reached";
        spec.Metadata ??= new Dictionary<string, object>();
        var previousStatus = spec.Status;

        var reviewedAt = reviewedAtUtc.ToString("o");
        ApplyReviewVerifiedConditions(spec, analysis.VerifiedConditionIds, reviewerId, reviewedAtUtc, "copilot-cli-review");
        var questions = BuildReviewQuestions(spec, analysis, reviewerId, reviewedAt);
        var hasOpenQuestions = questions.Any(q => string.Equals(q.Status, "open", StringComparison.OrdinalIgnoreCase));

        // 사용자가 retry-limit-reached 질문에 답변한 경우 시도 카운터 초기화 (새 예산 부여)
        var hasAnsweredRetryLimit = questions.Any(q =>
            q.Id == RetryLimitQuestionId &&
            !string.Equals(q.Status, "open", StringComparison.OrdinalIgnoreCase));
        if (hasAnsweredRetryLimit)
        {
            spec.Metadata["implementationAttempts"] = 0;
        }

        // 최대 시도 횟수 초과 시 사용자 개입 강제 (자동 requeue 차단)
        if (!hasOpenQuestions && maxAttempts > 0)
        {
            var attempts = GetMetadataInt(spec.Metadata, "implementationAttempts");
            if (attempts >= maxAttempts)
            {
                questions.Add(new SpecReviewQuestion
                {
                    Id = RetryLimitQuestionId,
                    Type = "user-decision",
                    Question = $"자동 구현이 {attempts}회 연속 실패했습니다. 계속 자동 재시도할까요, 아니면 스펙을 수정해야 할까요?",
                    Why = $"자동 재시도 한도({maxAttempts}회)에 도달했습니다. 사용자의 결정이 필요합니다.",
                    Status = "open",
                    RequestedAt = reviewedAt,
                    RequestedBy = reviewerId
                });
                hasOpenQuestions = true;
            }
        }

        // 재배치 시 시도 카운터 초기화
        if (!hasOpenQuestions)
        {
            spec.Metadata["implementationAttempts"] = 0;
        }

        var decision = SpecReviewEvaluator.ResolveDecision(spec, hasOpenQuestions);
        SpecReviewEvaluator.NormalizeConditionReviewStates(spec, hasOpenQuestions);
        decision = SpecReviewEvaluator.ResolveDecision(spec, hasOpenQuestions);
        List<SpecConditionUpdate>? conditionUpdates = null;
        string activityOutcome;
        string activitySummary;

        if (hasOpenQuestions)
        {
            spec.Status = "needs-review";
            spec.Metadata.Remove("lastVerifiedAt");
            spec.Metadata.Remove("lastVerifiedBy");
            spec.Metadata.Remove("verificationSource");
            activityOutcome = "needs-review";
            activitySummary = "리뷰 결과 사용자 질문 응답이 필요해 스펙을 needs-review로 유지했다.";
            conditionUpdates = BuildConditionUpdatesForNeedsReview(spec, "user-input-required", "사용자 질문 응답이 필요하다.");
        }
        else if (decision.IsFinal)
        {
            spec.Status = decision.SpecStatus;
            spec.Metadata["lastVerifiedAt"] = reviewedAt;
            spec.Metadata["lastVerifiedBy"] = reviewerId;
            spec.Metadata["verificationSource"] = "copilot-cli-review";
            spec.Metadata.Remove("worktreePath");
            spec.Metadata.Remove("worktreeBranch");
            activityOutcome = string.Equals(decision.SpecStatus, "done", StringComparison.OrdinalIgnoreCase)
                ? "done"
                : "verified";
            activitySummary = string.Equals(decision.SpecStatus, "done", StringComparison.OrdinalIgnoreCase)
                ? "리뷰 결과 모든 condition이 충족되어 task를 done으로 확정했다."
                : "리뷰 결과 모든 condition이 충족되어 feature를 verified로 확정했다.";
            conditionUpdates = BuildConditionUpdatesForVerified(spec, "automated-tests-passed", "리뷰 증거와 테스트 결과가 condition 충족을 뒷받침한다.");
        }
        else if (string.Equals(decision.SpecStatus, "needs-review", StringComparison.OrdinalIgnoreCase))
        {
            spec.Status = "needs-review";
            spec.Metadata.Remove("lastVerifiedAt");
            spec.Metadata.Remove("lastVerifiedBy");
            spec.Metadata.Remove("verificationSource");
            activityOutcome = "needs-review";
            activitySummary = "리뷰 결과 사용자 수동 테스트가 필요해 스펙을 needs-review로 유지했다.";
            conditionUpdates = BuildConditionUpdatesForNeedsReview(spec, "user-test-required", "사용자 수동 테스트 또는 확인이 필요하다.");
        }
        else
        {
            spec.Status = "queued";
            spec.Metadata.Remove("lastVerifiedAt");
            spec.Metadata.Remove("lastVerifiedBy");
            spec.Metadata.Remove("verificationSource");
            conditionUpdates = ResetConditionsForRequeue(spec);
            activityOutcome = "requeue";
            activitySummary = "리뷰 결과 자동 재시도를 위해 스펙을 queued로 되돌렸다.";
        }

        spec.Metadata["review"] = new Dictionary<string, object>
        {
            ["source"] = "copilot-cli-review",
            ["reviewedAt"] = reviewedAt,
            ["reviewedBy"] = reviewerId,
            ["summary"] = analysis.Summary,
            ["failureReasons"] = analysis.FailureReasons,
            ["alternatives"] = analysis.Alternatives,
            ["suggestedAttempts"] = analysis.SuggestedAttempts,
            ["verifiedConditionIds"] = analysis.VerifiedConditionIds,
            ["additionalInformationRequests"] = analysis.AdditionalInformationRequests
        };
        spec.Metadata["questions"] = questions
            .Select(question => new Dictionary<string, object>
            {
                ["id"] = question.Id,
                ["type"] = question.Type,
                ["question"] = question.Question,
                ["why"] = question.Why,
                ["status"] = question.Status,
                ["answer"] = question.Answer ?? string.Empty,
                ["answeredAt"] = question.AnsweredAt ?? string.Empty,
                ["requestedAt"] = question.RequestedAt ?? reviewedAt,
                ["requestedBy"] = question.RequestedBy ?? reviewerId
            })
            .ToList();
        spec.Metadata["reviewDisposition"] = decision.ReviewDisposition;
        if (!string.IsNullOrWhiteSpace(decision.ReviewReason))
        {
            spec.Metadata["reviewReason"] = decision.ReviewReason;
        }
        else
        {
            spec.Metadata.Remove("reviewReason");
        }
        spec.Metadata["plannerState"] = hasOpenQuestions ? "waiting-user-input" : "standby";
        spec.Metadata["lastReviewAt"] = reviewedAt;
        spec.Metadata["lastReviewBy"] = reviewerId;
        spec.Metadata.Remove("requiresUserInput");

        if (hasOpenQuestions)
        {
            spec.Metadata["questionStatus"] = "waiting-user-input";
        }
        else
        {
            spec.Metadata.Remove("questionStatus");
        }

        AppendActivity(spec,
            role: "tester",
            summary: activitySummary,
            outcome: activityOutcome,
            kind: "verification",
            comment: BuildReviewActivityComment(analysis, questions, GetMetadataString(spec.Metadata, "lastAnsweredAt")),
            actor: reviewerId,
            model: "gpt-5-mini",
            statusFrom: previousStatus,
            statusTo: spec.Status,
            issues: BuildActivityIssues(decision.ReviewDisposition),
            conditionUpdates: conditionUpdates);
    }

    private static List<SpecConditionUpdate> BuildConditionUpdatesForNeedsReview(SpecNode spec, string reason, string comment)
    {
        var updates = new List<SpecConditionUpdate>();
        foreach (var condition in spec.Conditions)
        {
            if (!string.Equals(condition.Status, "needs-review", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            updates.Add(new SpecConditionUpdate
            {
                ConditionId = condition.Id,
                Status = "needs-review",
                Reason = reason,
                Comment = comment
            });
        }

        return updates;
    }

    private static List<SpecConditionUpdate> BuildConditionUpdatesForVerified(SpecNode spec, string reason, string comment)
    {
        var updates = new List<SpecConditionUpdate>();
        foreach (var condition in spec.Conditions)
        {
            if (!string.Equals(condition.Status, "verified", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            updates.Add(new SpecConditionUpdate
            {
                ConditionId = condition.Id,
                Status = "verified",
                Reason = reason,
                Comment = comment
            });
        }

        return updates;
    }

    private static string? BuildReviewActivityComment(
        SpecReviewAnalysis analysis,
        IReadOnlyCollection<SpecReviewQuestion> questions,
        string? lastAnsweredAt)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(analysis.Summary))
        {
            lines.Add(analysis.Summary.Trim());
        }

        foreach (var reason in analysis.FailureReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)))
        {
            lines.Add(reason.Trim());
        }

        var answeredQuestions = questions
            .Where(question =>
                !string.Equals(question.Status, "open", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(question.Answer))
            .Select(question => $"{question.Question.Trim()} => {question.Answer!.Trim()}")
            .ToList();

        if (answeredQuestions.Count > 0)
        {
            lines.Add($"answered: {string.Join(" | ", answeredQuestions)}");
        }

        if (!string.IsNullOrWhiteSpace(lastAnsweredAt))
        {
            lines.Add($"lastAnsweredAt={lastAnsweredAt}");
        }

        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    private static bool HasPersistedReviewResult(SpecNode? spec)
    {
        if (spec == null || spec.Metadata == null)
            return false;

        // needs-review handoff, verified/done final 상태 모두 review 결과 반영으로 간주한다.
        var hasValidStatus = string.Equals(spec.Status, "needs-review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(spec.Status, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(spec.Status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(spec.Status, "done", StringComparison.OrdinalIgnoreCase)
            || HasOpenQuestions(spec);

        if (!hasValidStatus)
            return false;

        return spec.Metadata.ContainsKey("lastReviewAt")
            && spec.Metadata.ContainsKey("lastReviewBy");
    }

    private static SpecReviewAnalysis CreateReviewAnalysis(JsonElement root)
    {
        var analysis = new SpecReviewAnalysis
        {
            Summary = GetString(root, "summary") ?? "재시도 전 검토가 필요합니다.",
            FailureReasons = GetStringArray(root, "failureReasons"),
            Alternatives = GetStringArray(root, "alternatives"),
            SuggestedAttempts = GetStringArray(root, "suggestedAttempts"),
            VerifiedConditionIds = GetStringArray(root, "verifiedConditionIds"),
            RequiresUserInput = GetBoolean(root, "requiresUserInput"),
            AdditionalInformationRequests = GetStringArray(root, "additionalInformationRequests"),
            Questions = GetQuestions(root)
        };

        if (analysis.FailureReasons.Count == 0 && !string.IsNullOrWhiteSpace(analysis.Summary))
        {
            analysis.FailureReasons.Add(analysis.Summary);
        }

        return SanitizeReviewAnalysis(analysis);
    }

    private static SpecReviewAnalysis SanitizeReviewAnalysis(SpecReviewAnalysis analysis)
    {
        analysis.VerifiedConditionIds = analysis.VerifiedConditionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        analysis.AdditionalInformationRequests = analysis.AdditionalInformationRequests
            .Where(request => !string.IsNullOrWhiteSpace(request))
            .Select(request => request.Trim())
            .Where(request => !LooksLikeInternalExecutionArtifactRequest(request))
            .ToList();

        var developerFollowUps = analysis.Questions
            .Where(question => !LooksLikeInternalExecutionArtifactRequest(question.Question, question.Why))
            .Where(question => !IsUserDecisionQuestion(question))
            .Select(FormatDeveloperFollowUp)
            .Where(request => !string.IsNullOrWhiteSpace(request));

        analysis.AdditionalInformationRequests = analysis.AdditionalInformationRequests
            .Concat(developerFollowUps)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        analysis.Questions = analysis.Questions
            .Where(question => !LooksLikeInternalExecutionArtifactRequest(question.Question, question.Why))
            .Where(IsUserDecisionQuestion)
            .ToList();

        if (analysis.RequiresUserInput
            && analysis.AdditionalInformationRequests.Count == 0
            && analysis.Questions.Count == 0)
        {
            analysis.RequiresUserInput = false;
        }

        return analysis;
    }

    private static void ApplyReviewVerifiedConditions(
        SpecNode spec,
        IEnumerable<string>? verifiedConditionIds,
        string reviewerId,
        DateTime reviewedAtUtc,
        string verificationSource)
    {
        if (verifiedConditionIds == null)
        {
            return;
        }

        var normalizedIds = new HashSet<string>(
            verifiedConditionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (normalizedIds.Count == 0)
        {
            return;
        }

        foreach (var condition in spec.Conditions)
        {
            if (!normalizedIds.Contains(condition.Id))
            {
                continue;
            }

            condition.Status = "verified";
            condition.Metadata ??= new Dictionary<string, object>();
            condition.Metadata["lastVerifiedAt"] = reviewedAtUtc.ToString("o");
            condition.Metadata["lastVerifiedBy"] = reviewerId;
            condition.Metadata["verificationSource"] = verificationSource;
            condition.Metadata.Remove("requiresManualVerification");
            condition.Metadata.Remove("manualVerificationReason");
            condition.Metadata.Remove("manualVerificationItems");
            condition.Metadata.Remove("reviewReason");
        }
    }

    private static bool LooksLikeInternalExecutionArtifactRequest(string? text, string? why = null)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(why))
        {
            return false;
        }

        var combined = string.Join(" ", new[] { text, why }.Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        return combined.Contains("stdout", StringComparison.Ordinal)
            || combined.Contains("stderr", StringComparison.Ordinal)
            || combined.Contains("stack trace", StringComparison.Ordinal)
            || combined.Contains("traceback", StringComparison.Ordinal)
            || combined.Contains("runner log", StringComparison.Ordinal)
            || combined.Contains("log file", StringComparison.Ordinal)
            || combined.Contains("full log", StringComparison.Ordinal)
            || combined.Contains("execution log", StringComparison.Ordinal)
            || combined.Contains("changed file", StringComparison.Ordinal)
            || combined.Contains("changed files", StringComparison.Ordinal)
            || combined.Contains("git diff", StringComparison.Ordinal)
            || combined.Contains("diff output", StringComparison.Ordinal)
            || combined.Contains("patch", StringComparison.Ordinal)
            || combined.Contains("commit hash", StringComparison.Ordinal)
            || combined.Contains("전체 로그", StringComparison.Ordinal)
            || combined.Contains("실행 로그", StringComparison.Ordinal)
            || combined.Contains("로그 파일", StringComparison.Ordinal)
            || combined.Contains("에러 로그", StringComparison.Ordinal)
            || combined.Contains("스택 트레이스", StringComparison.Ordinal)
            || combined.Contains("변경 파일", StringComparison.Ordinal)
            || combined.Contains("패치", StringComparison.Ordinal)
            || combined.Contains("커밋 해시", StringComparison.Ordinal);
    }

    private static bool IsUserDecisionQuestion(SpecReviewQuestion question)
        => string.Equals(question.Type?.Trim(), "user-decision", StringComparison.OrdinalIgnoreCase);

    private static string FormatDeveloperFollowUp(SpecReviewQuestion question)
    {
        var prompt = question.Question?.Trim();
        var why = question.Why?.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(why)
            ? prompt
            : $"{prompt} (reason: {why})";
    }

    private static List<SpecReviewQuestion> BuildReviewQuestions(SpecNode spec, SpecReviewAnalysis analysis, string reviewerId, string reviewedAt)
    {
        var merged = ReadExistingQuestions(spec);

        var nextIndex = merged.Count + 1;
        foreach (var question in analysis.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Question))
            {
                continue;
            }

            var candidate = new SpecReviewQuestion
            {
                Id = string.IsNullOrWhiteSpace(question.Id) ? $"{spec.Id}-Q{nextIndex++}" : question.Id,
                Type = string.IsNullOrWhiteSpace(question.Type) ? "clarification" : question.Type,
                Question = question.Question,
                Why = question.Why,
                Status = string.IsNullOrWhiteSpace(question.Status) ? "open" : question.Status,
                Answer = question.Answer,
                AnsweredAt = question.AnsweredAt,
                RequestedAt = question.RequestedAt ?? reviewedAt,
                RequestedBy = question.RequestedBy ?? reviewerId
            };

            UpsertReviewQuestion(merged, candidate);
        }

        return merged;
    }

    private static void UpsertReviewQuestion(List<SpecReviewQuestion> questions, SpecReviewQuestion incoming)
    {
        var index = questions.FindIndex(existing => QuestionMatches(existing, incoming));
        if (index < 0)
        {
            questions.Add(incoming);
            return;
        }

        var existing = questions[index];
        questions[index] = string.Equals(existing.Status, "open", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(incoming.Status, "open", StringComparison.OrdinalIgnoreCase)
            ? existing
            : MergeReviewQuestion(existing, incoming);
    }

    private static SpecReviewQuestion MergeReviewQuestion(SpecReviewQuestion existing, SpecReviewQuestion incoming)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(existing.Id) ? incoming.Id : existing.Id,
            Type = string.IsNullOrWhiteSpace(incoming.Type) ? existing.Type : incoming.Type,
            Question = string.IsNullOrWhiteSpace(existing.Question) ? incoming.Question : existing.Question,
            Why = string.IsNullOrWhiteSpace(incoming.Why) ? existing.Why : incoming.Why,
            Status = string.IsNullOrWhiteSpace(incoming.Status) ? existing.Status : incoming.Status,
            Answer = string.IsNullOrWhiteSpace(existing.Answer) ? incoming.Answer : existing.Answer,
            AnsweredAt = string.IsNullOrWhiteSpace(existing.AnsweredAt) ? incoming.AnsweredAt : existing.AnsweredAt,
            RequestedAt = string.IsNullOrWhiteSpace(existing.RequestedAt) ? incoming.RequestedAt : existing.RequestedAt,
            RequestedBy = string.IsNullOrWhiteSpace(existing.RequestedBy) ? incoming.RequestedBy : existing.RequestedBy
        };

    private static bool QuestionMatches(SpecReviewQuestion left, SpecReviewQuestion right)
    {
        if (!string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id)
            && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NormalizeQuestionText(left.Question) == NormalizeQuestionText(right.Question);
    }

    private static string NormalizeQuestionText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();

    private static List<SpecReviewQuestion> ReadExistingQuestions(SpecNode spec)
    {
        var result = new List<SpecReviewQuestion>();
        if (spec.Metadata == null || !spec.Metadata.TryGetValue("questions", out var rawQuestions) || rawQuestions == null)
        {
            return result;
        }

        if (rawQuestions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var parsed = ParseQuestion(item);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }
        }
        else if (rawQuestions is IEnumerable<object> enumerable)
        {
            foreach (var item in enumerable)
            {
                var parsed = ParseQuestion(item);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }
        }

        return result;
    }

    private static SpecReviewQuestion? ParseQuestion(object rawQuestion)
    {
        if (rawQuestion is JsonElement element)
        {
            return new SpecReviewQuestion
            {
                Id = GetString(element, "id") ?? "",
                Type = GetString(element, "type") ?? "clarification",
                Question = GetString(element, "question") ?? "",
                Why = GetString(element, "why") ?? "",
                Status = GetString(element, "status") ?? "open",
                Answer = GetString(element, "answer"),
                AnsweredAt = GetString(element, "answeredAt"),
                RequestedAt = GetString(element, "requestedAt"),
                RequestedBy = GetString(element, "requestedBy")
            };
        }

        if (rawQuestion is Dictionary<string, object> dictionary)
        {
            return new SpecReviewQuestion
            {
                Id = dictionary.GetValueOrDefault("id")?.ToString() ?? "",
                Type = dictionary.GetValueOrDefault("type")?.ToString() ?? "clarification",
                Question = dictionary.GetValueOrDefault("question")?.ToString() ?? "",
                Why = dictionary.GetValueOrDefault("why")?.ToString() ?? "",
                Status = dictionary.GetValueOrDefault("status")?.ToString() ?? "open",
                Answer = dictionary.GetValueOrDefault("answer")?.ToString(),
                AnsweredAt = dictionary.GetValueOrDefault("answeredAt")?.ToString(),
                RequestedAt = dictionary.GetValueOrDefault("requestedAt")?.ToString(),
                RequestedBy = dictionary.GetValueOrDefault("requestedBy")?.ToString()
            };
        }

        return null;
    }

    private static string? ExtractFirstJsonObject(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var start = output.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (int i = start; i < output.Length; i++)
        {
            var ch = output[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return output[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string? BuildPreviousReviewSection(SpecNode spec)
    {
        if (spec.Metadata == null) return null;
        if (!spec.Metadata.TryGetValue("review", out var reviewObj)) return null;

        var sb = new StringBuilder();

        // review 객체에서 관련 필드 추출
        string? summary = null;
        List<string>? failureReasons = null;
        List<string>? suggestedAttempts = null;

        if (reviewObj is JsonElement reviewEl && reviewEl.ValueKind == JsonValueKind.Object)
        {
            summary = GetString(reviewEl, "summary");
            failureReasons = GetStringArray(reviewEl, "failureReasons");
            suggestedAttempts = GetStringArray(reviewEl, "suggestedAttempts");
        }
        else if (reviewObj is System.Text.Json.Nodes.JsonObject reviewNode)
        {
            summary = reviewNode["summary"]?.GetValue<string>();
            failureReasons = reviewNode["failureReasons"]?.AsArray().Select(n => n?.GetValue<string>()).OfType<string>().ToList();
            suggestedAttempts = reviewNode["suggestedAttempts"]?.AsArray().Select(n => n?.GetValue<string>()).OfType<string>().ToList();
        }

        if (!string.IsNullOrWhiteSpace(summary))
            sb.AppendLine($"검토 요약: {summary}");

        if (failureReasons is { Count: > 0 })
        {
            sb.AppendLine("실패 원인:");
            foreach (var r in failureReasons) sb.AppendLine($"  - {r}");
        }

        if (suggestedAttempts is { Count: > 0 })
        {
            sb.AppendLine("권장 접근 방법:");
            foreach (var a in suggestedAttempts) sb.AppendLine($"  - {a}");
        }

        if (reviewObj is JsonElement reviewElement && reviewElement.ValueKind == JsonValueKind.Object)
        {
            var developerFollowUps = GetStringArray(reviewElement, "additionalInformationRequests");
            if (developerFollowUps.Count > 0)
            {
                sb.AppendLine("개발자 선행 확인 항목:");
                foreach (var item in developerFollowUps) sb.AppendLine($"  - {item}");
            }
        }
        else if (reviewObj is System.Text.Json.Nodes.JsonObject reviewNodeWithRequests)
        {
            var developerFollowUps = reviewNodeWithRequests["additionalInformationRequests"]?.AsArray()
                .Select(node => node?.GetValue<string>())
                .OfType<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

            if (developerFollowUps is { Count: > 0 })
            {
                sb.AppendLine("개발자 선행 확인 항목:");
                foreach (var item in developerFollowUps) sb.AppendLine($"  - {item}");
            }
        }

        // 답변된 질문 포함
        if (spec.Metadata.TryGetValue("questions", out var questionsObj))
        {
            var answered = ExtractAnsweredQuestions(questionsObj);
            if (answered.Count > 0)
            {
                sb.AppendLine("사용자 답변:");
                foreach (var (q, a) in answered)
                {
                    sb.AppendLine($"  Q: {q}");
                    sb.AppendLine($"  A: {a}");
                }
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static List<(string Question, string Answer)> ExtractAnsweredQuestions(object questionsObj)
    {
        var result = new List<(string, string)>();
        try
        {
            if (questionsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var status = GetString(item, "status");
                    var answer = GetString(item, "answer");
                    if (!string.Equals(status, "open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(answer))
                    {
                        var question = GetString(item, "question") ?? "";
                        result.Add((question, answer));
                    }
                }
            }
            else if (questionsObj is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is not System.Text.Json.Nodes.JsonObject obj) continue;
                    var status = obj["status"]?.GetValue<string>();
                    var answer = obj["answer"]?.GetValue<string>();
                    if (!string.Equals(status, "open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(answer))
                    {
                        var question = obj["question"]?.GetValue<string>() ?? "";
                        result.Add((question!, answer!));
                    }
                }
            }
        }
        catch { /* ignore */ }
        return result;
    }

    private static int GetMetadataInt(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var val)) return 0;
        return val switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out var n) ? n : 0,
            _ => int.TryParse(val?.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static DateTime? GetMetadataIsoDate(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            DateTime timestamp => timestamp,
            JsonElement { ValueKind: JsonValueKind.String } element => ParseIsoDate(element.GetString()),
            _ => ParseIsoDate(value.ToString())
        };
    }

    private static string BuildReviewContext(SpecNode spec)
    {
        var evaluation = SpecReviewEvaluator.Evaluate(spec);
        var conditionSummary = spec.Conditions.Count == 0
            ? "조건 없음"
            : string.Join(", ", spec.Conditions.Select(condition => $"{condition.Id}:{condition.Status}"));
        var lastError = spec.Metadata != null && spec.Metadata.TryGetValue("lastError", out var errorValue)
            ? errorValue?.ToString()
            : null;

        return $"상태={spec.Status}; nodeType={spec.NodeType}; conditions={conditionSummary}; codeRefs={evaluation.TotalCodeRefs}; descriptionLength={evaluation.DescriptionLength}; lastError={lastError ?? "none"}";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in property.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value!);
            }
        }

        return result;
    }

    private static List<SpecReviewQuestion> GetQuestions(JsonElement element)
    {
        var result = new List<SpecReviewQuestion>();
        if (!element.TryGetProperty("questions", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in property.EnumerateArray())
        {
            result.Add(new SpecReviewQuestion
            {
                Type = GetString(item, "type") ?? "clarification",
                Question = GetString(item, "question") ?? "",
                Why = GetString(item, "why") ?? "",
                Status = GetString(item, "status") ?? "open"
            });
        }

        return result.Where(question => !string.IsNullOrWhiteSpace(question.Question)).ToList();
    }

    private SpecWorkResult FinalizeResult(SpecWorkResult result)
    {
        result.CompletedAt = DateTime.UtcNow.ToString("o");
        return result;
    }
}
