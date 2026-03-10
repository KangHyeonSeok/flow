using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class CopilotServiceTests
{
    [Fact]
    public void BuildImplementPrompt_IncludesConditionTestingAndManualVerificationInstructions()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-302", "{\"id\":\"F-302\"}", "~/.flow/flow/specs/F-302.json", null);

        prompt.Should().Contain("스펙의 source of truth는 직접 파일 경로나 프롬프트에 포함된 JSON이 아니라 `flow.ps1 spec-get` 결과입니다.");
        prompt.Should().Contain("현재 작업 디렉터리는 runner worktree일 수 있으므로, 상위 경로를 올라가 `flow.ps1`를 찾으세요.");
        prompt.Should().Contain("& $flow spec-get F-302");
        prompt.Should().Contain("flow.ps1 not found in parent chain");
        prompt.Should().Contain("스펙의 각 condition을 읽고, 자동 테스트가 가능한 condition부터 테스트를 먼저 작성하세요.");
        prompt.Should().Contain("자동 테스트를 만들거나 안정적으로 실행하기 어려운 condition은 수동 검증 대상으로 남기세요.");
        prompt.Should().Contain("flow.ps1 spec-get F-302");
        prompt.Should().Contain("~/.flow/flow/specs/F-302.json");
        prompt.Should().Contain("requiresManualVerification");
        prompt.Should().Contain("manualVerificationReason");
        prompt.Should().Contain("manualVerificationItems");
        prompt.Should().Contain("spec-record-condition-review F-302");
        prompt.Should().Contain("condition.status는 직접 `verified`나 `done`으로 바꾸지 마세요.");
    }

    [Fact]
    public void BuildImplementPrompt_WithPreviousReview_IncludesReviewAndTestingSummaryInstructions()
    {
        var prompt = CopilotService.BuildImplementPrompt("F-303", "{\"id\":\"F-303\"}", "~/.flow/flow/specs/F-303.json", "테스트 부족");

        prompt.Should().Contain("이전 구현 검토 결과");
        prompt.Should().Contain("자동 테스트로 다룬 condition ID 목록");
        prompt.Should().Contain("수동 검증으로 남긴 condition ID 목록과 이유");
        prompt.Should().Contain("추가하거나 실행한 테스트/검증 명령");
        prompt.Should().Contain("수집한 테스트 결과 파일, 로그, 스크린샷 등 evidence 경로");
    }

    [Fact]
    public void BuildReviewPrompt_InstructsAgentToLoadSpecViaFlowScript()
    {
        var prompt = CopilotService.BuildReviewPrompt("F-304", "{\"id\":\"F-304\"}", "review context", "reviewer-1");

        prompt.Should().Contain("검토를 시작하기 전에 아래 조회 규칙대로 `flow.ps1 spec-get F-304`를 실행해 최신 스펙을 다시 읽습니다.");
        prompt.Should().Contain("스펙의 source of truth는 직접 파일 경로나 프롬프트에 포함된 JSON이 아니라 `flow.ps1 spec-get` 결과입니다.");
        prompt.Should().Contain("& $flow spec-get F-304 --json --pretty");
        prompt.Should().Contain("스펙 JSON 파일을 직접 수정하지 않습니다.");
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
