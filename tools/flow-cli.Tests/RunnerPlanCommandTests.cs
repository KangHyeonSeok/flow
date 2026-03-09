using System.Text.Json;
using FlowCLI.Services.SpecGraph;
using FlowCLI.Utils;
using FluentAssertions;

namespace FlowCLI.Tests;

[Collection("CommandGlobalState")]
public class RunnerPlanCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalCwd;
    private readonly TextWriter _originalOut;
    private readonly StringWriter _capturedOut;

    public RunnerPlanCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-runner-plan-{Guid.NewGuid():N}");
        _originalCwd = Directory.GetCurrentDirectory();
        _originalOut = Console.Out;
        _capturedOut = new StringWriter();

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, ".flow"));
        Directory.SetCurrentDirectory(_tempDir);
        Console.SetOut(_capturedOut);
        Environment.ExitCode = 0;
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Directory.SetCurrentDirectory(_originalCwd);
        Environment.ExitCode = 0;

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void RunnerPlan_PrintsReadyOrderAndBlockedReasons_WithoutMutatingSelectionMetadata()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();

        store.Create(new SpecNode
        {
            Id = "F-005",
            Title = "Windows Copilot path fix",
            Description = "ready candidate",
            Status = "queued",
            NodeType = "task"
        });

        store.Create(new SpecNode
        {
            Id = "F-002",
            Title = "Calculator CLI",
            Description = "blocked by dependency",
            Status = "queued",
            NodeType = "feature",
            Dependencies = ["F-005"]
        });

        store.Create(new SpecNode
        {
            Id = "F-003",
            Title = "Banner CLI",
            Description = "blocked by dependency",
            Status = "queued",
            NodeType = "feature",
            Dependencies = ["F-005"]
        });

        var app = new FlowApp();
        app.RunnerPlan(pretty: true);

        Environment.ExitCode.Should().Be(0);

        using var json = JsonDocument.Parse(ReadCapturedOutput());
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("command").GetString().Should().Be("runner-plan");
        root.GetProperty("message").GetString().Should().Be("다음 후보: F-005");

        var data = root.GetProperty("data");
        data.GetProperty("next_spec_id").GetString().Should().Be("F-005");
        data.GetProperty("ready_count").GetInt32().Should().Be(1);
        data.GetProperty("blocked_count").GetInt32().Should().Be(2);

        var readySpecs = data.GetProperty("ready_specs");
        readySpecs.GetArrayLength().Should().Be(1);
        readySpecs[0].GetProperty("spec_id").GetString().Should().Be("F-005");
        readySpecs[0].GetProperty("rank").GetInt32().Should().Be(1);
        readySpecs[0].GetProperty("is_fallback").GetBoolean().Should().BeTrue();

        var blockedSpecs = data.GetProperty("blocked_specs");
        blockedSpecs.GetArrayLength().Should().Be(2);
        blockedSpecs[0].GetProperty("reason").GetString().Should().Be("unmet-dependencies");
        blockedSpecs[0].GetProperty("unmet_dependencies")[0].GetString().Should().Be("F-005");

        store.Get("F-005")!.Metadata.Should().BeNullOrEmpty();
    }

    private string ReadCapturedOutput() => _capturedOut.ToString().Trim();
}