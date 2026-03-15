using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FlowCore.Backend;

/// <summary>Claude CLI 백엔드 (stream-json 모드)</summary>
public sealed class ClaudeCliBackend : ICliBackend
{
    private readonly string _command;
    private readonly int _maxRetries;

    public string BackendId => "claude-cli";

    public ClaudeCliBackend(string command = "claude", int maxRetries = 2)
    {
        _command = command;
        _maxRetries = maxRetries;
    }

    public async Task<CliResponse> RunPromptAsync(
        string prompt, CliBackendOptions options, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            var result = await RunOnceAsync(prompt, options, ct);

            if (result.Success || attempt > _maxRetries || ct.IsCancellationRequested)
                return result;

            // exit code != 0 → backoff 후 재시도
            var backoff = TimeSpan.FromSeconds(30 * attempt);
            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { return result; }
        }
    }

    private async Task<CliResponse> RunOnceAsync(
        string prompt, CliBackendOptions options, CancellationToken ct)
    {
        var (fileName, arguments) = BuildProcessArgs(prompt, options);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        if (options.WorkingDirectory is not null)
            psi.WorkingDirectory = options.WorkingDirectory;

        // 환경변수 설정
        psi.Environment["CI"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment.Remove("CLAUDECODE");
        psi.Environment.Remove("CLAUDE_CODE");

        // Windows: CLAUDE_CODE_GIT_BASH_PATH 자동 탐지
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !psi.Environment.ContainsKey("CLAUDE_CODE_GIT_BASH_PATH"))
        {
            var gitBashPath = FindGitBash();
            if (gitBashPath != null)
                psi.Environment["CLAUDE_CODE_GIT_BASH_PATH"] = gitBashPath;
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        }
        catch (Exception ex)
        {
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = $"process start failed: {ex.Message}",
                StopReason = CliStopReason.Error
            };
        }

        try
        {
            // stdin에 프롬프트 write → close
            await process.StandardInput.WriteAsync(prompt.AsMemory(), ct);
            process.StandardInput.Close();

            // stderr 비동기 수집
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            // Hard timeout + Idle timeout 통합 CTS
            using var hardCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hardCts.CancelAfter(options.HardTimeout);

            // stdout을 줄 단위로 읽으며 idle timeout 모니터링
            var stdoutBuilder = new StringBuilder();
            var lastOutputTime = DateTime.UtcNow;
            var idleTimeout = options.IdleTimeout;
            var timedOutByIdle = false;

            try
            {
                while (true)
                {
                    // idle 남은 시간 계산
                    var elapsed = DateTime.UtcNow - lastOutputTime;
                    var remaining = idleTimeout - elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        timedOutByIdle = true;
                        break;
                    }

                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(hardCts.Token);
                    idleCts.CancelAfter(remaining);

                    string? line;
                    try
                    {
                        line = await process.StandardOutput.ReadLineAsync(idleCts.Token);
                    }
                    catch (OperationCanceledException) when (!hardCts.IsCancellationRequested)
                    {
                        // idle timeout
                        timedOutByIdle = true;
                        break;
                    }

                    if (line == null) break; // EOF
                    stdoutBuilder.AppendLine(line);
                    lastOutputTime = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Hard timeout
                await ProcessKiller.GracefulKillAsync(process, TimeSpan.FromSeconds(5));
                return new CliResponse
                {
                    ResponseText = string.Empty,
                    Success = false,
                    ErrorMessage = $"hard timeout ({options.HardTimeout.TotalSeconds}s)",
                    StopReason = CliStopReason.Timeout
                };
            }

            if (timedOutByIdle)
            {
                await ProcessKiller.GracefulKillAsync(process, TimeSpan.FromSeconds(5));
                return new CliResponse
                {
                    ResponseText = stdoutBuilder.ToString(),
                    Success = false,
                    ErrorMessage = $"idle timeout ({idleTimeout.TotalSeconds}s without output)",
                    StopReason = CliStopReason.Timeout
                };
            }

            // EOF 도달 — 프로세스 종료 대기
            try
            {
                await process.WaitForExitAsync(hardCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await ProcessKiller.GracefulKillAsync(process, TimeSpan.FromSeconds(5));
                return new CliResponse
                {
                    ResponseText = string.Empty,
                    Success = false,
                    ErrorMessage = $"hard timeout ({options.HardTimeout.TotalSeconds}s)",
                    StopReason = CliStopReason.Timeout
                };
            }

            var stdout = stdoutBuilder.ToString();
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new CliResponse
                {
                    ResponseText = stdout,
                    Success = false,
                    ErrorMessage = $"exit code {process.ExitCode}: {TruncateStderr(stderr)}",
                    StopReason = CliStopReason.Error
                };
            }

            return StreamJsonParser.Parse(stdout);
        }
        catch (OperationCanceledException)
        {
            await ProcessKiller.GracefulKillAsync(process, TimeSpan.FromSeconds(5));
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "cancelled",
                StopReason = CliStopReason.Cancelled
            };
        }
        finally
        {
            process.Dispose();
        }
    }

    private (string fileName, List<string> args) BuildProcessArgs(
        string prompt, CliBackendOptions options)
    {
        var args = new List<string>();

        // Windows: .ps1/.bat 감지
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _command.EndsWith(".ps1"))
        {
            var fileName = "pwsh";
            args.AddRange(["-NonInteractive", "-File", _command]);
            AddClaudeArgs(args, options);
            return (fileName, args);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _command.EndsWith(".bat"))
        {
            var fileName = "cmd.exe";
            args.AddRange(["/c", _command]);
            AddClaudeArgs(args, options);
            return (fileName, args);
        }

        AddClaudeArgs(args, options);
        return (_command, args);
    }

    private static void AddClaudeArgs(List<string> args, CliBackendOptions options)
    {
        args.AddRange(["-p", "--output-format", "stream-json", "--verbose", "--dangerously-skip-permissions"]);

        if (options.AllowedTools is { Count: > 0 } tools)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", tools));
        }
    }

    private static string TruncateStderr(string stderr)
    {
        const int maxLen = 500;
        return stderr.Length <= maxLen ? stderr.Trim() : stderr[..maxLen].Trim() + "…";
    }

    private static string? FindGitBash()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // PATH에서 git.exe를 찾아서 상대 경로로 bash.exe 탐지
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "git",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                foreach (var line in output.Split('\n'))
                {
                    var gitPath = line.Trim();
                    if (string.IsNullOrEmpty(gitPath)) continue;
                    // git.exe → ../bin/bash.exe
                    var gitDir = Path.GetDirectoryName(Path.GetDirectoryName(gitPath));
                    if (gitDir != null)
                    {
                        var bashPath = Path.Combine(gitDir, "bin", "bash.exe");
                        if (File.Exists(bashPath)) return bashPath;
                    }
                }
            }
        }
        catch { /* best-effort */ }

        return null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
