using System.Text;
using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowCore.Agents.Cli;

/// <summary>AgentInput → 프롬프트 텍스트 변환</summary>
public sealed class PromptBuilder
{
    private const string ResponseFormatInstruction = """

        ## 응답 형식

        반드시 아래 형식의 JSON 블록 1개를 응답에 포함하세요:

        ```json
        {
          "proposedEvent": "<FlowEvent 이름>",
          "summary": "<요약 (한글 1-2문장)>",
          "proposedReviewRequest": null
        }
        ```

        proposedReviewRequest가 필요한 경우:
        ```json
        {
          "proposedReviewRequest": {
            "summary": "<리뷰 요약>",
            "questions": ["질문1", "질문2"],
            "options": [
              { "id": "approve", "label": "승인", "description": "현재 방향으로 진행" },
              { "id": "reject", "label": "반려", "description": "피드백과 함께 재작업 요청" }
            ]
          }
        }
        ```
        """;

    public string BuildPrompt(AgentInput input, AgentRole role)
    {
        var sb = new StringBuilder();

        // 공통 envelope
        sb.AppendLine("# Spec 정보");
        sb.AppendLine(JsonSerializer.Serialize(input.Spec, FlowJsonOptions.Default));
        sb.AppendLine();

        if (input.RecentActivity.Count > 0)
        {
            sb.AppendLine("# 최근 활동 이력");
            foreach (var evt in input.RecentActivity)
                sb.AppendLine($"- [{evt.Timestamp:yyyy-MM-dd HH:mm}] {evt.Action}: {evt.Message}");
            sb.AppendLine();
        }

        if (input.ReviewRequests.Count > 0)
        {
            sb.AppendLine("# Review Requests");
            sb.AppendLine(JsonSerializer.Serialize(input.ReviewRequests, FlowJsonOptions.Default));
            sb.AppendLine();
        }

        if (input.Assignment.Worktree is { } wt)
        {
            sb.AppendLine("# 작업 디렉토리");
            sb.AppendLine($"경로: {wt.Path}");
            if (wt.Branch is not null)
                sb.AppendLine($"브랜치: {wt.Branch}");
            sb.AppendLine("이 디렉토리의 코드와 테스트 결과를 기반으로 검증하세요.");
            sb.AppendLine();
        }

        // Role별 지시사항
        sb.AppendLine(GetRoleInstruction(input, role));
        sb.AppendLine(ResponseFormatInstruction);

        return sb.ToString();
    }

    private static string GetRoleInstruction(AgentInput input, AgentRole role)
    {
        return role switch
        {
            AgentRole.SpecValidator => GetSpecValidatorInstruction(input),
            AgentRole.Planner => "# 역할: Planner\n스펙의 구현 계획을 수립하세요.",
            AgentRole.Architect => "# 역할: Architect\n아키텍처 리뷰를 수행하세요.",
            AgentRole.TestValidator => "# 역할: Test Validator\n테스트 검증을 수행하세요.",
            AgentRole.Developer => "# 역할: Developer\n구현을 수행하세요.",
            _ => $"# 역할: {role}"
        };
    }

    private static string GetSpecValidatorInstruction(AgentInput input)
    {
        if (input.Spec.State == FlowState.Draft)
        {
            return """
                # 역할: Spec Validator — AC Precheck

                이 스펙의 인수 조건(Acceptance Criteria)이 검증 가능한지 사전 검사하세요.

                판단 기준:
                - AC가 측정 가능하고 명확한가?
                - 구현 범위가 합리적인가?
                - 의존성에 문제가 없는가?

                가능한 이벤트:
                - acPrecheckPassed: AC가 적절한 경우
                - acPrecheckRejected: AC가 부적절한 경우 (summary에 사유 기재)
                """;
        }

        return """
            # 역할: Spec Validator — Validation

            구현 결과가 스펙의 인수 조건을 충족하는지 검증하세요.

            가능한 이벤트:
            - specValidationPassed: 모든 AC 충족
            - specValidationReworkRequested: 재작업 필요 (summary에 사유 기재)
            - specValidationUserReviewRequested: 사용자 판단 필요 (proposedReviewRequest 포함 필수)
            - specValidationFailed: 치명적 실패 (복구 불가)
            """;
    }
}
