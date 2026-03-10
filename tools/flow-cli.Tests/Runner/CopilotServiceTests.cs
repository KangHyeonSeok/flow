using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class CopilotServiceTests
{
    [Fact]
    public void BuildImplementPrompt_IncludesCoreInstructions()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-302", "{\"id\":\"F-302\"}", null);

        prompt.Should().Contain("TDD");
        prompt.Should().Contain("./flow.ps1 spec-get F-302");
        prompt.Should().Contain("condition.status");
        prompt.Should().Contain("F-302");
    }

    [Fact]
    public void BuildImplementPrompt_WithPreviousReview_IncludesReviewSection()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-303", "{\"id\":\"F-303\"}", "테스트 부족");

        prompt.Should().Contain("이전 리뷰 결과");
        prompt.Should().Contain("테스트 부족");
    }

    [Fact]
    public void BuildReviewPrompt_IncludesFlowScriptAndSchema()
    {
        var prompt = CopilotService.BuildReviewPrompt("F-304", "{\"id\":\"F-304\"}", "review context", "reviewer-1");

        prompt.Should().Contain("./flow.ps1 spec-get F-304");
        prompt.Should().Contain("spec-append-review F-304");
        prompt.Should().Contain("스펙 JSON 파일을 직접 수정하지 마세요");
        prompt.Should().Contain("verifiedConditionIds");
        prompt.Should().Contain("사용자 결정이 필요한 항목만 `questions`에 넣으세요");
        prompt.Should().Contain("구현 전에 개발자가 확인하거나 재현할 수 있는 항목은 `additionalInformationRequests`에 넣으세요");
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
