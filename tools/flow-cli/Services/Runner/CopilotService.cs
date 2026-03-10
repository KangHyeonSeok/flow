using System.Diagnostics;
using System.Text;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

/// <summary>
/// GitHub Copilot CLI를 호출하여 스펙 기반 코드 구현/수정을 수행하는 서비스.
/// copilot -p "프롬프트" 형식으로 호출.
/// </summary>
public class CopilotService
{
    private const string ReviewModel = "gpt-5-mini";

    private readonly RunnerConfig _config;
    private readonly RunnerLogService _log;

    /// <summary>
    /// 검토 목적:
    /// - verified가 아닌 condition의 code/tests/evidence/metadata를 검토해 조건이 이미 충족되었는지 판단
    /// - 왜 실패했는지 또는 왜 재작업이 필요한지 요약
    /// - 어떤 대안이 있는지 제안
    /// - 다음 시도에서 무엇을 해야 하는지 제안
    /// - 사용자 판단이 필요한지 식별
    /// - 추가 정보 요청이 필요한지 식별
    /// 
    /// condition 판정 규칙:
    /// - 스펙 JSON 안의 condition, tests, evidence, metadata, codeRefs와 실제 워크스페이스 파일을 함께 검토할 수 있습니다.
    /// - 조건이 이미 만족되었다고 근거를 갖고 판단되면 그 condition ID를 `verifiedConditionIds`에 추가하세요.
    /// - 확신할 수 없는 condition은 `verifiedConditionIds`에 넣지 마세요.
    /// - `verifiedConditionIds`에 넣는 condition은 코드 수정 없이 review 단계에서 verified 처리됩니다.
    /// - 스펙 JSON 파일을 직접 수정하지 않습니다.
    public CopilotService(RunnerConfig config, RunnerLogService log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Copilot CLI를 호출하여 스펙을 구현한다.
    /// worktreePath에서 실행하여 해당 작업 디렉토리에서 코드를 생성/수정.
    /// </summary>
    public async Task<CopilotResult> ImplementSpecAsync(string specId, string specJson, string worktreePath, string? previousReview = null)
    {
        var prompt = BuildImplementPrompt(specId, specJson, previousReview);
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

    /// <summary>
    /// Copilot CLI를 호출하여 손상 스펙 JSON을 복구한다 (F-025-C4).
    /// </summary>
    public async Task<CopilotResult> RepairSpecAsync(string specId, string repairPrompt, string workDir)
    {
        return await RunCopilotAsync(repairPrompt, workDir, specId, "repair-spec");
    }

    /// <summary>
    /// Copilot CLI를 호출하여 검토 대기 스펙을 분석한다.
    /// review JSON 파일을 만들고 flow spec-append-review 명령으로 반영하도록 요청한다.
    /// </summary>
    public async Task<CopilotResult> ReviewSpecAsync(SpecNode spec, string specJson, string reviewContext, string workDir, string reviewerId)
    {
        var prompt = BuildReviewPrompt(spec.Id, specJson, reviewContext, reviewerId);
        var reviewModel = ResolveReviewModel(spec, _config);
        return await RunCopilotAsync(prompt, workDir, spec.Id, "review", allowWrites: true, modelOverride: reviewModel);
    }

    internal static string ResolveReviewModel(SpecNode spec, RunnerConfig config)
    {
        return ReviewModel;
    }

    private async Task<CopilotResult> RunCopilotAsync(string prompt, string workDir, string specId, string action, bool allowWrites = true, string? modelOverride = null)
    {
        var model = string.IsNullOrWhiteSpace(modelOverride) ? _config.CopilotModel : modelOverride;
        _log.Info($"copilot-{action}", $"Copilot 호출 시작 (model: {model})", specId);

        try
        {
            var escapedPrompt = prompt.Replace("\"", "\\\"");
            var copilotArgs = $"-p \"{escapedPrompt}\" --model {model}";
            if (allowWrites)
            {
                copilotArgs += " --yolo --autopilot";
            }

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
                    ErrorMessage = $"Copilot 호출 타임아웃 ({_config.CopilotTimeoutMinutes}분)",
                    FailureCategory = "transport-error"
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
                var failureCategory = ClassifyFailure(stdoutStr, stderrStr, process.ExitCode);
                _log.Error($"copilot-{action}", $"Copilot 실패 (exit: {process.ExitCode}): {stderrStr}", specId);
                return new CopilotResult
                {
                    Success = false,
                    Output = stdoutStr,
                    ErrorMessage = stderrStr,
                    ExitCode = process.ExitCode,
                    FailureCategory = failureCategory
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

    internal static string BuildImplementPrompt(string specId, string specJson, string? previousReview)
    {
        var reviewSection = string.IsNullOrWhiteSpace(previousReview)
            ? ""
            : $"""

                이전 리뷰 결과 (다른 접근 방식을 사용하세요):
                {previousReview}
                """;

        return $"""
            TDD로 다음 스펙을 구현하세요. 기존 프로젝트 구조와 코딩 패턴을 따르세요.
            스펙 상세는 `./flow.ps1 spec-get {specId}`로 조회하세요.
            condition.status를 직접 변경하지 마세요. 상태 변경은 runner가 처리합니다.
            {reviewSection}
            스펙 ID: {specId}
            스펙 내용:
            {specJson}
            """;
    }


    private static string BuildMergeResolvePrompt(string specId)
    {
        return $"""
            현재 git 머지 충돌이 발생했습니다. 충돌을 해결해주세요.
            스펙 확인: `./flow.ps1 spec-get {specId}`
            충돌 마커(<<<<<<, ======, >>>>>>)를 모두 제거하고 올바른 코드로 통합하세요.
            두 브랜치의 의도를 모두 보존하되, 스펙 {specId}의 구현이 반영되도록 해주세요.
            해결 후 빌드가 통과하는지 확인하세요.
            """;
    }

    private static string BuildErrorFixPrompt(string specId, string specJson, string errorInfo)
    {
        return $"""
            다음 스펙의 구현에서 오류가 발생했습니다. 오류를 수정하세요.
            스펙 상세는 `./flow.ps1 spec-get {specId}`로 조회하세요.

            스펙 ID: {specId}
            스펙 내용:
            {specJson}

            오류 정보:
            {errorInfo}

            오류를 분석하고 수정한 후 빌드가 통과하는지 확인하세요.
            """;
    }

    internal static string BuildReviewPrompt(string specId, string specJson, string reviewContext, string reviewerId)
    {
        var reviewFile = $".flow/review/{specId}-review.json";
        return $@"다음 스펙은 현재 needs-review 상태입니다. 코드 파일을 수정하지 말고, 검토 결과를 flow CLI로 저장하세요.
스펙 상세는 `./flow.ps1 spec-get {specId}`로 조회하세요. 스펙 JSON 파일을 직접 수정하지 마세요.

절차:
1. `.flow/review` 디렉토리가 없으면 생성합니다.
2. 아래 스키마의 JSON을 `{reviewFile}`에 저장합니다.
3. `./flow.ps1 spec-append-review {specId} --input-file ""{reviewFile}"" --reviewer ""{reviewerId}""`로 반영합니다.
4. 실패하면 `{reviewFile}`을 수정 후 재실행합니다. exit 0까지 반복합니다.
5. 최종 응답은 완료 메시지 한 줄만 출력합니다.

검토 목적:
- verified가 아닌 condition이 이미 충족되었는지 판단
- 실패/재작업 이유 요약, 대안 제안, 다음 시도 제안
- 사용자 판단이 필요한지 식별

condition 판정:
- 충족이 확인된 condition ID를 `verifiedConditionIds`에 추가 (확신 없으면 넣지 마세요)
- `verifiedConditionIds`의 condition은 코드 수정 없이 verified 처리됩니다

질문 생성:
- 사용자 결정이 필요한 항목만 `questions`에 넣으세요
- 구현 전에 개발자가 확인하거나 재현할 수 있는 항목은 `additionalInformationRequests`에 넣으세요
- 내부 디버깅 정보는 사용자 질문으로 만들지 말고 failureReasons/suggestedAttempts 또는 additionalInformationRequests에 남기세요

review JSON 스키마:
{{
    ""summary"": ""한두 문장 요약"",
    ""failureReasons"": [""실패/보류 원인""],
    ""alternatives"": [""가능한 대안""],
    ""suggestedAttempts"": [""다음 시도 액션""],
    ""verifiedConditionIds"": [""충족 확인된 condition ID""],
    ""requiresUserInput"": true,
    ""additionalInformationRequests"": [""구현 전에 개발자가 확인할 항목""],
    ""questions"": [
        {{
            ""type"": ""user-decision"",
            ""question"": ""사용자에게 물을 질문"",
            ""why"": ""왜 필요한지""
        }}
    ]
}}

스펙 ID: {specId}
검토 컨텍스트:
{reviewContext}

스펙 내용:
{specJson}";
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

    private static string? ClassifyFailure(string stdout, string stderr, int exitCode)
    {
        var combined = $"{stderr}\n{stdout}";
        if (string.IsNullOrWhiteSpace(combined) && exitCode != 0)
        {
            return null;
        }

        if (ContainsAny(combined,
                "429",
                "rate limit",
                "rate-limit",
                "rate limited",
                "too many requests",
                "retry after"))
        {
            return "rate-limited";
        }

        if (ContainsAny(combined,
                "econnreset",
                "etimedout",
                "ehostunreach",
                "enotfound",
                "socket hang up",
                "connection reset",
                "connection aborted",
                "connection closed",
                "network error",
                "transport error",
                "tls",
                "ssl",
                "proxy error",
                "broken pipe",
                "unexpected eof"))
        {
            return "transport-error";
        }

        return null;
    }

    private static bool ContainsAny(string source, params string[] needles)
        => needles.Any(needle => source.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
    public string? FailureCategory { get; set; }
}
