using System.Diagnostics;

namespace FlowCore.Runner;

/// <summary>
/// Git worktree 기반 IWorktreeProvisioner 구현체.
/// flow-cli의 GitWorktreeService 패턴을 flow-core용으로 추출한 것.
/// specId 기반으로 worktree를 생성/재사용/정리한다.
/// </summary>
public sealed class GitWorktreeProvisioner : IWorktreeProvisioner
{
    private readonly string _projectRoot;
    private readonly string _worktreeBaseDir;
    private readonly string _mainBranch;

    public GitWorktreeProvisioner(string projectRoot, string flowHome, string mainBranch = "main")
    {
        _projectRoot = projectRoot;
        _worktreeBaseDir = Path.Combine(flowHome, "worktrees");
        _mainBranch = mainBranch;
        Directory.CreateDirectory(_worktreeBaseDir);
    }

    public async Task<WorktreeProvisionResult> CreateAsync(string specId, CancellationToken ct = default)
    {
        var branchName = $"runner/{specId}";
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);

        await RunGitAsync("worktree prune", ct: ct);

        // 기존 worktree가 있으면 rebase 후 재사용
        if (Directory.Exists(worktreePath))
        {
            var stashResult = await RunGitAsync("stash", worktreePath, ct);
            var hasStash = stashResult.Success && !stashResult.Output.Contains("No local changes");

            var rebaseResult = await RunGitAsync($"rebase {_mainBranch}", worktreePath, ct);
            if (rebaseResult.Success)
            {
                if (hasStash) await RunGitAsync("stash pop", worktreePath, ct);
                return new WorktreeProvisionResult
                {
                    Success = true,
                    WorktreeId = specId,
                    Path = worktreePath,
                    Branch = branchName
                };
            }

            // rebase 실패 → abort 후 삭제, 새로 생성
            await RunGitAsync("rebase --abort", worktreePath, ct);
            if (hasStash) await RunGitAsync("stash pop", worktreePath, ct);
            await RemoveWorktreeDir(worktreePath, branchName, ct);
        }

        // 기존 브랜치 정리
        var branchExists = await RunGitAsync($"branch --list {branchName}", ct: ct);
        if (!string.IsNullOrWhiteSpace(branchExists.Output))
        {
            await RunGitAsync($"branch -D {branchName}", ct: ct);
        }

        // worktree 생성
        var result = await RunGitAsync($"worktree add -b {branchName} \"{worktreePath}\"", ct: ct);
        if (!result.Success)
        {
            // stale branch → prune 후 재시도
            await RunGitAsync("worktree prune", ct: ct);
            await RunGitAsync($"branch -D {branchName}", ct: ct);
            result = await RunGitAsync($"worktree add -b {branchName} \"{worktreePath}\"", ct: ct);
        }

        if (!result.Success)
        {
            return new WorktreeProvisionResult { Success = false };
        }

        return new WorktreeProvisionResult
        {
            Success = true,
            WorktreeId = specId,
            Path = worktreePath,
            Branch = branchName
        };
    }

    public async Task<bool> CommitChangesAsync(string specId, string message, CancellationToken ct = default)
    {
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);
        if (!Directory.Exists(worktreePath))
            return false;

        // 변경사항 확인
        var statusResult = await RunGitAsync("status --porcelain", worktreePath, ct);
        if (!statusResult.Success || string.IsNullOrWhiteSpace(statusResult.Output))
            return false;

        // 모든 변경사항 스테이징
        var addResult = await RunGitAsync("add -A", worktreePath, ct);
        if (!addResult.Success) return false;

        // 커밋
        var commitResult = await RunGitAsync($"commit -m \"{message}\"", worktreePath, ct);
        return commitResult.Success;
    }

    public async Task CleanupAsync(string specId, CancellationToken ct = default)
    {
        var branchName = $"runner/{specId}";
        var worktreePath = Path.Combine(_worktreeBaseDir, specId);

        if (Directory.Exists(worktreePath))
        {
            var result = await RunGitAsync($"worktree remove \"{worktreePath}\" --force", ct: ct);
            if (!result.Success)
            {
                try { Directory.Delete(worktreePath, true); }
                catch { /* best-effort */ }
            }
            await RunGitAsync("worktree prune", ct: ct);
        }

        await RunGitAsync($"branch -D {branchName}", ct: ct);
    }

    private async Task RemoveWorktreeDir(string worktreePath, string branchName, CancellationToken ct)
    {
        if (Directory.Exists(worktreePath))
        {
            var result = await RunGitAsync($"worktree remove \"{worktreePath}\" --force", ct: ct);
            if (!result.Success)
            {
                try { Directory.Delete(worktreePath, true); }
                catch { /* best-effort */ }
            }
            await RunGitAsync("worktree prune", ct: ct);
        }
        await RunGitAsync($"branch -D {branchName}", ct: ct);
    }

    private async Task<GitResult> RunGitAsync(string arguments, string? workDir = null, CancellationToken ct = default)
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
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            return new GitResult(false, "", ex.Message);
        }
    }

    private sealed record GitResult(bool Success, string Output, string Error);
}
