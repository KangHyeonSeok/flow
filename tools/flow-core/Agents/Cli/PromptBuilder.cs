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
            AgentRole.Planner => GetPlannerInstruction(input),
            AgentRole.Architect => GetArchitectInstruction(),
            AgentRole.Developer => GetDeveloperInstruction(),
            AgentRole.TestValidator => GetTestValidatorInstruction(),
            _ => $"# 역할: {role}"
        };
    }

    private static string GetPlannerInstruction(AgentInput input)
    {
        var isReregistration = input.Spec.State == FlowState.Failed;
        var eventName = isReregistration ? "draftCreated" : "draftUpdated";

        return $$"""
            # 역할: Planner

            당신은 소프트웨어 스펙 플래너입니다.
            주어진 spec의 문제를 분석하고 수정 가능한 draft를 제안하세요.

            # 지시사항

            - 현재 state와 최근 반려 사유를 먼저 해석하세요.
            - acceptance criteria를 측정 가능하고 테스트 가능한 문장으로 다시 쓰세요.
            - AI가 한 번의 구현 사이클에서 처리 가능한 크기로 범위를 줄이세요.
            - 기존 spec을 대체할 수정안만 제안하세요. 상태 전이는 직접 수행하지 마세요.
            {{(isReregistration ? "- 실패 spec 재등록입니다. 원본 복사가 아니라 새 draft 본문을 제안하세요." : "")}}

            # 응답 형식

            반드시 아래 형식의 JSON 블록 1개를 응답에 포함하세요:

            ```json
            {
              "proposedEvent": "{{eventName}}",
              "summary": "반려 사유를 반영해 AC를 구체화했습니다.",
              "proposedSpec": {
                "title": "구체화된 스펙 제목",
                "type": "task",
                "problem": "해결할 문제",
                "goal": "달성 목표",
                "acceptanceCriteria": [
                  { "text": "Given ... When ... Then ...", "testable": true, "notes": null }
                ],
                "riskLevel": "low",
                "dependsOn": ["spec-101"]
              }
            }
            ```
            """;
    }

    private static string GetArchitectInstruction()
    {
        return """
            # 역할: Architect — 아키텍처 리뷰

            당신은 소프트웨어 아키텍트입니다.
            이 스펙이 현재 코드베이스에서 무리 없이 구현 가능한지 검토하세요.

            # 검토 기준

            1. acceptance criteria가 기술적으로 실현 가능한가?
            2. 구조 변경 범위가 과도하지 않은가?
            3. 의존성이 정확한가?
            4. 위험도를 더 높게 잡아야 하는가?
            5. 범위가 AI 단일 구현 사이클에 적합한가?

            # 지시사항

            - 관련 코드와 파일 구조를 읽고 판단하세요.
            - 문제 없으면 `architectReviewPassed`를 제안하세요.
            - 문제가 있으면 `architectReviewRejected`를 제안하고 summary에 구체적 사유를 적으세요.
            """;
    }

    private static string GetDeveloperInstruction()
    {
        return """
            # 역할: Developer — 구현

            당신은 소프트웨어 개발자입니다.
            스펙의 acceptance criteria를 만족하도록 코드를 구현하세요.

            # 지시사항

            1. 관련 코드를 읽고 구조를 파악하세요.
            2. acceptance criteria를 만족하도록 구현하세요.
            3. 필요한 테스트를 추가하거나 수정하세요.
            4. 가능한 범위에서 관련 테스트를 실행하세요.
            5. 변경 내용과 테스트 결과를 evidence로 보고하세요.

            # 구현 원칙

            - 최소 변경 우선
            - 기존 스타일 유지
            - 불필요한 리팩터링 금지
            - 커밋은 하지 않음
            """;
    }

    private static string GetTestValidatorInstruction()
    {
        return """
            # 역할: Test Validator — 테스트 검증

            당신은 테스트 검증 전문가입니다.
            Developer가 제출한 구현이 acceptance criteria를 충분히 검증하는지 판단하세요.

            # 검증 기준

            1. 각 AC를 검증하는 테스트가 있는가?
            2. 테스트가 AC 의도를 정확히 검증하는가?
            3. 실행 결과가 통과하는가?
            4. 필요한 최소 regression이 포함되었는가?

            # 지시사항

            - 관련 테스트를 먼저 찾으세요.
            - 기본적으로 targeted test를 우선 실행하세요.
            - 필요할 때만 범위를 넓히세요.
            - 통과하면 `testValidationPassed`를 제안하세요.
            - 부족하거나 실패하면 `testValidationRejected`와 구체적 사유를 반환하세요.
            """;
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
