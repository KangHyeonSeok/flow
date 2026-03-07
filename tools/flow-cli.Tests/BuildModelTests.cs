using System.Text.Json;
using FlowCLI.Models;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// BuildManifest 및 BuildResult 모델의 JSON 직렬화/역직렬화 테스트.
/// </summary>
public class BuildModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #region BuildManifest Tests

    [Fact]
    public void BuildManifest_Deserialize_AllFields()
    {
        var json = """
        {
          "name": "unity",
          "version": "1.0.0",
          "description": "Unity build module for flow",
          "detect": {
            "files": ["Assets/", "ProjectSettings/ProjectVersion.txt"],
            "description": "Unity 프로젝트 감지 조건"
          },
          "scripts": {
            "lint": "scripts/lint.ps1",
            "build": "scripts/build.ps1",
            "test": "scripts/test.ps1",
            "run": "scripts/run.ps1"
          },
          "args": {
            "build": ["--target", "--buildPath"],
            "test": ["--mode", "--filter"]
          }
        }
        """;

        var manifest = JsonSerializer.Deserialize<BuildManifest>(json, JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.Name.Should().Be("unity");
        manifest.Version.Should().Be("1.0.0");
        manifest.Description.Should().Be("Unity build module for flow");

        manifest.Detect.Should().NotBeNull();
        manifest.Detect!.Files.Should().HaveCount(2);
        manifest.Detect.Files.Should().Contain("Assets/");
        manifest.Detect.Files.Should().Contain("ProjectSettings/ProjectVersion.txt");
        manifest.Detect.Description.Should().Be("Unity 프로젝트 감지 조건");

        manifest.Scripts.Should().NotBeNull();
        manifest.Scripts!.Lint.Should().Be("scripts/lint.ps1");
        manifest.Scripts.Build.Should().Be("scripts/build.ps1");
        manifest.Scripts.Test.Should().Be("scripts/test.ps1");
        manifest.Scripts.Run.Should().Be("scripts/run.ps1");

        manifest.Args.Should().NotBeNull();
        manifest.Args!.Should().ContainKey("build");
        manifest.Args["build"].Should().Contain("--target");
        manifest.Args["build"].Should().Contain("--buildPath");
        manifest.Args!.Should().ContainKey("test");
        manifest.Args["test"].Should().Contain("--mode");
    }

    [Fact]
    public void BuildManifest_Deserialize_MinimalFields()
    {
        var json = """
        {
          "name": "python",
          "version": "0.1.0",
          "description": "Python build module",
          "scripts": {
            "build": "scripts/build.ps1"
          }
        }
        """;

        var manifest = JsonSerializer.Deserialize<BuildManifest>(json, JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.Name.Should().Be("python");
        manifest.Detect.Should().BeNull();
        manifest.Scripts!.Lint.Should().BeNull();
        manifest.Scripts.Test.Should().BeNull();
        manifest.Scripts.Run.Should().BeNull();
        manifest.Args.Should().BeNull();
    }

    [Fact]
    public void BuildManifest_Serialize_OmitsNullFields()
    {
        var manifest = new BuildManifest
        {
            Name = "node",
            Version = "1.0.0",
            Description = "Node build module",
            Scripts = new BuildScripts { Build = "scripts/build.ps1" }
        };

        var json = JsonSerializer.Serialize(manifest, JsonOptions);

        json.Should().NotContain("\"args\"");
    }

    [Fact]
    public void BuildManifest_RoundTrip()
    {
        var original = new BuildManifest
        {
            Name = "dotnet",
            Version = "2.0.0",
            Description = "Dotnet build module",
            Detect = new BuildDetectRule
            {
                Files = new List<string> { "*.sln", "*.csproj" },
                Description = ".NET 프로젝트 감지"
            },
            Scripts = new BuildScripts
            {
                Lint = "scripts/lint.ps1",
                Build = "scripts/build.ps1",
                Test = "scripts/test.ps1"
            },
            Args = new Dictionary<string, List<string>>
            {
                ["build"] = new() { "--configuration", "--runtime" }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<BuildManifest>(json, JsonOptions);

        restored.Should().NotBeNull();
        restored!.Name.Should().Be(original.Name);
        restored.Version.Should().Be(original.Version);
        restored.Detect!.Files.Should().BeEquivalentTo(original.Detect.Files);
        restored.Scripts!.Build.Should().Be(original.Scripts.Build);
        restored.Args!["build"].Should().BeEquivalentTo(original.Args["build"]);
    }

    #endregion

    #region BuildResult Tests

    [Fact]
    public void BuildStepResult_Serialize_FlowJsonFormat()
    {
        var step = new BuildStepResult
        {
            Success = true,
            Action = "build",
            Platform = "unity",
            DurationMs = 12345,
            OutputPath = "Builds/Win64/game.exe",
            ExitCode = 0
        };

        var json = JsonSerializer.Serialize(step, JsonOptions);

        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"action\":\"build\"");
        json.Should().Contain("\"platform\":\"unity\"");
        json.Should().Contain("\"duration_ms\":12345");
        json.Should().Contain("\"output_path\":\"Builds/Win64/game.exe\"");
        json.Should().Contain("\"exit_code\":0");
        // null fields should be omitted
        json.Should().NotContain("\"stdout\"");
        json.Should().NotContain("\"stderr\"");
        json.Should().NotContain("\"details\"");
    }

    [Fact]
    public void BuildStepResult_FailedStep_IncludesStderr()
    {
        var step = new BuildStepResult
        {
            Success = false,
            Action = "test",
            Platform = "unity",
            DurationMs = 5000,
            ExitCode = 1,
            Stderr = "Test failed: NullReferenceException"
        };

        var json = JsonSerializer.Serialize(step, JsonOptions);

        json.Should().Contain("\"success\":false");
        json.Should().Contain("\"stderr\":\"Test failed: NullReferenceException\"");
    }

    [Fact]
    public void BuildResult_MultipleSteps_Aggregation()
    {
        var result = new BuildResult
        {
            Success = true,
            Platform = "unity",
            ProjectPath = "/projects/my-game",
            TotalDurationMs = 25000,
            Steps = new List<BuildStepResult>
            {
                new() { Success = true, Action = "lint", Platform = "unity", DurationMs = 3000, ExitCode = 0 },
                new() { Success = true, Action = "build", Platform = "unity", DurationMs = 15000, ExitCode = 0, OutputPath = "Builds/game.exe" },
                new() { Success = true, Action = "test", Platform = "unity", DurationMs = 7000, ExitCode = 0 }
            },
            Message = "All steps completed successfully"
        };

        result.Steps.Should().HaveCount(3);
        result.Steps.Should().AllSatisfy(s => s.Success.Should().BeTrue());
        result.TotalDurationMs.Should().Be(25000);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        json.Should().Contain("\"steps\"");
        json.Should().Contain("\"total_duration_ms\":25000");
    }

    [Fact]
    public void BuildResult_PartialFailure()
    {
        var result = new BuildResult
        {
            Success = false,
            Platform = "python",
            ProjectPath = "/projects/my-app",
            TotalDurationMs = 8000,
            Steps = new List<BuildStepResult>
            {
                new() { Success = true, Action = "lint", Platform = "python", DurationMs = 2000, ExitCode = 0 },
                new() { Success = false, Action = "build", Platform = "python", DurationMs = 6000, ExitCode = 1, Stderr = "SyntaxError" }
            },
            Message = "Build failed at step: build"
        };

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
        result.Steps[0].Success.Should().BeTrue();
        result.Steps[1].Success.Should().BeFalse();
    }

    [Fact]
    public void BuildResult_Deserialize_FromJson()
    {
        var json = """
        {
          "success": true,
          "platform": "unity",
          "project_path": "/games/demo",
          "total_duration_ms": 10000,
          "steps": [
            {
              "success": true,
              "action": "build",
              "platform": "unity",
              "duration_ms": 10000,
              "output_path": "Builds/demo.exe",
              "exit_code": 0
            }
          ],
          "message": "Build completed"
        }
        """;

        var result = JsonSerializer.Deserialize<BuildResult>(json, JsonOptions);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Platform.Should().Be("unity");
        result.ProjectPath.Should().Be("/games/demo");
        result.Steps.Should().HaveCount(1);
        result.Steps[0].OutputPath.Should().Be("Builds/demo.exe");
    }

    #endregion
}
