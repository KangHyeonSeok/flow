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
    public async Task<CopilotResult> ImplementSpecAsync(string specId, string specJson, string specFilePath, string worktreePath, string? previousReview = null)
    {
        var prompt = BuildImplementPrompt(specId, specJson, specFilePath, previousReview);
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

    internal static string BuildImplementPrompt(string specId, string specJson, string specFilePath, string? previousReview)
    {
        var accessInstructions = BuildSpecAccessInstructions(specId, worktreeContext: true);
        var reviewSection = string.IsNullOrWhiteSpace(previousReview)
            ? ""
            : $"""


                이전 구현 검토 결과 (참고하여 다른 접근 방식을 사용하세요):
                {previousReview}
                """;

        return $"""
            다음 스펙을 TDD 방식으로 구현하세요. 반드시 아래 Phase 순서를 따르세요.
            기존 프로젝트 구조와 코딩 패턴을 따르세요.

                        {accessInstructions}

            ## Phase 1: 테스트 작성 (코드 구현 전)
            스펙의 각 condition을 읽고, 자동 테스트가 가능한 condition부터 테스트를 먼저 작성하세요.
            - 테스트는 condition의 수락 조건을 직접 검증해야 합니다.
            - 아직 구현이 없으므로 테스트는 컴파일은 되지만 실패(Red)하는 상태여야 합니다.
            - 자동 테스트를 만들거나 안정적으로 실행하기 어려운 condition은 수동 검증 대상으로 남기세요.
                        - 수동 검증으로 남길 때는 먼저 위 규칙대로 `flow.ps1 spec-get {specId}`로 최신 스펙을 다시 읽고, 필요하면 스펙 파일 `{specFilePath}`의 해당 condition.metadata에 아래 필드를 기록하세요.
              - `requiresManualVerification`: true
              - `manualVerificationReason`: 자동화가 어려운 이유
              - `manualVerificationItems`: 사용자가 review 단계에서 확인할 수 있는 짧은 체크 항목 배열
            테스트를 작성한 뒤 빌드가 통과하는지 확인하세요. (테스트 실행은 아직 하지 않습니다.)

            ## Phase 2: 구현
            Phase 1에서 작성한 테스트를 통과시키면서 스펙의 모든 condition을 만족하는 코드를 작성하세요.
            - 테스트를 통과시키기 위한 최소한의 올바른 구현을 하세요.
            - 구현 중 테스트를 수정해야 한다면, 테스트의 검증 의도는 유지하면서 수정하세요.

            ## Phase 3: 검증
            모든 테스트를 실행하고 결과를 확인하세요.
            - 빌드와 테스트가 모두 통과하는지 확인하세요.
            - condition별 성공/실패 여부와 수집한 결과 파일/로그 위치를 분명히 남겨서 runner가 tests/evidence를 동기화할 수 있게 하세요.
            - 사람이 직접 확인해야 하는 condition은 review 단계에서 아래 명령으로 결과를 기록할 수 있게 condition ID와 확인 항목을 남기세요.
              - `./flow.ps1 spec-record-condition-review {specId} --condition-id <condition-id> --result passed|failed --comment ""<review comment>"" --reviewer ""human-review""`
            - condition.status는 직접 `verified`나 `done`으로 바꾸지 마세요. 상태 변경은 runner/review 단계에서 처리합니다.

            ## 최종 응답
            다음을 짧게 요약하세요.
            - 자동 테스트로 다룬 condition ID 목록
            - 수동 검증으로 남긴 condition ID 목록과 이유
            - 추가하거나 실행한 테스트/검증 명령
            - 수집한 테스트 결과 파일, 로그, 스크린샷 등 evidence 경로
            {reviewSection}
            스펙 ID: {specId}
            직접 편집이 필요할 때 참고할 스펙 파일 경로: {specFilePath}
            스펙 내용(참고용 요약, 최신본은 반드시 flow.ps1으로 재조회):
            {specJson}
            """;
    }

    internal static string BuildSpecAccessInstructions(string specId, bool worktreeContext)
    {
        var locationHint = worktreeContext
            ? "현재 작업 디렉터리는 runner worktree일 수 있으므로, 상위 경로를 올라가 `flow.ps1`를 찾으세요."
            : "현재 작업 디렉터리에서 `flow.ps1`를 찾을 수 없으면 상위 경로를 올라가 찾으세요.";

        return string.Join(Environment.NewLine,
        [
            "스펙 조회 규칙:",
            "- 스펙의 source of truth는 직접 파일 경로나 프롬프트에 포함된 JSON이 아니라 `flow.ps1 spec-get` 결과입니다.",
            $"- {locationHint}",
            "- PowerShell 예시:",
            "  $flowRoot = (Get-Location).Path",
            "  while ($true)",
            "  {",
            "      $candidate = Join-Path $flowRoot 'flow.ps1'",
            "      if (Test-Path $candidate) { $flow = $candidate; break }",
            "      $parent = Split-Path $flowRoot -Parent",
            "      if ($parent -eq $flowRoot) { throw 'flow.ps1 not found in parent chain' }",
            "      $flowRoot = $parent",
            "  }",
            $"  & $flow spec-get {specId}",
            $"- 구조화된 JSON이 필요하면 `& $flow spec-get {specId} --json --pretty`를 사용하세요.",
            "- 스펙을 다시 확인해야 할 때는 파일을 직접 열지 말고 항상 위 명령으로 최신 내용을 다시 읽으세요."
        ]);
    }

    private static string BuildMergeResolvePrompt(string specId)
    {
        return $"""
            현재 git 머지 충돌이 발생했습니다. 충돌을 해결해주세요.
            필요하면 상위 경로에서 `flow.ps1`를 찾아 `flow.ps1 spec-get {specId}`로 최신 스펙을 다시 확인하세요.
            충돌 마커(<<<<<<, ======, >>>>>>)를 모두 제거하고 올바른 코드로 통합하세요.
            두 브랜치의 의도를 모두 보존하되, 스펙 {specId}의 구현이 반영되도록 해주세요.
            해결 후 빌드가 통과하는지 확인하세요.
            """;
    }

    private static string BuildErrorFixPrompt(string specId, string specJson, string errorInfo)
    {
        var accessInstructions = BuildSpecAccessInstructions(specId, worktreeContext: true);
        return $"""
            다음 스펙의 구현에서 오류가 발생했습니다. 오류를 수정하세요.

            {accessInstructions}

            스펙 ID: {specId}
            스펙 내용(참고용 요약, 최신본은 반드시 flow.ps1으로 재조회):
            {specJson}

            오류 정보:
            {errorInfo}

            오류를 분석하고 수정한 후 빌드가 통과하는지 확인하세요.
            """;
    }

    internal static string BuildReviewPrompt(string specId, string specJson, string reviewContext, string reviewerId)
    {
        var reviewFile = $".flow/review/{specId}-review.json";
        var accessInstructions = BuildSpecAccessInstructions(specId, worktreeContext: true);
        return $@"다음 스펙은 현재 needs-review 상태입니다. 코드 파일을 수정하지 말고, 검토 결과를 flow CLI로 저장하세요.

반드시 아래 절차를 그대로 수행하세요.
1. `.flow/review` 디렉토리가 없으면 생성합니다.
2. 검토를 시작하기 전에 아래 조회 규칙대로 `flow.ps1 spec-get {specId}`를 실행해 최신 스펙을 다시 읽습니다.
3. 조회 결과를 기준으로 검토하되, 스펙 JSON 파일을 직접 수정하지 않습니다.
4. 아래 스키마와 정확히 일치하는 JSON 객체 하나만 `{reviewFile}` 파일에 저장합니다.
5. 다음 명령으로 review를 반영합니다.
   ./flow.ps1 spec-append-review {specId} --input-file ""{reviewFile}"" --reviewer ""{reviewerId}""
6. 명령이 JSON 형식 또는 스키마 오류로 실패하면, 오류 메시지를 보고 `{reviewFile}`을 수정한 뒤 같은 명령을 다시 실행합니다.
7. `spec-append-review` 명령이 exit 0으로 성공할 때까지 반복합니다.
8. 스펙 JSON 파일을 직접 수정하지 않습니다.
9. 최종 응답은 짧은 완료 메시지 한 줄만 출력합니다.

{accessInstructions}

검토 목적:
- verified가 아닌 condition의 code/tests/evidence/metadata를 검토해 조건이 이미 충족되었는지 판단
- 왜 실패했는지 또는 왜 재작업이 필요한지 요약
- 어떤 대안이 있는지 제안
- 다음 시도에서 무엇을 해야 하는지 제안
- 사용자 판단이 필요한지 식별
- 추가 정보 요청이 필요한지 식별

condition 판정 규칙:
- 스펙 JSON 안의 condition, tests, evidence, metadata, codeRefs와 실제 워크스페이스 파일을 함께 검토할 수 있습니다.
- 조건이 이미 만족되었다고 근거를 갖고 판단되면 그 condition ID를 `verifiedConditionIds`에 추가하세요.
- 확신할 수 없는 condition은 `verifiedConditionIds`에 넣지 마세요.
- `verifiedConditionIds`에 넣는 condition은 코드 수정 없이 review 단계에서 verified 처리됩니다.
- 스펙 JSON 파일을 직접 수정하지 않습니다.

질문 생성 규칙:
- 사용자 질문은 제품 요구사항, 정책 결정, 도메인 지식, 누락된 스펙 설명처럼 사용자만 답할 수 있는 정보에 한정합니다.
- runner 로그, stdout/stderr, git diff, 변경 파일 목록, 실행 아티팩트 같은 내부 디버깅 정보는 사용자에게 요청하지 않습니다.
- 내부 실행 정보가 더 필요하면 질문 대신 failureReasons, alternatives, suggestedAttempts에 검토자가 내부 로그/변경 사항을 확인해야 한다고만 남기고 requiresUserInput은 false로 유지합니다.

review JSON 스키마:
{{
    ""summary"": ""한두 문장 요약"",
    ""failureReasons"": [""실패/보류 원인""],
    ""alternatives"": [""가능한 대안""],
    ""suggestedAttempts"": [""다음 시도 액션""],
    ""verifiedConditionIds"": [""충족이 확인된 condition ID""],
    ""requiresUserInput"": true,
    ""additionalInformationRequests"": [""추가로 필요한 정보""],
    ""questions"": [
        {{
            ""type"": ""user-decision|missing-info|clarification"",
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
