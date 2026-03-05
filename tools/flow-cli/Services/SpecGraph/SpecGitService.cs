using System.Diagnostics;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 스펙 저장소 git 작업 서비스.
/// spec-push 명령에서 사용하며, 테스트 가능하도록 분리된다.
/// </summary>
public class SpecGitService
{
    public record PushResult(bool AlreadyUpToDate, string? CommitHash, string? CommitMessage, string? Error);

    /// <summary>
    /// 스펙 변경사항을 git add → commit → push 순서로 원격 저장소에 push한다.
    /// 변경사항이 없으면 AlreadyUpToDate=true를 반환한다.
    /// </summary>
    /// <param name="specsDir">스펙 디렉토리 경로</param>
    /// <param name="message">커밋 메시지 (null이면 자동 생성)</param>
    /// <returns>push 결과</returns>
    public async Task<PushResult> PushAsync(string specsDir, string? message = null)
    {
        var gitRoot = FindGitRoot(specsDir)
            ?? throw new InvalidOperationException($"git 저장소를 찾을 수 없습니다: {specsDir}");

        var commitMsg = message ?? $"feat: spec update [{DateTime.UtcNow:yyyy-MM-ddTHH:mm} UTC]";

        // git add -A
        var addResult = await RunGitAsync("add -A", gitRoot);
        if (!addResult.Success)
            throw new InvalidOperationException($"git add 실패: {addResult.Error}");

        // staged diff 확인 (exit 0 = 변경 없음, exit 1 = 변경 있음)
        var diffResult = await RunGitAsync("diff --cached --quiet", gitRoot);
        bool hasChanges = !diffResult.Success;
        string commitHash = "";

        if (hasChanges)
        {
            var commitResult = await RunGitAsync($"commit -m \"{commitMsg}\"", gitRoot);
            if (!commitResult.Success)
                throw new InvalidOperationException($"git commit 실패: {commitResult.Error}");

            var hashResult = await RunGitAsync("rev-parse --short HEAD", gitRoot);
            commitHash = hashResult.Success ? hashResult.Output : "";
        }

        // git push
        var pushResult = await RunGitAsync("push", gitRoot);
        if (!pushResult.Success)
            throw new InvalidOperationException($"git push 실패: {pushResult.Error}");

        if (!hasChanges)
            return new PushResult(AlreadyUpToDate: true, null, null, null);

        return new PushResult(AlreadyUpToDate: false, commitHash, commitMsg, null);
    }

    /// <summary>
    /// 미push 커밋 수를 반환한다. tracking branch가 없으면 -1을 반환한다.
    /// </summary>
    public async Task<int> GetUnpushedCountAsync(string specsDir)
    {
        var gitRoot = FindGitRoot(specsDir);
        if (gitRoot == null) { return 0; }

        var result = await RunGitAsync("rev-list @{u}..HEAD --count", gitRoot);
        if (!result.Success) { return -1; } // tracking branch 없음

        return int.TryParse(result.Output, out var count) ? count : 0;
    }

    /// <summary>
    /// startPath에서 위로 올라가며 .git 디렉토리가 있는 git 루트를 찾는다.
    /// 찾지 못하면 null을 반환한다.
    /// </summary>
    public static string? FindGitRoot(string startPath)
    {
        var current = startPath;
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    internal async Task<(bool Success, string Output, string Error)> RunGitAsync(
        string arguments, string workingDir)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            return (false, "", $"git 실행 실패: {ex.Message}");
        }
    }
}
