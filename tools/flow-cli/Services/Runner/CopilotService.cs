using System.Diagnostics;
using System.Text;

namespace FlowCLI.Services.Runner;

/// <summary>
/// GitHub Copilot CLI를 호출하여 스펙 기반 코드 구현/수정을 수행하는 서비스.
/// copilot -p "프롬프트" 형식으로 호출.
/// </summary>
public class CopilotService
{
    private readonly RunnerConfig _config;
    private readonly RunnerLogService _log;

    public CopilotService(RunnerConfig config, RunnerLogService log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Copilot CLI를 호출하여 스펙을 구현한다.
    /// worktreePath에서 실행하여 해당 작업 디렉토리에서 코드를 생성/수정.
    /// </summary>
    public async Task<CopilotResult> ImplementSpecAsync(string specId, string specJson, string worktreePath)
    {
        var prompt = BuildImplementPrompt(specId, specJson);
        return await RunCopilotAsync(prompt, worktreePath, specId, "implement");
    }

    /// <summary>
    /// Copilot CLI를 호출하여 머지 충돌을 해결한다.
    /// </summary>
    public async Task<CopilotResult> ResolveMergeConflictAsync(string specId, string worktreePath)
    {
        var prompt = BuildMergeResolvePrompt(specId);
        return await RunCopilotAsync(prompt, worktreePath, specId, "merge-resolve");
    }

    /// <summary>
    /// Copilot CLI를 호출하여 스펙의 오류를 수정한다.
    /// </summary>
    public async Task<CopilotResult> FixSpecErrorAsync(string specId, string specJson, string errorInfo, string worktreePath)
    {
        var prompt = BuildErrorFixPrompt(specId, specJson, errorInfo);
        return await RunCopilotAsync(prompt, worktreePath, specId, "error-fix");
    }

    private async Task<CopilotResult> RunCopilotAsync(string prompt, string workDir, string specId, string action)
    {
        _log.Info($"copilot-{action}", $"Copilot 호출 시작 (model: {_config.CopilotModel})", specId);

        try
        {
            var escapedPrompt = prompt.Replace("\"", "\\\"");
            // --yolo: 모든 권한 허용 (비대화형 모드에서 권한 확인 없이 파일 편집/도구 실행)
            // --autopilot: 멀티턴 자동 계속 실행
            var copilotArgs = $"-p \"{escapedPrompt}\" --model {_config.CopilotModel} --yolo --autopilot";

            string fileName;
            string arguments;
            var cliPath = _config.CopilotCliPath;
            if (!string.IsNullOrEmpty(cliPath) && cliPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "pwsh";
                arguments = $"-NonInteractive -File \"{cliPath}\" {copilotArgs}";
            }
            else if (!string.IsNullOrEmpty(cliPath) && cliPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "cmd.exe";
                arguments = $"/c \"{cliPath}\" {copilotArgs}";
            }
            else if (!string.IsNullOrEmpty(cliPath))
            {
                fileName = cliPath;
                arguments = copilotArgs;
            }
            else
            {
                // CopilotCommand 자동 해석: Windows에서 .ps1/.bat 가능성 고려
                var resolved = ResolveCommand(_config.CopilotCommand);
                if (resolved != null && resolved.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "pwsh";
                    arguments = $"-NonInteractive -File \"{resolved}\" {copilotArgs}";
                }
                else if (resolved != null && resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "cmd.exe";
                    arguments = $"/c \"{resolved}\" {copilotArgs}";
                }
                else
                {
                    fileName = _config.CopilotCommand;
                    arguments = copilotArgs;
                }
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeoutMs = _config.CopilotTimeoutMinutes * 60 * 1000;
            var exited = await WaitForExitAsync(process, timeoutMs);

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _log.Error($"copilot-{action}", $"Copilot 타임아웃 ({_config.CopilotTimeoutMinutes}분)", specId);
                return new CopilotResult
                {
                    Success = false,
                    TimedOut = true,
                    ErrorMessage = $"Copilot 호출 타임아웃 ({_config.CopilotTimeoutMinutes}분)"
                };
            }

            var stdoutStr = stdout.ToString().Trim();
            var stderrStr = stderr.ToString().Trim();

            if (process.ExitCode == 0)
            {
                _log.Info($"copilot-{action}", "Copilot 호출 성공", specId);
                return new CopilotResult
                {
                    Success = true,
                    Output = stdoutStr,
                    ExitCode = 0
                };
            }
            else
            {
                _log.Error($"copilot-{action}", $"Copilot 실패 (exit: {process.ExitCode}): {stderrStr}", specId);
                return new CopilotResult
                {
                    Success = false,
                    Output = stdoutStr,
                    ErrorMessage = stderrStr,
                    ExitCode = process.ExitCode
                };
            }
        }
        catch (Exception ex)
        {
            _log.Error($"copilot-{action}", $"Copilot 실행 예외: {ex.Message}", specId);
            return new CopilotResult
            {
                Success = false,
                ErrorMessage = $"Copilot 실행 실패: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    private static string BuildImplementPrompt(string specId, string specJson)
    {
        return $"""
            다음 스펙을 구현하세요. 스펙의 description과 conditions(수락 조건)를 모두 만족하도록 코드를 작성하세요.
            기존 프로젝트 구조와 코딩 패턴을 따르세요.
            구현 후 빌드가 통과하는지 확인하세요.

            스펙 ID: {specId}
            스펙 내용:
            {specJson}
            """;
    }

    private static string BuildMergeResolvePrompt(string specId)
    {
        return $"""
            현재 git 머지 충돌이 발생했습니다. 충돌을 해결해주세요.
            충돌 마커(<<<<<<, ======, >>>>>>)를 모두 제거하고 올바른 코드로 통합하세요.
            두 브랜치의 의도를 모두 보존하되, 스펙 {specId}의 구현이 반영되도록 해주세요.
            해결 후 빌드가 통과하는지 확인하세요.
            """;
    }

    private static string BuildErrorFixPrompt(string specId, string specJson, string errorInfo)
    {
        return $"""
            다음 스펙의 구현에서 오류가 발생했습니다. 오류를 수정하세요.

            스펙 ID: {specId}
            스펙 내용:
            {specJson}

            오류 정보:
            {errorInfo}

            오류를 분석하고 수정한 후 빌드가 통과하는지 확인하세요.
            """;
    }

    /// <summary>
    /// 커맨드 이름을 PATH에서 탐색하여 전체 경로를 반환한다. 찾지 못하면 null.
    /// </summary>
    private static string? ResolveCommand(string command)
    {
        try
        {
            // where.exe (Windows) / which (Unix) 로 경로 탐색
            var whereExe = OperatingSystem.IsWindows() ? "where.exe" : "which";
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whereExe,
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadLine();
            proc.WaitForExit();
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Copilot CLI 실행 결과
/// </summary>
public class CopilotResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
}
