using System.Diagnostics;
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
    private readonly string _instanceId;
    private readonly string _pidFilePath;
    private DateTime _lastIssuePollAt = DateTime.MinValue;

    private CancellationTokenSource? _cts;

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

        // 0. 이전 크래시 복구
        RecoverFromCrash();

        // 1. 스펙 저장소 동기화 (F-080-C3: git clone/pull로 최신 스펙을 로컬 캐시로 가져옴)
        _log.Info("sync", "스펙 저장소 동기화 시작");
        var synced = await _specRepo!.SyncAsync();
        if (!synced)
        {
            _log.Error("sync", "스펙 저장소 동기화 실패, 사이클 중단");
            return results;
        }

        // 1.5. 검토 대기 스펙 자동 검증
        var autoVerified = await AutoVerifyReviewedSpecsAsync();
        results.AddRange(autoVerified);

        // 1.6. F-025-C3: 손상 스펙 fresh scan → 우선 복구 시도
        var brokenRepaired = await RepairBrokenSpecsAsync();
        results.AddRange(brokenRepaired);

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

        // 4. GitHub 이슈 처리 (F-070-C11: issuePollIntervalMinutes 주기로 실행)
        await ProcessGitHubIssuesIfDueAsync();

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
        if (elapsed.TotalMinutes < _config.IssuePollIntervalMinutes) return;

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
                    await _specRepo.CommitAndPushAsync("[runner] Update specs from GitHub issues");
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
    /// </summary>
    public async Task RunDaemonAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        WritePidFile();
        _log.Info("daemon", $"데몬 시작 (PID: {Environment.ProcessId}, 주기: {_config.PollIntervalMinutes}분, 유휴 재확인: 30초)");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                List<SpecWorkResult> results = [];
                try
                {
                    results = await RunOnceAsync();
                }
                catch (Exception ex)
                {
                    _log.Error("daemon", $"사이클 실행 중 오류: {ex.Message}");
                }

                // 처리된 스펙이 있으면 설정된 주기 대기, 없으면 30초 후 재확인
                var delay = results.Count > 0
                    ? TimeSpan.FromMinutes(_config.PollIntervalMinutes)
                    : TimeSpan.FromSeconds(30);

                if (results.Count == 0)
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
            RemovePidFile();
            _log.Info("daemon", "데몬 종료");
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
                    await _git.RemoveWorktreeAsync(spec.Id);
                    return FinalizeResult(result);
                }
            }
            else if (!mergeSuccess)
            {
                result.Success = false;
                result.ErrorMessage = "머지 실패";
                MarkSpecFailed(spec, result.ErrorMessage);
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
                await _specRepo.CommitAndPushAsync($"[runner] Update {spec.Id} status to {spec.Status}");
            }

            result.Success = true;
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("process", $"스펙 처리 중 예외: {ex.Message}", spec.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            MarkSpecFailed(spec, ex.Message);

            // worktree 정리 시도
            try { await _git.RemoveWorktreeAsync(spec.Id); } catch { }

            return FinalizeResult(result);
        }
    }

    /// <summary>
    /// 구현 대상 스펙을 탐색한다.
    /// target status에 해당하는 스펙을 반환한다.
    /// 손상 스펙 복구는 RepairBrokenSpecsAsync()에서 별도로 최우선 처리한다 (F-025-C3).
    /// </summary>
    private List<SpecNode> FindTargetSpecs()
    {
        var allSpecs = _specStore.GetAll();
        var targets = new List<SpecNode>();

        foreach (var spec in allSpecs)
        {
            // working 상태는 AI가 구현 중이므로 스킵
            if (spec.Status == "working") continue;

            // 타겟 상태에 해당하는 스펙만 처리
            if (_config.TargetStatuses.Contains(spec.Status))
            {
                targets.Add(spec);
            }
        }

        // 의존성 순서로 정렬 (의존성이 적은 것 먼저)
        return targets
            .OrderBy(s => s.Dependencies.Count)
            .ThenBy(s => s.Id)
            .ToList();
    }

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

        spec.Status = "needs-review";
        spec.Metadata ??= new Dictionary<string, object>();
        spec.Metadata["lastCompletedAt"] = DateTime.UtcNow.ToString("o");
        spec.Metadata["lastCompletedBy"] = _instanceId;
        spec.Metadata.Remove("lastError");
        spec.Metadata.Remove("lastErrorAt");

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
                CompletedAt = timestamp
            });
        }

        if (results.Count > 0 && _specRepo != null)
        {
            var summary = results.Count <= 3
                ? string.Join(", ", results.Select(result => result.SpecId))
                : $"{results.Count} specs";
            await _specRepo.CommitAndPushAsync($"[runner] Auto-verify {summary}");
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
        var unresolved = _diagService.GetUnresolved();

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
            await _specRepo.CommitAndPushAsync("[runner] Repair broken spec JSON files");
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
    private void RecoverFromCrash()
    {
        var allSpecs = _specStore.GetAll();
        var staleSpecs = allSpecs.Where(s => s.Status == "working").ToList();

        if (staleSpecs.Count == 0) return;

        _log.Warn("recovery", $"비정상 종료된 작업 {staleSpecs.Count}개 발견, 복구 중...");

        foreach (var spec in staleSpecs)
        {
            var prevInstanceId = spec.Metadata?.ContainsKey("runnerInstanceId") == true
                ? spec.Metadata["runnerInstanceId"]?.ToString()
                : "unknown";

            // 현재 인스턴스가 아닌 이전 인스턴스의 작업만 복구
            if (prevInstanceId != _instanceId)
            {
                MarkSpecFailed(spec, $"Runner 인스턴스 비정상 종료 (이전 인스턴스: {prevInstanceId})");

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

    private SpecWorkResult FinalizeResult(SpecWorkResult result)
    {
        result.CompletedAt = DateTime.UtcNow.ToString("o");
        return result;
    }
}
