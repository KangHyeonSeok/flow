using System.Text.Json;
using FlowCLI.Models;
using FlowCLI.Services;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// BuildOrchestrator 서비스의 단위 테스트.
/// </summary>
public class BuildOrchestratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestPathResolver _paths;
    private readonly BuildModuleManager _moduleManager;
    private readonly ScriptRunner _scriptRunner;
    private readonly BuildOrchestrator _orchestrator;

    public BuildOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, ".flow"));

        _paths = new TestPathResolver(_tempDir);
        _moduleManager = new BuildModuleManager(_paths);
        _scriptRunner = new ScriptRunner();
        _orchestrator = new BuildOrchestrator(_paths, _moduleManager, _scriptRunner);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region DetectPlatform Tests

    [Fact]
    public void DetectPlatform_UnityProject_ReturnsUnity()
    {
        var projectDir = CreateProjectDir("unity-game");
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectDir, "ProjectSettings"));

        _orchestrator.DetectPlatform(projectDir).Should().Be("unity");
    }

    [Fact]
    public void DetectPlatform_PythonProject_ReturnsPython()
    {
        var projectDir = CreateProjectDir("py-app");
        File.WriteAllText(Path.Combine(projectDir, "requirements.txt"), "flask==2.0");

        _orchestrator.DetectPlatform(projectDir).Should().Be("python");
    }

    [Fact]
    public void DetectPlatform_NodeProject_ReturnsNode()
    {
        var projectDir = CreateProjectDir("node-app");
        File.WriteAllText(Path.Combine(projectDir, "package.json"), "{}");

        _orchestrator.DetectPlatform(projectDir).Should().Be("node");
    }

    [Fact]
    public void DetectPlatform_FlutterProject_ReturnsFlutter()
    {
        var projectDir = CreateProjectDir("flutter-app");
        File.WriteAllText(Path.Combine(projectDir, "pubspec.yaml"), "name: app");

        _orchestrator.DetectPlatform(projectDir).Should().Be("flutter");
    }

    [Fact]
    public void DetectPlatform_DotnetProject_ReturnsDotnet()
    {
        var projectDir = CreateProjectDir("dotnet-app");
        File.WriteAllText(Path.Combine(projectDir, "app.csproj"), "<Project />");

        _orchestrator.DetectPlatform(projectDir).Should().Be("dotnet");
    }

    [Fact]
    public void DetectPlatform_EmptyDir_ReturnsUnknown()
    {
        var projectDir = CreateProjectDir("empty");
        _orchestrator.DetectPlatform(projectDir).Should().Be("unknown");
    }

    [Fact]
    public void DetectPlatform_NonexistentDir_ReturnsUnknown()
    {
        _orchestrator.DetectPlatform("/nonexistent/path").Should().Be("unknown");
    }

    [Fact]
    public void DetectPlatform_UsesManifestDetectRules_WhenAvailable()
    {
        // 커스텀 detect 규칙이 있는 모듈 설치
        var modulePath = _paths.GetBuildModulePath("unity");
        Directory.CreateDirectory(modulePath);
        var manifest = new BuildManifest
        {
            Name = "unity",
            Version = "1.0.0",
            Description = "test",
            Detect = new BuildDetectRule
            {
                Files = new List<string> { "Assets/", "ProjectSettings/ProjectVersion.txt" }
            },
            Scripts = new BuildScripts { Build = "scripts/build.ps1" }
        };
        File.WriteAllText(
            Path.Combine(modulePath, "manifest.json"),
            JsonSerializer.Serialize(manifest));

        // Assets 디렉토리만 있고 ProjectVersion.txt가 없는 프로젝트
        var projectDir = CreateProjectDir("partial-unity");
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectDir, "ProjectSettings"));
        // ProjectVersion.txt 없음 → manifest 규칙에 따라 실패

        // 기본 규칙에서는 "Assets" 디렉토리만으로 unity 감지
        // 하지만 manifest 규칙은 모든 파일 존재를 요구하므로, ProjectVersion.txt 없으면 감지 실패
        // → 기본 규칙 fallback은 없음 (manifest가 우선)
        _orchestrator.DetectPlatform(projectDir).Should().NotBe("unity");
    }

    #endregion

    #region Execute Tests

    [Fact]
    public void Execute_SuccessfulStep_ReturnsSuccess()
    {
        InstallModuleWithScript("testplat", "build", @"
            param([string]$ProjectPath)
            Write-Output 'build done'
        ");

        var result = _orchestrator.Execute(
            _tempDir, "testplat", new[] { "build" });

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(1);
        result.Steps[0].Action.Should().Be("build");
        result.Steps[0].Success.Should().BeTrue();
        result.Message.Should().Contain("모든 단계 완료");
    }

    [Fact]
    public void Execute_MultipleSteps_ExecutesInOrder()
    {
        InstallModuleWithScript("testplat", "lint", "Write-Output 'lint ok'");
        InstallModuleWithScript("testplat", "build", "Write-Output 'build ok'", append: true);

        var result = _orchestrator.Execute(
            _tempDir, "testplat", new[] { "lint", "build" });

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(2);
        result.Steps[0].Action.Should().Be("lint");
        result.Steps[1].Action.Should().Be("build");
    }

    [Fact]
    public void Execute_FailFast_StopsOnFailure()
    {
        InstallModuleWithScript("testplat", "lint", @"
            Write-Error 'lint error'
            exit 1
        ");
        InstallModuleWithScript("testplat", "build", "Write-Output 'build ok'", append: true);

        var result = _orchestrator.Execute(
            _tempDir, "testplat", new[] { "lint", "build" });

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(1); // build는 실행되지 않음
        result.Steps[0].Action.Should().Be("lint");
        result.Steps[0].Success.Should().BeFalse();
        result.Message.Should().Contain("lint");
    }

    [Fact]
    public void Execute_MissingScript_ReturnsError()
    {
        // 모듈은 있지만 특정 단계의 스크립트가 없음
        var modulePath = _paths.GetBuildModulePath("noscript");
        Directory.CreateDirectory(modulePath);
        var manifest = new BuildManifest
        {
            Name = "noscript",
            Version = "1.0.0",
            Description = "test",
            Scripts = new BuildScripts { Build = "scripts/build.ps1" } // lint 없음
        };
        File.WriteAllText(
            Path.Combine(modulePath, "manifest.json"),
            JsonSerializer.Serialize(manifest));

        var result = _orchestrator.Execute(
            _tempDir, "noscript", new[] { "lint" });

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(1);
        result.Steps[0].Success.Should().BeFalse();
        result.Message.Should().Contain("스크립트 없음");
    }

    [Fact]
    public void Execute_Timeout_ReturnsTimeoutError()
    {
        InstallModuleWithScript("testplat", "build", @"
            Start-Sleep -Seconds 30
        ");

        var result = _orchestrator.Execute(
            _tempDir, "testplat", new[] { "build" },
            timeoutMs: 2000);

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(1);
        result.Message.Should().Contain("타임아웃");
    }

    #endregion

    #region Helpers

    private string CreateProjectDir(string name)
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void InstallModuleWithScript(string platform, string action, string scriptBody, bool append = false)
    {
        var modulePath = _paths.GetBuildModulePath(platform);
        var scriptsDir = Path.Combine(modulePath, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // manifest 작성 (append 시에는 기존 manifest에 스크립트 추가)
        BuildManifest manifest;
        var manifestPath = Path.Combine(modulePath, "manifest.json");

        if (append && File.Exists(manifestPath))
        {
            manifest = JsonSerializer.Deserialize<BuildManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        else
        {
            manifest = new BuildManifest
            {
                Name = platform,
                Version = "1.0.0",
                Description = "test module",
                Scripts = new BuildScripts()
            };
        }

        var scriptFileName = $"{action}.ps1";
        var scriptPath = $"scripts/{scriptFileName}";

        switch (action.ToLowerInvariant())
        {
            case "lint": manifest.Scripts!.Lint = scriptPath; break;
            case "build": manifest.Scripts!.Build = scriptPath; break;
            case "test": manifest.Scripts!.Test = scriptPath; break;
            case "run": manifest.Scripts!.Run = scriptPath; break;
        }

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(scriptsDir, scriptFileName), scriptBody);
    }

    private class TestPathResolver : PathResolver
    {
        public TestPathResolver(string tempRoot) : base(tempRoot) { }
    }

    #endregion
}
