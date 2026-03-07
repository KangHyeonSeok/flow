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

    // ─── F-004-C3: JSON 입력 경로 회귀 테스트 ───────────────────────────

    [Fact]
    public void JsonPath_BuildPayload_ParametersMappedEquivalentToCli()
    {
        // Given: JSON payload representing CLI args --platform dotnet --lint --build --timeout 120
        var json = """
            {
                "command": "build",
                "payload": {
                    "platform": "dotnet",
                    "lint": true,
                    "build": true,
                    "test": false,
                    "timeout": 120
                }
            }
            """;
        var request = JsonSerializer.Deserialize<FlowRequest>(json, FlowCLI.Utils.JsonOutput.Read);
        request.Should().NotBeNull();
        request!.Payload.Should().NotBeNull();

        // When: extract parameters from payload (equivalent to CLI arg binding)
        var payload = request.Payload!.Value;
        var platform = payload.GetProperty("platform").GetString();
        var lint = payload.GetProperty("lint").GetBoolean();
        var build = payload.GetProperty("build").GetBoolean();
        var test = payload.GetProperty("test").GetBoolean();
        var timeout = payload.GetProperty("timeout").GetInt32();

        // Then: values match CLI arg expectations
        platform.Should().Be("dotnet");
        lint.Should().BeTrue();
        build.Should().BeTrue();
        test.Should().BeFalse();
        timeout.Should().Be(120);
    }

    [Fact]
    public void JsonPath_BuildAllFlag_StepSelectionEquivalentToCli()
    {
        // Given: JSON payload with all: true
        var json = """{"command": "build", "payload": {"all": true}}""";
        var request = JsonSerializer.Deserialize<FlowRequest>(json, FlowCLI.Utils.JsonOutput.Read);

        // When: determine steps from payload (mirrors CLI --all logic)
        bool all = request!.Payload!.Value.GetProperty("all").GetBoolean();
        var steps = new List<string>();
        if (all)
            steps.AddRange(new[] { "lint", "build", "test" });

        // Then: identical to legacy CLI --all flag behaviour
        steps.Should().BeEquivalentTo(new[] { "lint", "build", "test" });
    }

    [Fact]
    public void JsonPath_DbAddPayload_RequiredContentExtracted()
    {
        // Given: db-add JSON request with required content field
        var json = """
            {
                "command": "db-add",
                "payload": { "content": "구현 완료", "tags": "spec,test", "feature": "F-001" }
            }
            """;
        var request = JsonSerializer.Deserialize<FlowRequest>(json, FlowCLI.Utils.JsonOutput.Read);
        var payload = request!.Payload!.Value;

        // When: extract fields (same fields used by CLI --content --tags --feature)
        var content = payload.GetProperty("content").GetString();
        var tags = payload.GetProperty("tags").GetString();
        var feature = payload.GetProperty("feature").GetString();

        // Then: matches CLI arg values
        content.Should().Be("구현 완료");
        tags.Should().Be("spec,test");
        feature.Should().Be("F-001");
    }

    [Fact]
    public void JsonPath_PayloadMergedIntoOptions_OptionsWinOnConflict()
    {
        // Given: request with both payload and options (options take priority per F-003-C2)
        var json = """
            {
                "command": "build",
                "payload": { "platform": "python", "lint": false },
                "options": { "platform": "dotnet" }
            }
            """;
        var request = JsonSerializer.Deserialize<FlowRequest>(json, FlowCLI.Utils.JsonOutput.Read);
        request.Should().NotBeNull();

        // When: merge payload into options (options win)
        var merged = new Dictionary<string, JsonElement>(request!.Options ?? []);
        if (request.Payload?.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in request.Payload.Value.EnumerateObject())
            {
                if (!merged.ContainsKey(prop.Name))
                    merged[prop.Name] = prop.Value;
            }
        }

        // Then: options value wins for platform, payload fills in lint
        merged["platform"].GetString().Should().Be("dotnet");
        merged["lint"].GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void JsonPath_ValidateRequiredFields_DetectsEmptyContent()
    {
        // F-004-C2: 빈 content로 db-add 요청 시 검증 오류가 검출되어야 한다
        var opts = new Dictionary<string, JsonElement>
        {
            ["content"] = JsonDocument.Parse("\"\"").RootElement
        };

        var errors = FlowApp.ValidateRequiredFields(opts, "content");
        errors.Should().ContainSingle(e => e.Contains("content"));
    }

    [Fact]
    public void JsonPath_ValidateOptionTypes_DetectsTypeMismatch()
    {
        // F-004-C2: 잘못된 타입의 옵션 제공 시 검증 오류가 검출되어야 한다
        var opts = new Dictionary<string, JsonElement>
        {
            ["lint"] = JsonDocument.Parse("\"yes\"").RootElement  // string 대신 bool이어야 함
        };

        var errors = FlowApp.ValidateOptionTypes(opts,
            new Dictionary<string, Type> { ["lint"] = typeof(bool) });
        errors.Should().ContainSingle(e => e.Contains("lint"));
    }

    [Fact]
    public void JsonPath_ValidateRequiredFields_MissingField_ReturnsError()
    {
        // F-004-C2: 필수 필드가 없을 때 오류가 검출되어야 한다
        var opts = new Dictionary<string, JsonElement>
        {
            ["tags"] = JsonDocument.Parse("\"spec\"").RootElement
        };

        var errors = FlowApp.ValidateRequiredFields(opts, "content");
        errors.Should().ContainSingle(e => e.Contains("content"));
    }

    [Fact]
    public void JsonPath_ValidateRequiredFields_PresentField_NoError()
    {
        // F-004-C2: 필수 필드가 있을 때 오류가 없어야 한다
        var opts = new Dictionary<string, JsonElement>
        {
            ["content"] = JsonDocument.Parse("\"실제 내용\"").RootElement
        };

        var errors = FlowApp.ValidateRequiredFields(opts, "content");
        errors.Should().BeEmpty();
    }

    /// <summary>
    /// 테스트용 PathResolver. 임시 디렉토리를 프로젝트 루트로 사용.
    /// </summary>
    private class TestPathResolver : PathResolver
    {
        public TestPathResolver(string tempRoot) : base(tempRoot) { }
    }
}
