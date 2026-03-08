using System.Diagnostics;
using System.Text;
using FlowCLI.Services.SpecGraph;
using FlowCLI.Services.TestSync;

namespace FlowCLI.Services.Runner;

internal sealed class AutomatedTestService
{
    private readonly RunnerConfig _config;
    private readonly SpecStore _specStore;
    private readonly RunnerLogService _log;
    private readonly TestSyncService _testSyncService;

    public AutomatedTestService(RunnerConfig config, SpecStore specStore, RunnerLogService log)
    {
        _config = config;
        _specStore = specStore;
        _log = log;
        _testSyncService = new TestSyncService(specStore);
    }

    public async Task<AutomatedTestRunResult> RunAndSyncAsync(string specId, string worktreePath)
    {
        if (!_config.AutomatedTestsEnabled)
        {
            return new AutomatedTestRunResult { Executed = false, Success = true };
        }

        var plan = ResolvePlan(worktreePath, specId, _specStore.EvidenceDir);
        if (plan == null)
        {
            _log.Info("automated-tests", "지원되는 자동 테스트 구성을 찾지 못해 테스트를 건너뜁니다.", specId);
            return new AutomatedTestRunResult { Executed = false, Success = true };
        }

        Directory.CreateDirectory(plan.ResultDirectory);
        _log.Info("automated-tests", $"자동 테스트 실행 시작 ({plan.Platform})", specId);

        var execution = await ExecuteAsync(plan, _config.AutomatedTestTimeoutMinutes * 60 * 1000);

        TestSyncResult? syncResult = null;
        if (File.Exists(plan.ResultFilePath))
        {
            syncResult = _testSyncService.Sync(plan.ResultFilePath);
            var evidenceSpecs = _testSyncService.AppendEvidence(
                plan.ResultFilePath,
                syncResult,
                plan.Platform,
                DateTime.UtcNow.ToString("o"));

            _log.Info(
                "automated-tests",
                $"테스트 결과 동기화 완료: mapped={syncResult.MappedTests}, updatedSpecs={syncResult.UpdatedSpecs}, evidenceSpecs={evidenceSpecs}",
                specId);
        }
        else
        {
            _log.Warn("automated-tests", "테스트 결과 파일이 없어 condition sync를 건너뜁니다.", specId);
        }

        if (execution.Success)
        {
            _log.Info("automated-tests", "자동 테스트 통과", specId);
            return new AutomatedTestRunResult
            {
                Executed = true,
                Success = true,
                ResultFilePath = File.Exists(plan.ResultFilePath) ? plan.ResultFilePath : null,
                Platform = plan.Platform,
                SyncResult = syncResult
            };
        }

        var errorMessage = execution.TimedOut
            ? $"자동 테스트 타임아웃 ({_config.AutomatedTestTimeoutMinutes}분)"
            : BuildFailureMessage(plan.Platform, execution, syncResult);

        _log.Error("automated-tests", errorMessage, specId);
        return new AutomatedTestRunResult
        {
            Executed = true,
            Success = false,
            TimedOut = execution.TimedOut,
            ErrorMessage = errorMessage,
            ResultFilePath = File.Exists(plan.ResultFilePath) ? plan.ResultFilePath : null,
            Platform = plan.Platform,
            SyncResult = syncResult
        };
    }

    internal static AutomatedTestPlan? ResolvePlan(string worktreePath, string specId, string evidenceRoot)
    {
        var solutionPath = Directory.GetFiles(worktreePath, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solutionPath != null)
        {
            return CreateDotnetPlan(solutionPath, specId, evidenceRoot);
        }

        var projectPath = Directory.GetFiles(worktreePath, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (projectPath != null)
        {
            return CreateDotnetPlan(projectPath, specId, evidenceRoot);
        }

        return null;
    }

    private static AutomatedTestPlan CreateDotnetPlan(string targetPath, string specId, string evidenceRoot)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var resultDirectory = Path.Combine(evidenceRoot, specId, "runner-tests", timestamp);
        var resultFilePath = Path.Combine(resultDirectory, "runner-tests.trx");
        var arguments = $"test \"{targetPath}\" --logger \"trx;LogFileName=runner-tests.trx\" --results-directory \"{resultDirectory}\"";

        return new AutomatedTestPlan
        {
            Platform = "dotnet",
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(targetPath) ?? ".",
            ResultDirectory = resultDirectory,
            ResultFilePath = resultFilePath
        };
    }

    private async Task<(bool Success, bool TimedOut, string Stdout, string Stderr)> ExecuteAsync(AutomatedTestPlan plan, int timeoutMs)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                Arguments = plan.Arguments,
                WorkingDirectory = plan.WorkingDirectory,
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
            if (e.Data != null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return (false, true, stdout.ToString().Trim(), stderr.ToString().Trim());
        }

        return (process.ExitCode == 0, false, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private static string BuildFailureMessage(string platform, (bool Success, bool TimedOut, string Stdout, string Stderr) execution, TestSyncResult? syncResult)
    {
        var message = $"자동 테스트 실패 ({platform})";
        if (syncResult != null)
        {
            message += $": mapped={syncResult.MappedTests}, failed={syncResult.Mappings.Count(m => m.Status == "failed")}";
        }

        var detail = FirstNonEmptyLine(execution.Stderr) ?? FirstNonEmptyLine(execution.Stdout);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" - {detail}";
        }

        return message;
    }

    private static string? FirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
    }
}