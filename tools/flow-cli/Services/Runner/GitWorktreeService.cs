using System.Diagnostics;

namespace FlowCLI.Services.Runner;

/// <summary>
/// Git worktree 생성/삭제/머지를 관리하는 서비스.
/// runner/{spec-id} 브랜치로 격리된 작업 환경을 제공.
/// </summary>
public class GitWorktreeService
{
    private readonly string _projectRoot;
    private readonly string _worktreeBaseDir;
    private readonly string _mainBranch;
    private readonly RunnerLogService _log;

    public GitWorktreeService(string projectRoot, string flowRoot, string worktreeSubDir, string mainBranch, RunnerLogService log)
    {
        _projectRoot = projectRoot;
        _worktreeBaseDir = Path.Combine(flowRoot, worktreeSubDir);
        _mainBranch = mainBranch;
        _log = log;
        Directory.CreateDirectory(_worktreeBaseDir);
    }

    /// <summary>
    /// 스펙 ID 기반으로 worktree를 생성하거나 기존 worktree를 재사용한다.
    /// 기존 worktree가 있으면 main을 rebase하여 최신 상태로 갱신한다.
    /// rebase 실패(충돌) 시 삭제 후 새로 생성한다.
    /// 브랜치: runner/{specId}, 경로: .flow/worktrees/{specId}
    /// </summary>
    public async Task<(bool Success, string WorktreePath, string BranchName)> CreateWorktreeAsync(string specId)
    {
        var branchName = $"runner/{specId}";
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);

        // 기존 worktree가 있으면 재사용 시도
        if (Directory.Exists(worktreePath))
        {
            _log.Info("worktree-reuse", $"기존 worktree 발견, rebase 시도: {worktreePath}", specId);

            // uncommitted 변경사항 커밋 (rebase 전 필요)
            var stashResult = await RunGitAsync("stash", worktreePath);
            var hasStash = stashResult.Success && !stashResult.Output.Contains("No local changes");

            // main 기준으로 rebase
            var rebaseResult = await RunGitAsync($"rebase {_mainBranch}", worktreePath);
            if (rebaseResult.Success)
            {
                if (hasStash) await RunGitAsync("stash pop", worktreePath);
                _log.Info("worktree-reuse", $"기존 worktree rebase 성공, 재사용: {worktreePath} (branch: {branchName})", specId);
                return (true, worktreePath, branchName);
            }

            // rebase 실패 → abort 후 삭제하고 새로 생성
            _log.Warn("worktree-reuse", $"rebase 실패, 워크트리 재생성: {rebaseResult.Error}", specId);
            await RunGitAsync("rebase --abort", worktreePath);
            if (hasStash) await RunGitAsync("stash pop", worktreePath);
            await RemoveWorktreeAsync(specId);
        }

        // 브랜치가 남아있으면 삭제
        var branchExists = await RunGitAsync($"branch --list {branchName}");
        if (!string.IsNullOrWhiteSpace(branchExists.Output))
        {
            await RunGitAsync($"branch -D {branchName}");
        }

        // worktree 추가 (새 브랜치 생성)
        var result = await RunGitAsync($"worktree add -b {branchName} \"{worktreePath}\"");
        if (!result.Success)
        {
            _log.Error("worktree-create", $"worktree 생성 실패: {result.Error}", specId);
            return (false, worktreePath, branchName);
        }

        _log.Info("worktree-create", $"worktree 생성 완료: {worktreePath} (branch: {branchName})", specId);
        return (true, worktreePath, branchName);
    }

    /// <summary>
    /// 스펙 ID에 해당하는 worktree 절대 경로를 반환한다.
    /// </summary>
    public string GetWorktreePath(string specId) => Path.Combine(_worktreeBaseDir, specId);

    /// <summary>
    /// 스펙 ID에 해당하는 브랜치 이름을 반환한다.
    /// </summary>
    public string GetBranchName(string specId) => $"runner/{specId}";

    /// <summary>
    /// worktree 삭제 및 브랜치 정리
    /// </summary>
    public async Task<bool> RemoveWorktreeAsync(string specId)
    {
        var branchName = $"runner/{specId}";
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);

        // worktree 제거
        if (Directory.Exists(worktreePath))
        {
            var result = await RunGitAsync($"worktree remove \"{worktreePath}\" --force");
            if (!result.Success)
            {
                _log.Warn("worktree-remove", $"git worktree remove 실패, 직접 삭제 시도: {result.Error}", specId);
                try
                {
                    Directory.Delete(worktreePath, true);
                }
                catch (Exception ex)
                {
                    _log.Error("worktree-remove", $"디렉토리 삭제 실패: {ex.Message}", specId);
                    return false;
                }
            }
            // prune stale worktree entries
            await RunGitAsync("worktree prune");
        }

        // 브랜치 삭제
        var branchResult = await RunGitAsync($"branch -D {branchName}");
        if (!branchResult.Success)
        {
            _log.Warn("worktree-remove", $"브랜치 삭제 실패 (이미 없을 수 있음): {branchResult.Error}", specId);
        }

        _log.Info("worktree-remove", $"worktree 정리 완료: {specId}", specId);
        return true;
    }

    /// <summary>
    /// worktree의 변경사항을 커밋한다.
    /// </summary>
    public async Task<bool> CommitChangesAsync(string specId, string message)
    {
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);

        var addResult = await RunGitAsync("add -A", worktreePath);
        if (!addResult.Success)
        {
            _log.Error("git-commit", $"git add 실패: {addResult.Error}", specId);
            return false;
        }

        // 변경사항이 있는지 확인
        var diffResult = await RunGitAsync("diff --cached --quiet", worktreePath);
        if (diffResult.Success)
        {
            _log.Info("git-commit", "변경사항 없음, 커밋 스킵", specId);
            return true;
        }

        var commitResult = await RunGitAsync($"commit -m \"{message}\"", worktreePath);
        if (!commitResult.Success)
        {
            _log.Error("git-commit", $"git commit 실패: {commitResult.Error}", specId);
            return false;
        }

        _log.Info("git-commit", "커밋 완료", specId);
        return true;
    }

    /// <summary>
    /// 특정 파일의 uncommitted 변경사항을 버린다 (git checkout -- path).
    /// 파일이 없거나 변경사항이 없으면 무시한다.
    /// </summary>
    public async Task DiscardLocalChangesAsync(string relativePath)
    {
        await RunGitAsync($"checkout -- \"{relativePath}\"");
    }

    /// <summary>
    /// worktree 브랜치를 메인 브랜치에 머지한다.
    /// </summary>
    public async Task<(bool Success, bool HasConflict)> MergeToMainAsync(string specId, string mainBranch)
    {
        var branchName = $"runner/{specId}";

        // 메인 브랜치로 체크아웃
        var checkoutResult = await RunGitAsync($"checkout {mainBranch}");
        if (!checkoutResult.Success)
        {
            _log.Error("merge", $"메인 브랜치 체크아웃 실패: {checkoutResult.Error}", specId);
            return (false, false);
        }

        // 머지 시도
        var mergeResult = await RunGitAsync($"merge {branchName} --no-edit");
        if (mergeResult.Success)
        {
            _log.Info("merge", $"머지 성공: {branchName} → {mainBranch}", specId);
            return (true, false);
        }

        // 충돌 여부 확인
        var statusResult = await RunGitAsync("status --porcelain");
        bool hasConflict = statusResult.Output?.Contains("UU") == true
                        || statusResult.Output?.Contains("AA") == true;

        if (hasConflict)
        {
            _log.Warn("merge", $"머지 충돌 발생: {branchName} → {mainBranch}", specId);
            return (false, true);
        }

        _log.Error("merge", $"머지 실패 (충돌 아닌 오류): {mergeResult.Error}", specId);
        return (false, false);
    }

    /// <summary>
    /// 머지 충돌 해결 후 커밋
    /// </summary>
    public async Task<bool> CommitMergeResolutionAsync(string specId)
    {
        var addResult = await RunGitAsync("add -A");
        if (!addResult.Success) return false;

        var commitResult = await RunGitAsync("commit --no-edit");
        if (!commitResult.Success)
        {
            _log.Error("merge-resolve", $"머지 커밋 실패: {commitResult.Error}", specId);
            return false;
        }

        _log.Info("merge-resolve", "충돌 해결 커밋 완료", specId);
        return true;
    }

    /// <summary>
    /// 머지 중단
    /// </summary>
    public async Task AbortMergeAsync()
    {
        await RunGitAsync("merge --abort");
    }

    /// <summary>
    /// git pull로 원격 동기화
    /// </summary>
    public async Task<bool> PullAsync(string remoteName, string branch)
    {
        var result = await RunGitAsync($"pull {remoteName} {branch} --rebase=false");
        if (!result.Success)
        {
            _log.Warn("pull", $"git pull 실패: {result.Error}");
            return false;
        }
        _log.Info("pull", $"동기화 완료: {remoteName}/{branch}");
        return true;
    }

    /// <summary>
    /// git push
    /// </summary>
    public async Task<bool> PushAsync(string remoteName, string branch)
    {
        var result = await RunGitAsync($"push {remoteName} {branch}");
        if (!result.Success)
        {
            _log.Warn("push", $"git push 실패: {result.Error}");
            return false;
        }
        return true;
    }

    private async Task<GitResult> RunGitAsync(string arguments, string? workDir = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workDir ?? _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new GitResult
            {
                Success = process.ExitCode == 0,
                Output = stdout.Trim(),
                Error = stderr.Trim(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new GitResult
            {
                Success = false,
                Error = $"git 실행 실패: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    private class GitResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public int ExitCode { get; set; }
    }
}
