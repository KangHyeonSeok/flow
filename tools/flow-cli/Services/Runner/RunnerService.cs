using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

/// <summary>
/// Flow Runner 핵심 서비스. 주기적으로 스펙 그래프를 폴링하고
/// 미구현/오류 스펙을 Copilot CLI로 자동 구현한다.
/// </summary>
public class RunnerService
{
    private readonly string _projectRoot;
    private readonly string _flowRoot;
    private readonly RunnerConfig _config;
    private readonly SpecStore _specStore;
    private readonly SpecRepoService? _specRepo;
    private readonly GitWorktreeService _git;
    private readonly CopilotService _copilot;
    private readonly RunnerLogService _log;
    private readonly GitHubIssueService? _githubIssue;
    private readonly BrokenSpecDiagService _diagService;
    private readonly SemaphoreSlim _specRepoGate = new(1, 1);
    private readonly string _instanceId;
    private readonly string _pidFilePath;
    private DateTime _lastIssuePollAt = DateTime.MinValue;

    private CancellationTokenSource? _cts;
    private DateTime _lastIssueSyncAt = DateTime.MinValue;

    private static readonly JsonSerializerOptions SpecJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RunnerService(string projectRoot, RunnerConfig config, string specCacheDir)
    {
        _projectRoot = projectRoot;
        _flowRoot = Path.Combine(projectRoot, ".flow");
        _config = config;

        _instanceId = $"runner-{Environment.ProcessId}-{DateTime.UtcNow:HHmmss}";
        _pidFilePath = Path.Combine(_flowRoot, _config.PidFile);

        _log = new RunnerLogService(_flowRoot, _config.LogDir, _instanceId);

        // specRepository 필수 검증 (F-080-C5)
        if (string.IsNullOrWhiteSpace(_config.SpecRepository))
        {
            throw new InvalidOperationException(
                "specRepository가 설정되지 않았습니다. " +
                "'.flow/config.json'에 specRepository(git URL)를 설정하세요. " +
                "예: flow config --spec-repo https://github.com/user/flow-spec.git");
        }

        // 스펙 저장소 설정: .flow/spec-cache/ 에서 스펙 로드 (F-080-C3, C4)
        _specRepo = new SpecRepoService(_config.SpecRepository, _config.SpecBranch, specCacheDir, _log);
        _specStore = new SpecStore(_specRepo.SpecsDir, externalRepo: true);
        _log.Info("init", $"스펙 저장소 설정: {_config.SpecRepository} → {specCacheDir}");

        // F-025: 손상 스펙 진단 서비스 초기화
        _diagService = new BrokenSpecDiagService(specCacheDir, _log);
        _specStore.SetDiagService(_diagService);

        _git = new GitWorktreeService(projectRoot, _flowRoot, _config.WorktreeDir, _log);
        _copilot = new CopilotService(_config, _log);

        // GitHub 이슈 연동 (F-070-C11~C15)
        if (_config.GitHubIssuesEnabled)
        {
            try
            {
                _githubIssue = new GitHubIssueService(_config, _specStore, _copilot, _log, _flowRoot);
                _log.Info("init", "GitHub 이슈 연동 활성화");
            }
            catch (Exception ex)
            {
                _log.Warn("init", $"GitHub 이슈 연동 초기화 실패: {ex.Message}");
                _githubIssue = null;
            }
        }
    }

    public string InstanceId => _instanceId;
    public RunnerLogService Log => _log;

    /// <summary>
    /// Runner를 단일 사이클로 실행한다 (한 번만 스캔하고 처리).
    /// </summary>
    public async Task<List<SpecWorkResult>> RunOnceAsync()
    {
        var results = new List<SpecWorkResult>();

        _log.Info("cycle", "=== Runner 사이클 시작 ===");

        // 0. 스펙 저장소 동기화 (크래시 복구보다 먼저 실행 — 최신 스펙 상태 기반 복구)
        _log.Info("sync", "스펙 저장소 동기화 시작");
        var synced = await SyncSpecRepoAsync();
        if (!synced)
        {
            _log.Error("sync", "스펙 저장소 동기화 실패, 사이클 중단");
            return results;
        }

        // 1. 이전 크래시 복구 (sync 후 실행 — 로컬 변경 없이 pull된 최신 상태에서 복구)
        var crashRecovered = RecoverFromCrash();
        if (crashRecovered > 0 && _specRepo != null)
        {
            await CommitSpecRepoAsync("[runner] Recover crashed specs to needs-review");
        }

        // 1.5. 검토 대기 스펙 자동 검증
        var autoVerified = await AutoVerifyReviewedSpecsAsync();
        results.AddRange(autoVerified);

        // 1.6. F-025-C3: 손상 스펙 fresh scan → 우선 복구 시도
        var brokenRepaired = await RepairBrokenSpecsAsync();
        results.AddRange(brokenRepaired);

        // 1.7. F-016: 후보 선택 전 이슈 스냅샷 선반영
        // - GitHub 연동 활성화 + 폴링 주기 도달: 전체 이슈 처리를 먼저 수행해 같은 사이클 후보에 신규 연결 반영
        // - GitHub 연동 활성화 + 폴링 주기 미도달: 이전 스냅샷 시각 복원으로 fallback 판단
        // - GitHub 연동 비활성화: 이전 스냅샷 시각 복원으로 fallback 판단
        await ProcessGitHubIssuesIfDueAsync();

        // 1.8. F-015-C1: queued 스펙 이슈 연결 동기화 (구현 대상 탐색 전 최신 이슈 정보 반영)
        await SyncIssueConnectionsForQueueAsync();

        // 2. 구현 대상 스펙 탐색
        var targets = FindTargetSpecs();
        if (targets.Count == 0)
        {
            _log.Info("scan", "구현 대상 스펙 없음");
            return results;
        }

        _log.Info("scan", $"구현 대상 스펙 {targets.Count}개 발견: {string.Join(", ", targets.Select(s => s.Id))}");

        // 3. 각 스펙 처리 (maxConcurrent 만큼)
        var batch = targets.Take(_config.MaxConcurrentSpecs).ToList();
        foreach (var spec in batch)
        {
            var result = await ProcessSpecAsync(spec);
            results.Add(result);
        }

        _log.Info("cycle", $"=== Runner 사이클 완료: {results.Count(r => r.Success)} 성공 / {results.Count(r => !r.Success)} 실패 ===");

        return results;
    }

    /// <summary>
    /// GitHub 이슈 폴링 주기가 도래하면 이슈를 처리한다.
    /// </summary>
    private async Task ProcessGitHubIssuesIfDueAsync()
    {
        if (_githubIssue == null) return;

        var now = DateTime.UtcNow;
        var elapsed = now - _lastIssuePollAt;
        if (elapsed.TotalMinutes < _config.IssuePollIntervalMinutes)
        {
            // F-016: 폴링 주기 미도달 — 이전 스냅샷의 신뢰 가능 시각 복원으로 fallback 판단
            var snapshotAt = ReadIssueSnapshotTimestamp();
            if (snapshotAt.HasValue)
                _log.Info("github-issues", $"이슈 폴링 주기 미도달 (경과: {elapsed.TotalMinutes:F1}분 / 주기: {_config.IssuePollIntervalMinutes}분), 이전 스냅샷 복원: {snapshotAt.Value:o}");
            return;
        }

        try
        {
            _log.Info("github-issues", "GitHub 이슈 폴링 시작");
            var issueResults = await _githubIssue.ProcessIssuesAsync();
            _lastIssuePollAt = now;

            if (issueResults.Count > 0)
            {
                _log.Info("github-issues",
                    $"GitHub 이슈 처리: {issueResults.Count(r => r.Action == "linked")} 연결, " +
                    $"{issueResults.Count(r => r.Action == "created")} 생성, " +
                    $"{issueResults.Count(r => r.Action == "error")} 오류");

                // 이슈 처리로 스펙이 변경되었으면 push
                if (issueResults.Any(r => r.Success && r.Action is "linked" or "created") && _specRepo != null)
                {
                    await CommitSpecRepoAsync("[runner] Update specs from GitHub issues");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("github-issues", $"GitHub 이슈 폴링 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// Runner를 데몬 모드로 실행한다 (주기적 폴링).
    /// 처리할 스펙이 없으면 30초마다 재확인하고, 스펙이 처리된 경우 설정된 주기(PollIntervalMinutes)만큼 대기한다.
    /// F-031: 상태 전환 직후 기본 poll 대기 없이 즉시 다음 후보를 재평가한다.
    /// </summary>
    public async Task RunDaemonAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? reviewLoopTask = null;

        WritePidFile();
        _log.Info("daemon", $"데몬 시작 (PID: {Environment.ProcessId}, 구현 주기: {_config.PollIntervalMinutes}분, 검토 주기: {_config.ReviewPollIntervalSeconds}초, 유휴 재확인: 30초, 최대 즉시 재스케줄: {_config.MaxReschedulesPerPoll}회)");

        if (_config.ReviewPollIntervalSeconds > 0)
        {
            reviewLoopTask = RunReviewDaemonAsync(_cts.Token);
        }

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

            if (reviewLoopTask != null)
            {
                try
                {
                    await reviewLoopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            RemovePidFile();
            _log.Info("daemon", "데몬 종료");
        }
    }

    private async Task RunReviewDaemonAsync(CancellationToken cancellationToken)
    {
        _log.Info("review-loop", $"검토 루프 시작 (주기: {_config.ReviewPollIntervalSeconds}초, 단건 처리)");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReviewNextSpecAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error("review-loop", $"검토 루프 오류: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.ReviewPollIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.Info("review-loop", "검토 루프 종료");
    }

    /// <summary>
    /// F-031: 상태 전환 후 즉시 재스케줄 경량 사이클.
    /// git sync 없이 최신 in-memory 스펙 그래프로 다음 후보를 재평가한다 (C2).
    /// auto-verify → 후보 탐색 → 처리 순으로 실행한다 (C3: review handoff 메타데이터 보존).
    /// </summary>
    private async Task<List<SpecWorkResult>> RunRescheduleAsync()
    {
        var results = new List<SpecWorkResult>();

        // C3: needs-review 스펙 자동 검증 (verified 전환 시 의존 스펙 unblock 가능)
        var autoVerified = await AutoVerifyReviewedSpecsAsync();
        results.AddRange(autoVerified);

        // C2: 최신 스펙 그래프 기반 후보 탐색 (stale 캐시/이전 선택 결과 재사용 금지)
        var targets = FindTargetSpecs();
        if (targets.Count == 0)
        {
            // C4: 후보 없음 사유를 선택 메타데이터에 기록
            LogNoRescheduleCandidates();
            return results;
        }

        _log.Info("reschedule", $"즉시 재스케줄 후보 {targets.Count}개 발견 — 처리 시작");

        var batch = targets.Take(_config.MaxConcurrentSpecs).ToList();
        foreach (var spec in batch)
        {
            if (_cts?.Token.IsCancellationRequested == true) break;
            var result = await ProcessSpecAsync(spec);
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
            // requiresUserInput=true 스펙은 Fix 1 이후 "needs-review" 상태 → 별도 카운팅
            var needsInput = allSpecs.Count(s =>
                string.Equals(s.Status, "needs-review", StringComparison.OrdinalIgnoreCase) && IsRequiresUserInput(s));
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
                MarkSpecFailed(spec, result.ErrorMessage);
                result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환
                return FinalizeResult(result);
            }

            // 3. Copilot으로 구현 시도
            var specJson = JsonSerializer.Serialize(spec, SpecJsonOpts);
            var copilotResult = await _copilot.ImplementSpecAsync(spec.Id, specJson, worktreePath);

            if (!copilotResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = copilotResult.ErrorMessage ?? "Copilot 구현 실패";
                MarkSpecFailed(spec, result.ErrorMessage);
                result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환
                await _git.RemoveWorktreeAsync(spec.Id);
                return FinalizeResult(result);
            }

            // 4. 변경사항 커밋
            var committed = await _git.CommitChangesAsync(spec.Id, $"[runner] Implement {spec.Id}: {spec.Title}");
            if (!committed)
            {
                result.Success = false;
                result.ErrorMessage = "변경사항 커밋 실패";
                MarkSpecFailed(spec, result.ErrorMessage);
                result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환
                await _git.RemoveWorktreeAsync(spec.Id);
                return FinalizeResult(result);
            }

            // 5. 메인 브랜치로 머지
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
                    MarkSpecFailed(spec, result.ErrorMessage);
                    result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환
                    await _git.RemoveWorktreeAsync(spec.Id);
                    return FinalizeResult(result);
                }
            }
            else if (!mergeSuccess)
            {
                result.Success = false;
                result.ErrorMessage = "머지 실패";
                MarkSpecFailed(spec, result.ErrorMessage);
                result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환
                await _git.RemoveWorktreeAsync(spec.Id);
                return FinalizeResult(result);
            }

            // 6. 성공 → worktree 정리 및 스펙 상태 업데이트
            await _git.RemoveWorktreeAsync(spec.Id);
            MarkSpecCompleted(spec);

            // 7. 프로젝트 변경사항 push
            await _git.PushAsync(_config.RemoteName, _config.MainBranch);

            // 8. 스펙 저장소 변경사항 push (별도 저장소인 경우)
            if (_specRepo != null)
            {
                await CommitSpecRepoAsync($"[runner] Update {spec.Id} status to {spec.Status}");
            }

            result.Success = true;
            result.TriggeredReschedule = true; // F-031-C1: working → needs-review/verified 전환
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("process", $"스펙 처리 중 예외: {ex.Message}", spec.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            MarkSpecFailed(spec, ex.Message);
            result.TriggeredReschedule = true; // F-031-C1: working → needs-review 전환

            // worktree 정리 시도
            try { await _git.RemoveWorktreeAsync(spec.Id); } catch { }

            return FinalizeResult(result);
        }
    }

    /// <summary>
    /// 구현 대상 스펙을 탐색하고, readiness 그룹별로 이슈 연관도 점수 기반 재정렬을 수행한다 (F-015).
    /// 손상 스펙 복구는 RepairBrokenSpecsAsync()에서 별도로 최우선 처리한다 (F-025-C3).
    /// </summary>
    private List<SpecNode> FindTargetSpecs()
    {
        var allSpecs = _specStore.GetAll();
        var allSpecIds = allSpecs.Select(s => s.Id).ToHashSet();

        // 의존성이 충족된 상태(verified/done)에 있는 스펙 ID 집합
        var completedIds = allSpecs
            .Where(s => s.Status is "verified" or "done")
            .Select(s => s.Id)
            .ToHashSet();

        var candidates = allSpecs
            .Where(s => s.Status != "working" && _config.TargetStatuses.Contains(s.Status))
            .ToList();

        // F-015-C3: readiness 그룹 분리
        var ready = new List<SpecNode>();
        var notReady = new List<SpecNode>();

        foreach (var spec in candidates)
        {
            // C3: requiresUserInput=true 스펙은 선행 처리 대상에서 제외
            if (IsRequiresUserInput(spec))
            {
                notReady.Add(spec);
                continue;
            }

            // C3: 의존성 미충족 스펙은 선행 처리 대상에서 제외
            // (의존 대상이 그래프에 없는 경우는 satisfied로 간주)
            var depsReady = spec.Dependencies
                .All(dep => !allSpecIds.Contains(dep) || completedIds.Contains(dep));

            if (depsReady)
                ready.Add(spec);
            else
                notReady.Add(spec);
        }

        // C3: ready 그룹 — issue priority score 내림차순, 동점 시 기존 fallback(의존성 수, 스펙 ID)
        var sortedReady = ready
            .OrderByDescending(s => GetIssuePriorityScore(s))
            .ThenBy(s => s.Dependencies.Count)
            .ThenBy(s => s.Id)
            .ToList();

        // C4: not-ready 그룹(의존성 미충족 또는 사용자 입력 필요)은 처리 대상에서 제외.
        // 포함 시 처리 불가 스펙이 자동 선택되어 잘못된 구현이 시작될 수 있음.
        if (notReady.Count > 0)
            _log.Info("queue-priority", $"처리 불가 스펙 {notReady.Count}개 제외 (의존성 미충족 또는 사용자 입력 필요)");

        var result = sortedReady;

        // C6: 큐 정렬 결과 로깅 및 선택된 스펙 selectionReason 기록
        LogQueueSelection(result);

        return result;
    }

    // ── F-015: 이슈 기반 큐 재정렬 헬퍼 ───────────────────────

    /// <summary>
    /// F-015-C1: queued 스펙에 대한 이슈 연결 상태를 동기화한다.
    /// F-016: GitHub 연동 비활성화 시 이전 스냅샷 시각을 복원해 fallback 판단 근거를 제공한다.
    /// </summary>
    private async Task SyncIssueConnectionsForQueueAsync()
    {
        if (_githubIssue == null)
        {
            // F-016: GitHub 연동 비활성 — 이전 스냅샷의 신뢰 가능 시각 복원으로 fallback 판단
            var snapshotAt = ReadIssueSnapshotTimestamp();
            if (snapshotAt.HasValue)
                _log.Info("queue-priority", $"GitHub 연동 비활성, 이전 이슈 스냅샷 복원: {snapshotAt.Value:o} (fallback)");
            return;
        }

        var queuedSpecs = _specStore.GetAll()
            .Where(s => _config.TargetStatuses.Contains(s.Status))
            .ToList();

        if (queuedSpecs.Count == 0) return;

        try
        {
            await _githubIssue.SyncQueuedSpecIssueConnectionsAsync(queuedSpecs);
            _lastIssueSyncAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log.Warn("queue-priority", $"이슈 연결 동기화 실패 (큐 정렬은 기존 fallback으로 진행): {ex.Message}");
        }
    }

    /// <summary>
    /// F-016: 이슈 체크 상태 파일에서 마지막 스냅샷 시각을 읽는다.
    /// GitHub 연동 비활성화 또는 폴링 주기 미도달 시 fallback 판단에 사용.
    /// </summary>
    private DateTime? ReadIssueSnapshotTimestamp()
    {
        var stateFilePath = Path.Combine(_flowRoot, "issue-check-state.json");
        try
        {
            if (!File.Exists(stateFilePath)) return null;
            var json = File.ReadAllText(stateFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lastCheckedAt", out var prop) &&
                DateTime.TryParse(prop.GetString(), out var dt))
                return dt;
        }
        catch { }
        return null;
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
    /// F-015-C3: metadata.requiresUserInput이 true인지 확인한다.
    /// </summary>
    private static bool IsRequiresUserInput(SpecNode spec)
    {
        if (spec.Metadata == null) return false;
        if (!spec.Metadata.TryGetValue("requiresUserInput", out var val)) return false;
        if (val is System.Text.Json.JsonElement je)
            return je.ValueKind == System.Text.Json.JsonValueKind.True;
        if (val is bool b) return b;
        return bool.TryParse(val?.ToString(), out var bp) && bp;
    }

    /// <summary>
    /// F-015-C6: 큐 정렬 결과를 로그에 기록하고, 선택된 스펙의 metadata.selectionReason을 갱신한다.
    /// </summary>
    private void LogQueueSelection(List<SpecNode> rankedSpecs)
    {
        if (rankedSpecs.Count == 0) return;

        var topN = rankedSpecs.Take(Math.Min(5, rankedSpecs.Count)).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($"큐 우선순위 재정렬 결과 (상위 {topN.Count}/{rankedSpecs.Count}개):");

        for (int i = 0; i < topN.Count; i++)
        {
            var spec = topN[i];
            var score = GetIssuePriorityScore(spec);
            var isFallback = score == 0.0;
            var label = isFallback ? "fallback(no-issue-signal)" : $"score={score:F1}";
            sb.Append($" [{i + 1}]{spec.Id}({label})");
        }

        _log.Info("queue-priority", sb.ToString());

        // C6: 선택 예정 스펙(1위)에만 selectionReason 기록
        var selected = rankedSpecs[0];
        var selScore = GetIssuePriorityScore(selected);
        selected.Metadata ??= new Dictionary<string, object>();
        selected.Metadata["selectionReason"] = new Dictionary<string, object>
        {
            ["selectedAt"] = DateTime.UtcNow.ToString("o"),
            ["issuePriorityScore"] = selScore,
            ["isFallback"] = selScore == 0.0,
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
        spec.Metadata["runnerStartedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["implementationPlan"] = new
        {
            triggeredFrom = prevStatus,
            approach = $"AI Runner가 '{spec.Id}: {spec.Title}' 구현을 시작합니다.",
            startedAt = DateTime.UtcNow.ToString("o"),
        };
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → working", spec.Id);
    }

    /// <summary>
    /// 스펙을 실패로 마킹
    /// </summary>
    private void MarkSpecFailed(SpecNode spec, string error)
    {
        spec.Status = "needs-review";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastError"] = error;
        spec.Metadata["lastErrorAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["runnerInstanceId"] = _instanceId;
        _specStore.Update(spec);
        _log.Error("status", $"스펙 상태 변경: needs-review (오류: {error})", spec.Id);
    }

    /// <summary>
    /// 스펙을 구현 완료로 마킹한다.
    /// </summary>
    private void MarkSpecCompleted(SpecNode spec)
    {
        var prevStatus = spec.Status;
        var review = SpecReviewEvaluator.Evaluate(spec);

        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastCompletedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastCompletedBy"] = _instanceId;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");

        if (IsTaskSpec(spec))
        {
            MarkSpecDone(spec, prevStatus, "구현 완료, task 종료");
            return;
        }

        spec.Status = "needs-review";

        if (review.CanAutoVerify)
        {
            spec.Status = "verified";
            spec.Metadata["lastVerifiedAt"] = DateTime.UtcNow.ToString("o");
            spec.Metadata["lastVerifiedBy"] = _instanceId;
            spec.Metadata["verificationSource"] = "runner-completion";
            _specStore.Update(spec);
            _log.Info("status", $"스펙 상태 변경: {prevStatus} → verified (모든 컨디션 충족, 자동 검증 완료)", spec.Id);
            return;
        }

        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("verificationSource");
        _specStore.Update(spec);

        if (review.RequiresManualVerification)
        {
            _log.Info("status", $"스펙 상태 변경: {prevStatus} → needs-review (구현 완료, 수동 검증 필요)", spec.Id);
            return;
        }

        _log.Info("status", $"스펙 상태 변경: {prevStatus} → needs-review (구현 완료, 검토 대기)", spec.Id);
    }

    private void MarkSpecVerified(SpecNode spec, string verificationSource)
    {
        var prevStatus = spec.Status;
        spec.Status = "verified";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastVerifiedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastVerifiedBy"] = _instanceId;
        spec.Metadata["verificationSource"] = verificationSource;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → verified (모든 컨디션 충족, 자동 검증 완료)", spec.Id);
    }

    private void MarkSpecDone(SpecNode spec, string prevStatus, string reason)
    {
        spec.Status = "done";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastDoneAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastDoneBy"] = _instanceId;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");
        spec.Metadata.Remove("lastVerifiedAt");
        spec.Metadata.Remove("lastVerifiedBy");
        spec.Metadata.Remove("verificationSource");
        spec.Metadata.Remove("questionStatus");
        spec.Metadata.Remove("reviewDisposition");
        spec.Metadata.Remove("requiresUserInput");
        _specStore.Update(spec);
        _log.Info("status", $"스펙 상태 변경: {prevStatus} → done ({reason})", spec.Id);
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
            if (IsTaskSpec(spec))
            {
                var completedAt = DateTime.UtcNow.ToString("o");
                MarkSpecDone(spec, "needs-review", "task 타입 최종 상태 정리");

                results.Add(new SpecWorkResult
                {
                    SpecId = spec.Id,
                    Success = true,
                    Action = "auto-complete-task",
                    StartedAt = completedAt,
                    CompletedAt = completedAt,
                    TriggeredReschedule = true
                });
                continue;
            }

            var evaluation = SpecReviewEvaluator.Evaluate(spec);
            if (!evaluation.CanAutoVerify)
            {
                continue;
            }

            var timestamp = DateTime.UtcNow.ToString("o");
            MarkSpecVerified(spec, "runner-review-pass");

            results.Add(new SpecWorkResult
            {
                SpecId = spec.Id,
                Success = true,
                Action = "auto-verify",
                StartedAt = timestamp,
                CompletedAt = timestamp,
                TriggeredReschedule = true // F-031-C1: needs-review → verified 전환
            });
        }

        if (results.Count > 0 && _specRepo != null)
        {
            var summary = results.Count <= 3
                ? string.Join(", ", results.Select(result => result.SpecId))
                : $"{results.Count} specs";
            await CommitSpecRepoAsync($"[runner] Auto-verify {summary}");
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

        // 복구로 인한 스펙 저장소 변경 push
        if (results.Any(r => r.Success && r.Action == "repair") && _specRepo != null)
        {
            await CommitSpecRepoAsync("[runner] Repair broken spec JSON files");
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

            // 현재 인스턴스가 아닌 이전 인스턴스의 작업만 복구
            if (prevInstanceId != _instanceId)
            {
                MarkSpecFailed(spec, $"Runner 인스턴스 비정상 종료 (이전 인스턴스: {prevInstanceId})");
                recovered++;

                // 잔여 worktree 정리 시도
                try
                {
                    _ = _git.RemoveWorktreeAsync(spec.Id).GetAwaiter().GetResult();
                }
                catch
                {
                    _log.Warn("recovery", $"잔여 worktree 정리 실패: {spec.Id}", spec.Id);
                }
            }
        }

        return recovered;
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

    private async Task<bool> SyncSpecRepoAsync(CancellationToken cancellationToken = default)
    {
        if (_specRepo == null)
        {
            return true;
        }

        await _specRepoGate.WaitAsync(cancellationToken);
        try
        {
            return await _specRepo.SyncAsync();
        }
        finally
        {
            _specRepoGate.Release();
        }
    }

    private async Task CommitSpecRepoAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_specRepo == null)
        {
            return;
        }

        await _specRepoGate.WaitAsync(cancellationToken);
        try
        {
            await _specRepo.CommitAndPushAsync(message);
        }
        finally
        {
            _specRepoGate.Release();
        }
    }

    private async Task<SpecWorkResult?> ReviewNextSpecAsync(CancellationToken cancellationToken)
    {
        if (_specRepo == null)
        {
            return null;
        }

        var synced = await SyncSpecRepoAsync(cancellationToken);
        if (!synced)
        {
            _log.Warn("review-loop", "검토 루프용 스펙 저장소 동기화 실패");
            return null;
        }

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

        var specJson = JsonSerializer.Serialize(candidate, SpecJsonOpts);
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

        var commitMsg = IsRequiresUserInput(reviewedSpec!)
            ? $"[runner] Review {candidate.Id}: waiting for user input"
            : $"[runner] Review {candidate.Id} and requeue";
        await CommitSpecRepoAsync(commitMsg, cancellationToken);

        result.Success = true;
        result.TriggeredReschedule = !IsRequiresUserInput(reviewedSpec!);
        return FinalizeResult(result);
    }

    private SpecNode? FindNextReviewCandidate()
        => _specStore.GetAll()
            .Where(spec => string.Equals(spec.Status, "needs-review", StringComparison.OrdinalIgnoreCase)
                && !IsTaskSpec(spec)
                && !IsRequiresUserInput(spec)) // 사용자 입력 대기 중인 스펙은 이미 검토 완료 — 재검토 불필요
            .OrderBy(spec => ParseIsoDate(spec.UpdatedAt) ?? DateTime.MaxValue)
            .ThenBy(spec => spec.Id)
            .FirstOrDefault();

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
                ? "자동 검토 결과를 구조화하지 못해 재시도 대기열로 되돌립니다."
                : $"자동 검토 파싱 실패. 마지막 오류: {lastError}",
            FailureReasons = string.IsNullOrWhiteSpace(lastError)
                ? ["Copilot 검토 결과를 JSON으로 해석하지 못했습니다."]
                : [$"마지막 오류: {lastError}"],
            Alternatives = ["구현 로그와 변경 파일을 확인한 뒤 재시도합니다."],
            SuggestedAttempts = ["Copilot 검토 프롬프트를 다시 실행합니다.", "원인 로그를 확인한 뒤 queued 상태에서 재작업합니다."],
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

    internal static void ApplyReviewAnalysis(SpecNode spec, SpecReviewAnalysis analysis, string reviewerId, DateTime reviewedAtUtc)
    {
        spec.Metadata ??= new Dictionary<string, object>();

        var reviewedAt = reviewedAtUtc.ToString("o");
        var questions = BuildReviewQuestions(spec, analysis, reviewerId, reviewedAt);
        var requiresUserInput = analysis.RequiresUserInput || questions.Any(q => string.Equals(q.Status, "open", StringComparison.OrdinalIgnoreCase));

        // requiresUserInput=true → "needs-review" 상태 유지 (사용자 입력 대기, 자동 재처리 금지)
        // requiresUserInput=false → "queued"로 재배치 (자동 재시도 가능)
        spec.Status = requiresUserInput ? "needs-review" : "queued";

        spec.Metadata["review"] = new Dictionary<string, object>
        {
            ["source"] = "copilot-cli-review",
            ["reviewedAt"] = reviewedAt,
            ["reviewedBy"] = reviewerId,
            ["summary"] = analysis.Summary,
            ["failureReasons"] = analysis.FailureReasons,
            ["alternatives"] = analysis.Alternatives,
            ["suggestedAttempts"] = analysis.SuggestedAttempts,
            ["requiresUserInput"] = requiresUserInput,
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
                ["requestedAt"] = question.RequestedAt ?? reviewedAt,
                ["requestedBy"] = question.RequestedBy ?? reviewerId
            })
            .ToList();
        spec.Metadata["reviewDisposition"] = requiresUserInput ? "needs-user-decision" : "retry-queued";
        spec.Metadata["requiresUserInput"] = requiresUserInput;
        spec.Metadata["plannerState"] = requiresUserInput ? "waiting-user-input" : "standby";
        spec.Metadata["lastReviewAt"] = reviewedAt;
        spec.Metadata["lastReviewBy"] = reviewerId;

        if (requiresUserInput)
        {
            spec.Metadata["questionStatus"] = "waiting-user-input";
        }
        else
        {
            spec.Metadata.Remove("questionStatus");
        }
    }

    private static bool HasPersistedReviewResult(SpecNode? spec)
    {
        if (spec == null || spec.Metadata == null)
            return false;

        // "queued" (자동 재시도) 또는 "needs-review" + requiresUserInput=true (사용자 입력 대기) 모두 유효한 반영 결과
        var hasValidStatus = string.Equals(spec.Status, "queued", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(spec.Status, "needs-review", StringComparison.OrdinalIgnoreCase) && IsRequiresUserInput(spec));

        if (!hasValidStatus)
            return false;

        return spec.Metadata.ContainsKey("review")
            && spec.Metadata.ContainsKey("lastReviewAt")
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
        analysis.AdditionalInformationRequests = analysis.AdditionalInformationRequests
            .Where(request => !LooksLikeInternalExecutionArtifactRequest(request))
            .ToList();

        analysis.Questions = analysis.Questions
            .Where(question => !LooksLikeInternalExecutionArtifactRequest(question.Question, question.Why))
            .ToList();

        if (analysis.RequiresUserInput
            && analysis.AdditionalInformationRequests.Count == 0
            && analysis.Questions.Count == 0)
        {
            analysis.RequiresUserInput = false;
        }

        return analysis;
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

    private static List<SpecReviewQuestion> BuildReviewQuestions(SpecNode spec, SpecReviewAnalysis analysis, string reviewerId, string reviewedAt)
    {
        var merged = ReadExistingQuestions(spec)
            .Where(question => !string.Equals(question.Status, "open", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nextIndex = merged.Count + 1;
        foreach (var question in analysis.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Question))
            {
                continue;
            }

            merged.Add(new SpecReviewQuestion
            {
                Id = string.IsNullOrWhiteSpace(question.Id) ? $"{spec.Id}-Q{nextIndex++}" : question.Id,
                Type = string.IsNullOrWhiteSpace(question.Type) ? "clarification" : question.Type,
                Question = question.Question,
                Why = question.Why,
                Status = string.IsNullOrWhiteSpace(question.Status) ? "open" : question.Status,
                RequestedAt = question.RequestedAt ?? reviewedAt,
                RequestedBy = question.RequestedBy ?? reviewerId
            });
        }

        foreach (var request in analysis.AdditionalInformationRequests)
        {
            if (string.IsNullOrWhiteSpace(request))
            {
                continue;
            }

            merged.Add(new SpecReviewQuestion
            {
                Id = $"{spec.Id}-Q{nextIndex++}",
                Type = "missing-info",
                Question = request,
                Why = "추가 정보 없이는 다음 구현 시도를 확정하기 어렵습니다.",
                Status = "open",
                RequestedAt = reviewedAt,
                RequestedBy = reviewerId
            });
        }

        return merged;
    }

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
