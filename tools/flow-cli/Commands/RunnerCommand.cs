using Cocona;
using FlowCLI.Services.Runner;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    private RunnerConfig? _runnerConfig;
    private RunnerConfig RunnerConfig => _runnerConfig ??= LoadRunnerConfig();

    [Command("runner-start", Description = "Flow Runner를 시작한다. 스펙 그래프를 폴링하여 미구현 스펙을 자동 구현한다.")]
    public void RunnerStart(
        [Option("daemon", Description = "백그라운드 데몬 모드로 실행")] bool daemon = false,
        [Option("interval", Description = "폴링 주기 (분)")] int interval = 0,
        [Option("model", Description = "Copilot 모델")] string? model = null,
        [Option("once", Description = "한 번만 실행하고 종료")] bool once = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var config = RunnerConfig;
            if (interval > 0) config.PollIntervalMinutes = interval;
            if (!string.IsNullOrEmpty(model)) config.CopilotModel = model;

            // 이미 실행 중인 인스턴스 확인
            var existing = RunnerService.GetRunningInstance(PathResolver.FlowRoot, config.PidFile);
            if (existing?.Status == "running")
            {
                JsonOutput.Write(JsonOutput.Error("runner-start",
                    $"Runner가 이미 실행 중입니다 (PID: {existing.ProcessId}, Instance: {existing.InstanceId})"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var runner = new RunnerService(PathResolver.ProjectRoot, config);

            if (daemon)
            {
                // 데몬 모드: Ctrl+C로 종료
                Console.Error.WriteLine($"[Runner] 데몬 모드 시작 (Instance: {runner.InstanceId})");
                Console.Error.WriteLine($"[Runner] 폴링 주기: {config.PollIntervalMinutes}분, 모델: {config.CopilotModel}");
                Console.Error.WriteLine("[Runner] Ctrl+C로 종료");

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    Console.Error.WriteLine("\n[Runner] 종료 요청 수신, 현재 작업 완료 후 종료...");
                    cts.Cancel();
                };

                // Windows ConPTY 콘솔 그룹에서 분리하여 CTRL_C_EVENT 브로드캐스트 간섭 방지 (F-080-C6)
                // extension의 runner-status 호출이 CTRL_C_EVENT를 broadcast해 데몬이 즉시 종료되는 문제 수정
                ConsoleHelper.DetachConsoleForDaemon();

                runner.RunDaemonAsync(cts.Token).GetAwaiter().GetResult();

                JsonOutput.Write(JsonOutput.Success("runner-start", new
                {
                    mode = "daemon",
                    instanceId = runner.InstanceId,
                    status = "stopped"
                }, "Runner 데몬 종료"), pretty);
            }
            else if (once)
            {
                // 단발 실행
                var results = runner.RunOnceAsync().GetAwaiter().GetResult();

                JsonOutput.Write(JsonOutput.Success("runner-start", new
                {
                    mode = "once",
                    instanceId = runner.InstanceId,
                    totalProcessed = results.Count,
                    successful = results.Count(r => r.Success),
                    failed = results.Count(r => !r.Success),
                    results = results.Select(r => new
                    {
                        specId = r.SpecId,
                        success = r.Success,
                        action = r.Action,
                        error = r.ErrorMessage
                    })
                }, $"Runner 완료: {results.Count(r => r.Success)} 성공 / {results.Count(r => !r.Success)} 실패"), pretty);
            }
            else
            {
                // 기본: 단발 실행
                var results = runner.RunOnceAsync().GetAwaiter().GetResult();

                JsonOutput.Write(JsonOutput.Success("runner-start", new
                {
                    mode = "once",
                    instanceId = runner.InstanceId,
                    totalProcessed = results.Count,
                    successful = results.Count(r => r.Success),
                    failed = results.Count(r => !r.Success),
                    results = results.Select(r => new
                    {
                        specId = r.SpecId,
                        success = r.Success,
                        action = r.Action,
                        error = r.ErrorMessage
                    })
                }, $"Runner 완료: {results.Count(r => r.Success)} 성공 / {results.Count(r => !r.Success)} 실패"), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("runner-start", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    [Command("runner-status", Description = "실행 중인 Runner 상태를 확인한다.")]
    public void RunnerStatus(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var config = RunnerConfig;
            var instance = RunnerService.GetRunningInstance(PathResolver.FlowRoot, config.PidFile);

            if (instance == null)
            {
                JsonOutput.Write(JsonOutput.Success("runner-status", new
                {
                    running = false,
                    message = "Runner가 실행 중이 아닙니다"
                }), pretty);
                return;
            }

            JsonOutput.Write(JsonOutput.Success("runner-status", new
            {
                running = instance.Status == "running",
                instanceId = instance.InstanceId,
                processId = instance.ProcessId,
                startedAt = instance.StartedAt,
                status = instance.Status,
                currentSpec = instance.CurrentSpecId
            }, $"Runner 상태: {instance.Status}"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("runner-status", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    [Command("runner-stop", Description = "실행 중인 Runner를 중지한다.")]
    public void RunnerStop(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var config = RunnerConfig;
            var instance = RunnerService.GetRunningInstance(PathResolver.FlowRoot, config.PidFile);

            if (instance == null || instance.Status != "running")
            {
                JsonOutput.Write(JsonOutput.Error("runner-stop", "실행 중인 Runner가 없습니다"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(instance.ProcessId);
                proc.Kill(entireProcessTree: true);

                // PID 파일 삭제
                var pidPath = Path.Combine(PathResolver.FlowRoot, config.PidFile);
                if (File.Exists(pidPath)) File.Delete(pidPath);

                JsonOutput.Write(JsonOutput.Success("runner-stop", new
                {
                    instanceId = instance.InstanceId,
                    processId = instance.ProcessId
                }, "Runner 중지 완료"), pretty);
            }
            catch (Exception ex)
            {
                JsonOutput.Write(JsonOutput.Error("runner-stop", $"Runner 중지 실패: {ex.Message}"), pretty);
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("runner-stop", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    [Command("runner-logs", Description = "Runner 실행 로그를 조회한다.")]
    public void RunnerLogs(
        [Option("tail", Description = "출력할 마지막 줄 수")] int tail = 50,
        [Option("list", Description = "로그 파일 목록만 출력")] bool list = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var config = RunnerConfig;
            var logService = new RunnerLogService(PathResolver.FlowRoot, config.LogDir, "viewer");

            if (list)
            {
                var files = logService.ListLogFiles();
                JsonOutput.Write(JsonOutput.Success("runner-logs", new
                {
                    files = files.Select(f => Path.GetFileName(f))
                }, $"로그 파일 {files.Count}개"), pretty);
                return;
            }

            var content = logService.ReadLatestLog(tail);
            if (content == null)
            {
                JsonOutput.Write(JsonOutput.Success("runner-logs", new
                {
                    message = "로그 파일이 없습니다"
                }), pretty);
                return;
            }

            // 로그는 사람이 읽을 것이므로 stderr로 출력
            Console.Error.WriteLine(content);

            JsonOutput.Write(JsonOutput.Success("runner-logs", new
            {
                tailLines = tail
            }, "로그 출력 완료"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("runner-logs", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    [Command("runner-plan", Description = "현재 스펙 상태 기준으로 Runner가 집을 후보 순서를 미리 보여준다.")]
    public void RunnerPlan(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var runner = new RunnerService(PathResolver.ProjectRoot, RunnerConfig, echoLogsToConsole: false);
            var plan = runner.GetQueuePlan();

            JsonOutput.Write(JsonOutput.Success("runner-plan", new
            {
                targetStatuses = plan.TargetStatuses,
                totalCandidates = plan.TotalCandidates,
                readyCount = plan.ReadyCount,
                blockedCount = plan.BlockedCount,
                stagedCount = plan.StagedCount,
                reviewReadyCount = plan.ReviewReadyCount,
                nextSpecId = plan.NextSpecId,
                readySpecs = plan.ReadySpecs.Select(spec => new
                {
                    specId = spec.SpecId,
                    title = spec.Title,
                    status = spec.Status,
                    rank = spec.Rank,
                    issuePriorityScore = spec.IssuePriorityScore,
                    isFallback = spec.IsFallback,
                    dependencyCount = spec.DependencyCount,
                    dependencies = spec.Dependencies
                }),
                blockedSpecs = plan.BlockedSpecs.Select(spec => new
                {
                    specId = spec.SpecId,
                    title = spec.Title,
                    status = spec.Status,
                    reason = spec.Reason,
                    unmetDependencies = spec.UnmetDependencies,
                    openQuestionCount = spec.OpenQuestionCount,
                    retryNotBefore = spec.RetryNotBefore
                }),
                stagedSpecs = plan.StagedSpecs.Select(spec => new
                {
                    specId = spec.SpecId,
                    title = spec.Title,
                    status = spec.Status,
                    stage = spec.Stage,
                    lastCompletedAt = spec.LastCompletedAt,
                    worktreePath = spec.WorktreePath
                }),
                reviewReadySpecs = plan.ReviewReadySpecs.Select(spec => new
                {
                    specId = spec.SpecId,
                    title = spec.Title,
                    status = spec.Status,
                    stage = spec.Stage,
                    lastCompletedAt = spec.LastCompletedAt,
                    worktreePath = spec.WorktreePath
                })
            }, plan.NextSpecId == null
                ? "Runner 후보 없음"
                : $"다음 후보: {plan.NextSpecId}"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("runner-plan", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// Runner 설정 로드. .flow/config.json 단일 파일에서 모든 설정을 읽는다.
    /// (runner-config.json은 config.json으로 통합됨)
    /// </summary>
    private RunnerConfig LoadRunnerConfig()
    {
        var flowConfig = FlowConfigService.Load();

        return new RunnerConfig
        {
            PollIntervalMinutes   = flowConfig.PollIntervalMinutes,
            ReviewPollIntervalSeconds = flowConfig.ReviewPollIntervalSeconds,
            MaxConcurrentSpecs    = flowConfig.MaxConcurrentSpecs,
            LogDir                = flowConfig.LogDir,
            PidFile               = flowConfig.PidFile,
            CopilotModel          = flowConfig.CopilotModel,
            CopilotCommand        = flowConfig.CopilotCommand,
            CopilotCliPath        = flowConfig.CopilotCliPath,
            TargetStatuses        = flowConfig.TargetStatuses,
            WorktreeDir           = flowConfig.WorktreeDir,
            CopilotTimeoutMinutes = flowConfig.CopilotTimeoutMinutes,
            RemoteName            = flowConfig.RemoteName,
            MainBranch            = flowConfig.MainBranch,
            RateLimitCooldownSeconds = flowConfig.RateLimitCooldownSeconds,
            TransportErrorCooldownSeconds = flowConfig.TransportErrorCooldownSeconds,
            ExecutionCrashCooldownSeconds = flowConfig.ExecutionCrashCooldownSeconds,

        };
    }
}
