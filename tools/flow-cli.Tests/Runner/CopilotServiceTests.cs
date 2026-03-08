using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class CopilotServiceTests
{
    [Fact]
    public void BuildImplementPrompt_IncludesConditionTestingAndManualVerificationInstructions()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-302", "{\"id\":\"F-302\"}", null);

        prompt.Should().Contain("각 condition별로 가능한 자동 테스트를 먼저 추가하거나 실행 가능한 상태로 맞추세요.");
        prompt.Should().Contain("자동 테스트를 만들거나 안정적으로 실행하기 어려운 condition은 추측으로 통과 처리하지 말고 수동 검증 대상으로 남기세요.");
        prompt.Should().Contain("docs/specs/F-302.json");
        prompt.Should().Contain("requiresManualVerification");
        prompt.Should().Contain("manualVerificationReason");
        prompt.Should().Contain("manualVerificationItems");
        prompt.Should().Contain("condition.status는 직접 `verified`나 `done`으로 바꾸지 마세요.");
    }

    [Fact]
    public void BuildImplementPrompt_WithPreviousReview_IncludesReviewAndTestingSummaryInstructions()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-303", "{\"id\":\"F-303\"}", "테스트 부족");

        prompt.Should().Contain("이전 구현 검토 결과");
        prompt.Should().Contain("자동 테스트로 다룬 condition ID 목록");
        prompt.Should().Contain("수동 검증으로 남긴 condition ID 목록과 이유");
        prompt.Should().Contain("추가하거나 실행한 테스트/검증 명령");
    }

    [Fact]
    public void ResolveReviewModel_AlwaysUsesGpt5Mini_ForSmallSpec()
    {
        var config = new RunnerConfig();
        var spec = new SpecNode
        {
            Id = "F-300",
            Title = "Small review",
            Description = "짧은 설명",
            Status = "needs-review",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-300-C1",
                    Status = "verified"
                }
            ]
        };

        var model = CopilotService.ResolveReviewModel(spec, config);

        model.Should().Be("gpt-5-mini");
    }

    [Fact]
    public void ResolveReviewModel_AlwaysUsesGpt5Mini_ForLargeSpec()
    {
        var config = new RunnerConfig();
        var spec = new SpecNode
        {
            Id = "F-301",
            Title = "Large review",
            Description = new string('y', 900),
            Status = "needs-review",
            CodeRefs = ["a.cs", "b.cs", "c.cs"],
            Conditions =
            [
                new SpecCondition { Id = "F-301-C1", Status = "verified", CodeRefs = ["1.cs", "2.cs"] },
                new SpecCondition { Id = "F-301-C2", Status = "verified", CodeRefs = ["3.cs"] },
                new SpecCondition { Id = "F-301-C3", Status = "verified" }
            ]
        };

        var model = CopilotService.ResolveReviewModel(spec, config);

        model.Should().Be("gpt-5-mini");
    }
}
