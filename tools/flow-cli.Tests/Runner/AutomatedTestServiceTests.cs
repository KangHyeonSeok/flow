using FlowCLI.Services.Runner;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class AutomatedTestServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AutomatedTestServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-auto-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void ResolvePlan_WithDotnetSolution_ReturnsDotnetPlan()
    {
        var solutionPath = Path.Combine(_tempDir, "flow.sln");
        File.WriteAllText(solutionPath, "Microsoft Visual Studio Solution File, Format Version 12.00");

        var plan = AutomatedTestService.ResolvePlan(_tempDir, "F-030", Path.Combine(_tempDir, "docs", "evidence"));

        plan.Should().NotBeNull();
        plan!.Platform.Should().Be("dotnet");
        plan.FileName.Should().Be("dotnet");
        plan.Arguments.Should().Contain("test ");
        plan.Arguments.Should().Contain(solutionPath);
        plan.WorkingDirectory.Should().Be(_tempDir);
        plan.ResultFilePath.Should().Contain(Path.Combine("F-030", "runner-tests"));
        plan.ResultFilePath.Should().EndWith("runner-tests.trx");
    }

    [Fact]
    public void ResolvePlan_WithoutSupportedProject_ReturnsNull()
    {
        var plan = AutomatedTestService.ResolvePlan(_tempDir, "F-030", Path.Combine(_tempDir, "docs", "evidence"));

        plan.Should().BeNull();
    }
}