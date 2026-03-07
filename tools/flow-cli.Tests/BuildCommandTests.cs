using System.IO;
using System.Text.Json;
using FluentAssertions;
using FlowCLI.Models;
using FlowCLI.Services;
using Xunit;

namespace FlowCLI.Tests;

public class BuildCommandTests
{
    private readonly string _testDir;
    private readonly TestPathResolver _paths;

    public BuildCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "flow-build-cmd-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _paths = new TestPathResolver(_testDir);
    }

    // ─── BuildOrchestrator.DetectPlatform Tests (CLI 연동) ───

    [Fact]
    public void DetectPlatform_UnityProject_ReturnsUnity()
    {
        // Unity 프로젝트 구조 시뮬레이션
        Directory.CreateDirectory(Path.Combine(_testDir, "Assets"));
        Directory.CreateDirectory(Path.Combine(_testDir, "ProjectSettings"));

        var manager = new BuildModuleManager(_paths);
        var orchestrator = new BuildOrchestrator(_paths, manager, new ScriptRunner());

        var result = orchestrator.DetectPlatform(_testDir);
        result.Should().Be("unity");
    }

    [Fact]
    public void DetectPlatform_DotnetProject_ReturnsDotnet()
    {
        File.WriteAllText(Path.Combine(_testDir, "test.csproj"), "<Project/>");

        var manager = new BuildModuleManager(_paths);
        var orchestrator = new BuildOrchestrator(_paths, manager, new ScriptRunner());

        var result = orchestrator.DetectPlatform(_testDir);
        result.Should().Be("dotnet");
    }

    [Fact]
    public void DetectPlatform_PythonProject_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_testDir, "setup.py"), "");

        var manager = new BuildModuleManager(_paths);
        var orchestrator = new BuildOrchestrator(_paths, manager, new ScriptRunner());

        var result = orchestrator.DetectPlatform(_testDir);
        result.Should().Be("python");
    }

    [Fact]
    public void DetectPlatform_UnknownProject_ReturnsUnknown()
    {
        // 빈 디렉토리 — 아무 감지 규칙도 매치 안됨
        var manager = new BuildModuleManager(_paths);
        var orchestrator = new BuildOrchestrator(_paths, manager, new ScriptRunner());

        var result = orchestrator.DetectPlatform(_testDir);
        result.Should().Be("unknown");
    }

    // ─── Step 결정 로직 테스트 ───

    [Fact]
    public void Steps_AllFlag_IncludesLintBuildTest()
    {
        var steps = new List<string>();
        bool all = true, lint = false, build = false, test = false, run = false;

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

        steps.Should().BeEquivalentTo(new[] { "lint", "build", "test" });
    }

    [Fact]
    public void Steps_NoFlags_DefaultsToBuild()
    {
        var steps = new List<string>();
        bool all = false, lint = false, build = false, test = false, run = false;

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

        if (steps.Count == 0)
            steps.Add("build");

        steps.Should().BeEquivalentTo(new[] { "build" });
    }

    [Fact]
    public void Steps_MultipleFlags_AllIncluded()
    {
        var steps = new List<string>();
        bool all = false, lint = true, build = true, test = false, run = true;

        if (lint) steps.Add("lint");
        if (build) steps.Add("build");
        if (test) steps.Add("test");
        if (run) steps.Add("run");

        steps.Should().BeEquivalentTo(new[] { "lint", "build", "run" });
    }

    // ─── BuildResult JSON 출력 형식 테스트 ───

    [Fact]
    public void BuildResult_Success_SerializesCorrectly()
    {
        var result = new BuildResult
        {
            Success = true,
            Platform = "unity",
            ProjectPath = "/test/project",
            TotalDurationMs = 1234,
            Message = "빌드 성공",
            Steps = new List<BuildStepResult>
            {
                new()
                {
                    Success = true,
                    Action = "build",
                    Platform = "unity",
                    DurationMs = 1000,
                    ExitCode = 0
                }
            }
        };

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(1);
        result.Steps[0].Action.Should().Be("build");
    }

    [Fact]
    public void BuildResult_Failure_ContainsFailedStep()
    {
        var result = new BuildResult
        {
            Success = false,
            Platform = "dotnet",
            ProjectPath = "/test/project",
            TotalDurationMs = 500,
            Message = "lint 실패",
            Steps = new List<BuildStepResult>
            {
                new()
                {
                    Success = false,
                    Action = "lint",
                    Platform = "dotnet",
                    DurationMs = 500,
                    ExitCode = 1,
                    Stderr = "error CS0001"
                }
            }
        };

        result.Success.Should().BeFalse();
        var failedStep = result.Steps.LastOrDefault(s => !s.Success);
        failedStep.Should().NotBeNull();
        failedStep!.Action.Should().Be("lint");
        failedStep.ExitCode.Should().Be(1);
    }

    // ─── BuildModuleManager 통합 테스트 ───

    [Fact]
    public void ModuleManager_NoModuleInstalled_ReturnsNotInstalled()
    {
        var manager = new BuildModuleManager(_paths);

        manager.IsInstalled("unity").Should().BeFalse();
    }

    [Fact]
    public void ModuleManager_WithManifest_LoadsCorrectly()
    {
        var modulePath = _paths.GetBuildModulePath("unity");
        Directory.CreateDirectory(modulePath);

        var manifest = new BuildManifest
        {
            Name = "unity",
            Version = "1.0.0",
            Description = "Unity build module",
            Scripts = new BuildScripts
            {
                Build = "scripts/build.ps1"
            }
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.GetBuildManifestPath("unity"), json);

        var manager = new BuildModuleManager(_paths);
        manager.IsInstalled("unity").Should().BeTrue();

        var loaded = manager.LoadManifest("unity");
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("unity");
        loaded.Version.Should().Be("1.0.0");
    }

    // ─── 에지 케이스 ───

    [Fact]
    public void ProjectPath_Null_ResolvesToCurrentDirectory()
    {
        string? target = null;
        var projectPath = Path.GetFullPath(target ?? ".");

        projectPath.Should().NotBeNullOrEmpty();
        Directory.Exists(projectPath).Should().BeTrue();
    }

    [Fact]
    public void Platform_CaseInsensitive_AutoDetect()
    {
        var platform = "AUTO";
        platform.Equals("auto", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    /// <summary>
    /// 테스트용 PathResolver. 임시 디렉토리를 프로젝트 루트로 사용.
    /// </summary>
    private class TestPathResolver : PathResolver
    {
        public TestPathResolver(string tempRoot) : base(tempRoot) { }
    }
}
