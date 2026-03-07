using FlowCLI.Models;
using FlowCLI.Services.Runner;
using FluentAssertions;

namespace FlowCLI.Tests;

public class RunnerConfigDefaultsTests
{
    [Fact]
    public void FlowConfig_DefaultCopilotModel_IsClaudeSonnet46()
    {
        var config = new FlowConfig();

        config.CopilotModel.Should().Be("claude-sonnet-4.6");
    }

    [Fact]
    public void RunnerConfig_DefaultCopilotModel_IsClaudeSonnet46()
    {
        var config = new RunnerConfig();

        config.CopilotModel.Should().Be("claude-sonnet-4.6");
    }
}