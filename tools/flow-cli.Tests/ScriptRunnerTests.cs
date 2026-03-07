using System.Text.Json;
using FlowCLI.Services;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// ScriptRunner 서비스의 단위 테스트.
/// </summary>
public class ScriptRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScriptRunner _runner;

    public ScriptRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _runner = new ScriptRunner();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region FindPowerShell Tests

    [Fact]
    public void FindPowerShell_ReturnsNonNull()
    {
        var pwsh = _runner.FindPowerShell();
        pwsh.Should().NotBeNull("PowerShell should be available on the test system");
    }

    #endregion

    #region RunScript Tests

    [Fact]
    public void RunScript_SimpleEcho_Success()
    {
        var script = CreateScript(@"
            Write-Output 'hello world'
        ");

        var result = _runner.RunScript(script);

        result.IsSuccess.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("hello world");
        result.Error.Should().BeNull();
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public void RunScript_WithParameters_PassesCorrectly()
    {
        var script = CreateScript(@"
            param([string]$ProjectPath, [string]$Config)
            Write-Output ""path=$ProjectPath config=$Config""
        ");

        var parameters = new Dictionary<string, string>
        {
            ["ProjectPath"] = "/my/project",
            ["Config"] = "debug"
        };

        var result = _runner.RunScript(script, parameters);

        result.IsSuccess.Should().BeTrue();
        result.Stdout.Should().Contain("path=/my/project");
        result.Stdout.Should().Contain("config=debug");
    }

    [Fact]
    public void RunScript_JsonOutput_CapturesCorrectly()
    {
        var script = CreateScript(@"
            $result = @{
                success = $true
                command = 'build'
                data = @{ platform = 'unity'; action = 'build' }
                message = 'Build completed'
            } | ConvertTo-Json -Compress
            Write-Output $result
        ");

        var result = _runner.RunScript(script);

        result.IsSuccess.Should().BeTrue();
        result.Stdout.Should().NotBeNull();

        var json = ScriptRunner.TryParseJson(result.Stdout);
        json.Should().NotBeNull();
        json!.Value.GetProperty("success").GetBoolean().Should().BeTrue();
        json.Value.GetProperty("command").GetString().Should().Be("build");
    }

    [Fact]
    public void RunScript_ScriptFails_ReturnsError()
    {
        var script = CreateScript(@"
            Write-Error 'Something went wrong'
            exit 1
        ");

        var result = _runner.RunScript(script);

        result.IsSuccess.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RunScript_ScriptNotFound_ReturnsError()
    {
        var result = _runner.RunScript("/nonexistent/script.ps1");

        result.IsSuccess.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.Error.Should().Contain("스크립트를 찾을 수 없습니다");
    }

    [Fact]
    public void RunScript_Timeout_KillsProcess()
    {
        var script = CreateScript(@"
            Start-Sleep -Seconds 30
            Write-Output 'should not reach here'
        ");

        var result = _runner.RunScript(script, timeoutMs: 2000);

        result.IsSuccess.Should().BeFalse();
        result.TimedOut.Should().BeTrue();
        result.Error.Should().Contain("타임아웃");
    }

    [Fact]
    public void RunScript_StderrOutput_CapturedSeparately()
    {
        var script = CreateScript(@"
            Write-Output 'stdout message'
            Write-Error 'stderr message'
            exit 0
        ");

        var result = _runner.RunScript(script);

        result.Stdout.Should().Contain("stdout message");
        result.Stderr.Should().Contain("stderr message");
    }

    #endregion

    #region TryParseJson Tests

    [Fact]
    public void TryParseJson_ValidJson_ReturnsElement()
    {
        var input = """{"success":true,"command":"build","message":"ok"}""";
        var result = ScriptRunner.TryParseJson(input);

        result.Should().NotBeNull();
        result!.Value.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TryParseJson_JsonWithPrefixText_ExtractsJson()
    {
        var input = "Loading modules...\nInitializing...\n{\"success\":true,\"command\":\"build\"}";
        var result = ScriptRunner.TryParseJson(input);

        result.Should().NotBeNull();
        result!.Value.GetProperty("command").GetString().Should().Be("build");
    }

    [Fact]
    public void TryParseJson_NullInput_ReturnsNull()
    {
        ScriptRunner.TryParseJson(null).Should().BeNull();
    }

    [Fact]
    public void TryParseJson_EmptyInput_ReturnsNull()
    {
        ScriptRunner.TryParseJson("").Should().BeNull();
        ScriptRunner.TryParseJson("  ").Should().BeNull();
    }

    [Fact]
    public void TryParseJson_NoJson_ReturnsNull()
    {
        ScriptRunner.TryParseJson("just plain text output").Should().BeNull();
    }

    #endregion

    private string CreateScript(string body)
    {
        var path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, body);
        return path;
    }
}
