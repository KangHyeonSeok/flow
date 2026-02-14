using System.Text.Json;
using Cocona;
using FlowCLI.Services;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    private BuildOrchestrator? _buildOrchestrator;
    private BuildOrchestrator BuildOrchestrator => _buildOrchestrator ??=
        new BuildOrchestrator(PathResolver, new BuildModuleManager(PathResolver), new ScriptRunner());

    [Command("build", Description = "Build, lint, test, or run a project")]
    public void Build(
        [Argument(Description = "Project path (default: current directory)")] string? target = null,
        [Option("platform", Description = "Target platform (unity|python|node|dotnet|flutter|auto)")] string platform = "auto",
        [Option("lint", Description = "Run linter")] bool lint = false,
        [Option("build", Description = "Run build")] bool build = false,
        [Option("test", Description = "Run tests")] bool test = false,
        [Option("run", Description = "Run built artifact")] bool run = false,
        [Option("all", Description = "Run lint + build + test")] bool all = false,
        [Option("config", Description = "Build configuration file path")] string? config = null,
        [Option("timeout", Description = "Timeout per step in seconds")] int timeout = 300,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            // 프로젝트 경로 해석
            var projectPath = Path.GetFullPath(target ?? ".");
            if (!Directory.Exists(projectPath))
            {
                JsonOutput.Write(JsonOutput.Error("build",
                    $"프로젝트 경로를 찾을 수 없습니다: {target}",
                    new { path = projectPath }), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // 플랫폼 감지
            var detectedPlatform = platform;
            if (platform.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                detectedPlatform = BuildOrchestrator.DetectPlatform(projectPath);
                if (detectedPlatform == "unknown")
                {
                    JsonOutput.Write(JsonOutput.Error("build",
                        "프로젝트 타입을 감지할 수 없습니다. --platform 옵션을 명시해 주세요.",
                        new { path = projectPath, supported = new[] { "unity", "python", "node", "dotnet", "flutter" } }), pretty);
                    Environment.ExitCode = 1;
                    return;
                }
            }

            // 실행할 단계 결정
            var steps = new List<string>();
            if (all)
            {
                steps.AddRange(new[] { "lint", "build", "test" });
            }
            else
            {
                if (lint) steps.Add("lint");
                if (build) steps.Add("build");
                if (test) steps.Add("test");
                if (run) steps.Add("run");
            }

            // 아무 옵션도 없으면 기본 build
            if (steps.Count == 0)
            {
                steps.Add("build");
            }

            // 모듈 확인/설치
            var moduleError = BuildOrchestrator.CheckAndInstallModule(detectedPlatform);
            if (moduleError != null)
            {
                JsonOutput.Write(JsonOutput.Error("build", moduleError,
                    new { platform = detectedPlatform }), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // 추가 파라미터 수집
            Dictionary<string, string>? extraParams = null;
            if (!string.IsNullOrEmpty(config))
            {
                extraParams = new Dictionary<string, string> { ["Config"] = config };
            }

            // 빌드 실행
            var result = BuildOrchestrator.Execute(
                projectPath,
                detectedPlatform,
                steps,
                extraParams,
                timeout * 1000);

            // 결과 출력
            if (result.Success)
            {
                JsonOutput.Write(JsonOutput.Success("build", new
                {
                    platform = result.Platform,
                    project_path = result.ProjectPath,
                    total_duration_ms = result.TotalDurationMs,
                    steps = result.Steps.Select(s => new
                    {
                        action = s.Action,
                        success = s.Success,
                        duration_ms = s.DurationMs,
                        output_path = s.OutputPath
                    }),
                    steps_count = result.Steps.Count
                }, result.Message), pretty);
            }
            else
            {
                var failedStep = result.Steps.LastOrDefault(s => !s.Success);
                JsonOutput.Write(JsonOutput.Error("build",
                    result.Message ?? "빌드 실패",
                    new
                    {
                        platform = result.Platform,
                        project_path = result.ProjectPath,
                        total_duration_ms = result.TotalDurationMs,
                        failed_step = failedStep?.Action,
                        exit_code = failedStep?.ExitCode,
                        stderr = failedStep?.Stderr,
                        steps_completed = result.Steps.Count(s => s.Success),
                        steps_total = result.Steps.Count
                    }), pretty);
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("build", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
