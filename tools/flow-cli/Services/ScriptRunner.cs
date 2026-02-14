using System.Diagnostics;
using System.Text.Json;

namespace FlowCLI.Services;

/// <summary>
/// PowerShell 스크립트를 서브프로세스로 실행하고 JSON 결과를 캡처하는 범용 브릿지.
/// PythonBridge 패턴을 참고하되, PowerShell 스크립트 전용으로 설계.
/// </summary>
public class ScriptRunner
{
    private const int DefaultTimeoutMs = 300_000; // 5분

    /// <summary>
    /// 시스템에서 PowerShell 7+ 실행 파일을 탐색한다.
    /// pwsh (크로스 플랫폼) → powershell (Windows 전용) 순서로 시도.
    /// </summary>
    public string? FindPowerShell()
    {
        string[] candidates = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? ["pwsh", "powershell"]
            : ["pwsh"];

        foreach (var cmd in candidates)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.Major\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && int.TryParse(output, out var major) && major >= 7)
                    return cmd;
            }
            catch
            {
                // Command not found, try next
            }
        }

        // Fallback: powershell (any version) on Windows
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -Command \"echo ok\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
                if (process.ExitCode == 0)
                    return "powershell";
            }
            catch
            {
                // Not available
            }
        }

        return null;
    }

    /// <summary>
    /// PowerShell 스크립트를 실행하고 stdout/stderr를 캡처하여 ScriptResult로 반환한다.
    /// </summary>
    /// <param name="scriptPath">실행할 .ps1 스크립트의 전체 경로</param>
    /// <param name="parameters">스크립트 파라미터 (이름-값 쌍)</param>
    /// <param name="workingDirectory">작업 디렉토리 (null이면 스크립트 디렉토리 사용)</param>
    /// <param name="timeoutMs">타임아웃 밀리초 (기본 300,000 = 5분)</param>
    public ScriptResult RunScript(
        string scriptPath,
        Dictionary<string, string>? parameters = null,
        string? workingDirectory = null,
        int timeoutMs = DefaultTimeoutMs)
    {
        var pwsh = FindPowerShell();
        if (pwsh == null)
        {
            return new ScriptResult
            {
                ExitCode = -1,
                Error = "PowerShell을 찾을 수 없습니다. pwsh (PowerShell 7+)를 설치해 주세요."
            };
        }

        if (!File.Exists(scriptPath))
        {
            return new ScriptResult
            {
                ExitCode = -1,
                Error = $"스크립트를 찾을 수 없습니다: {scriptPath}"
            };
        }

        var args = BuildArguments(scriptPath, parameters);
        var workDir = workingDirectory ?? Path.GetDirectoryName(scriptPath) ?? ".";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pwsh,
                    Arguments = args,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // 비동기로 stdout/stderr 읽기 (데드락 방지)
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var exited = process.WaitForExit(timeoutMs);

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new ScriptResult
                {
                    ExitCode = -1,
                    Stdout = stdoutTask.IsCompleted ? stdoutTask.Result.Trim() : null,
                    Stderr = stderrTask.IsCompleted ? stderrTask.Result.Trim() : null,
                    Error = $"스크립트 실행 타임아웃 ({timeoutMs}ms): {scriptPath}",
                    TimedOut = true
                };
            }

            var stdout = stdoutTask.Result.Trim();
            var stderr = stderrTask.Result.Trim();

            return new ScriptResult
            {
                ExitCode = process.ExitCode,
                Stdout = string.IsNullOrEmpty(stdout) ? null : stdout,
                Stderr = string.IsNullOrEmpty(stderr) ? null : stderr,
                Error = process.ExitCode != 0 ? (string.IsNullOrEmpty(stderr) ? $"스크립트 실패 (exit code: {process.ExitCode})" : stderr) : null
            };
        }
        catch (Exception ex)
        {
            return new ScriptResult
            {
                ExitCode = -1,
                Error = $"스크립트 실행 실패: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// stdout에서 flow JSON 포맷을 파싱한다.
    /// 뒤에서부터 '{' 를 탐색하여 유효한 JSON 객체를 찾는다.
    /// </summary>
    public static JsonElement? TryParseJson(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var pos = output.Length;
        while (pos > 0)
        {
            pos = output.LastIndexOf('{', pos - 1);
            if (pos < 0) break;

            try
            {
                var jsonStr = output[pos..].TrimEnd();
                var element = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                if (element.ValueKind == JsonValueKind.Object)
                    return element;
            }
            catch
            {
                // Not valid JSON from this position, try previous '{'
            }
        }

        return null;
    }

    /// <summary>
    /// PowerShell 호출 인자를 조립한다.
    /// -NoProfile -ExecutionPolicy Bypass -File "scriptPath" -Param1 Value1 ...
    /// </summary>
    private static string BuildArguments(string scriptPath, Dictionary<string, string>? parameters)
    {
        var parts = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", QuoteArg(scriptPath)
        };

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                parts.Add($"-{key}");
                if (!string.IsNullOrEmpty(value))
                    parts.Add(QuoteArg(value));
            }
        }

        return string.Join(" ", parts);
    }

    private static string QuoteArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"'))
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        return arg;
    }
}

/// <summary>
/// PowerShell 스크립트 실행 결과.
/// </summary>
public class ScriptResult
{
    public int ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public string? Error { get; set; }
    public bool TimedOut { get; set; }

    public bool IsSuccess => ExitCode == 0 && !TimedOut;
}
