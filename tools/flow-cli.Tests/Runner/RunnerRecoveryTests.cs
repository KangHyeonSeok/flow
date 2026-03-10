using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class RunnerRecoveryTests : IDisposable
{
    private readonly string _tempDir;

    public RunnerRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-runner-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task RunOnceAsync_RequeuesStaleWorkingSpecWithCooldownAfterCrashRecovery()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();

        store.Create(new SpecNode
        {
            Id = "F-950",
            Title = "Recover stale working spec",
            Description = "runner crash recovery should requeue this spec",
            Status = "working",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-950-C1",
                    Description = "condition",
                    Status = "draft"
                }
            ],
            Metadata = new Dictionary<string, object>
            {
                ["runnerInstanceId"] = "runner-dead-123",
                ["runnerProcessId"] = 999999,
                ["runnerStartedAt"] = DateTime.UtcNow.AddMinutes(-10).ToString("o")
            }
        });

        var runner = new RunnerService(_tempDir, new RunnerConfig(), echoLogsToConsole: false);

        var results = await runner.RunOnceAsync();

        results.Should().BeEmpty("recovered spec should stay in cooldown queue instead of being retried in the same cycle");

        var recovered = store.Get("F-950");
        recovered.Should().NotBeNull();
        recovered!.Status.Should().Be("queued");
        recovered.Metadata.Should().NotBeNull();
        recovered.Metadata!["reviewDisposition"].ToString().Should().Be("execution-crash");
        recovered.Metadata["lastErrorType"].ToString().Should().Be("execution-crash");
        recovered.Metadata.Should().ContainKey("retryNotBefore");
        recovered.Metadata.Should().NotContainKey("runnerProcessId");
        recovered.Activity.Should().NotBeEmpty();
        recovered.Activity[^1].Outcome.Should().Be("requeue");
        recovered.Activity[^1].Kind.Should().Be("recovery");
    }
}