using FlowCLI.Models;
using FlowCLI.Services.Runner;
using FluentAssertions;

namespace FlowCLI.Tests;

public class RunnerConfigDefaultsTests
{
    [Fact]
    public void FlowConfig_DefaultCopilotModel_IsGPT54()
    {
        var config = new FlowConfig();

        config.CopilotModel.Should().Be("gpt-5.4");
    }

    [Fact]
    public void RunnerConfig_DefaultCopilotModel_IsGPT54()
    {
        var config = new RunnerConfig();

        config.CopilotModel.Should().Be("gpt-5.4");
    }

    [Fact]
    public void RunnerConfig_DefaultReviewPollIntervalSeconds_IsThirty()
    {
        var config = new RunnerConfig();

        config.ReviewPollIntervalSeconds.Should().Be(30);
    }

    /// <summary>
    /// F-031-C5: MaxReschedulesPerPoll 기본값은 10이어야 한다.
    /// busy-wait 방지를 위한 cycle 상한 기본값을 검증한다.
    /// </summary>
    [Fact]
    public void RunnerConfig_DefaultMaxReschedulesPerPoll_IsTen()
    {
        var config = new RunnerConfig();

        config.MaxReschedulesPerPoll.Should().Be(10);
    }

    /// <summary>
    /// F-031-C1: SpecWorkResult.TriggeredReschedule 기본값은 false여야 한다.
    /// 상태 전환 없는 일반 결과가 의도치 않은 재스케줄을 유발하지 않음을 검증한다.
    /// </summary>
    [Fact]
    public void SpecWorkResult_DefaultTriggeredReschedule_IsFalse()
    {
        var result = new SpecWorkResult { SpecId = "F-001" };

        result.TriggeredReschedule.Should().BeFalse();
    }
}