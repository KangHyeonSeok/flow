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
            AgentRole.TestGenerator => GetTestGeneratorInstruction(),
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
            TestGenerator가 생성한 BDD 테스트를 통과하도록 코드를 구현하세요.

            # 지시사항

            1. 작업 디렉토리의 BDD 테스트 파일을 먼저 확인하세요.
            2. 테스트가 요구하는 행동을 구현하세요.
            3. 모든 BDD 테스트가 통과하는지 실행하여 확인하세요.
            4. 기존 테스트가 깨지지 않는지 확인하세요.
            5. 변경 내용과 테스트 결과를 evidence로 보고하세요.

            # 구현 원칙

            - 최소 변경 우선
            - 기존 스타일 유지
            - 불필요한 리팩터링 금지
            - 커밋은 하지 않음

            # Evidence 보고 형식

            구현 완료 후 변경된 파일과 테스트 결과를 evidenceRefs로 보고하세요:

            ```json
            {
              "proposedEvent": "implementationSubmitted",
              "summary": "AC를 만족하도록 구현했습니다.",
              "proposedReviewRequest": null,
              "evidenceRefs": [
                { "kind": "source", "relativePath": "src/Foo.cs", "summary": "핵심 로직 구현" },
                { "kind": "test", "relativePath": "tests/FooTests.cs", "summary": "AC-1 검증 테스트" },
                { "kind": "testResult", "relativePath": "test-output.log", "summary": "전체 테스트 통과" }
              ]
            }
            ```

            evidenceRefs의 kind 값: source, test, testResult, config, doc
            relativePath는 작업 디렉토리(worktree) 기준 상대 경로입니다.
            runner가 evidence manifest에 이 경로를 기록합니다.
            """;
    }

    private static string GetTestGeneratorInstruction()
    {
        return """
            # 역할: Test Generator — BDD 테스트 생성

            당신은 BDD 테스트 전문가입니다.
            스펙의 acceptance criteria(Given-When-Then)를 기반으로 테스트를 생성하세요.
            이 테스트는 Developer가 구현 시 통과해야 할 목표가 됩니다.

            # 지시사항

            1. 각 AC를 분석하고 Given-When-Then 구조의 테스트를 작성하세요.
            2. 프로젝트의 기존 테스트 패턴과 프레임워크를 파악하세요.
            3. 테스트는 현재 코드로는 실패해야 합니다 (red phase).
            4. 테스트 파일을 작업 디렉토리에 생성하세요.
            5. 각 AC에 대해 최소 1개의 테스트를 작성하세요.

            # 테스트 원칙

            - AC의 핵심 행동(behavior)을 검증하는 테스트만 작성
            - 구현 세부사항이 아닌 행동을 테스트
            - 기존 프로젝트의 테스트 컨벤션 준수
            - 테스트 이름은 AC를 명확히 설명 (Given_When_Then 패턴)
            - 테스트가 BDD AC와 1:1로 추적 가능해야 함

            # Evidence 보고 형식

            테스트 생성 완료 후 evidenceRefs로 보고하세요:

            ```json
            {
              "proposedEvent": "testGenerationCompleted",
              "summary": "AC 기반 BDD 테스트를 생성했습니다.",
              "proposedReviewRequest": null,
              "evidenceRefs": [
                { "kind": "test", "relativePath": "tests/FooTests.cs", "summary": "AC-1 BDD 테스트" },
                { "kind": "test", "relativePath": "tests/BarTests.cs", "summary": "AC-2 BDD 테스트" }
              ]
            }
            ```

            AC로부터 테스트를 생성할 수 없는 경우 `testGenerationRejected`를 제안하고
            summary에 어떤 AC가 문제인지 구체적으로 기재하세요.

            evidenceRefs의 kind 값: source, test, testResult, config, doc
            relativePath는 작업 디렉토리(worktree) 기준 상대 경로입니다.
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

        var reworkCount = input.Spec.RetryCounters?.ReworkLoopCount ?? 0;
        var userReviewCount = input.Spec.RetryCounters?.UserReviewLoopCount ?? 0;
        var answeredRRs = input.ReviewRequests
            .Where(r => r.Status == ReviewRequestStatus.Answered)
            .ToList();

        var contextSection = "";
        if (answeredRRs.Count > 0)
        {
            contextSection = $"""

                # 사용자 피드백 이력

                이전 리뷰에서 사용자가 {answeredRRs.Count}회 응답했습니다.
                Review Requests 섹션의 Answered 항목을 반드시 확인하고, 사용자 피드백을 반영하여 판단하세요.
                이미 답변된 질문을 반복하지 마세요.
                """;
        }

        return $$"""
            # 역할: Spec Validator — Validation

            구현 결과가 스펙의 인수 조건(AC)을 충족하는지 검증하세요.

            # 검증 절차

            1. 스펙의 각 AC를 하나씩 확인하세요.
            2. 작업 디렉토리의 소스 코드를 읽고 AC 구현 여부를 판단하세요.
            3. 테스트 코드와 실행 결과를 확인하세요.
            4. 최근 활동 이력에서 TestGenerator와 Developer의 evidence를 참고하세요.

            # 판단 기준

            ## specValidationPassed (승인)
            - 모든 AC의 핵심 의도가 충족된 경우 승인하세요.
            - 사소한 스타일 차이, 추가 개선 가능성은 승인 사유가 됩니다. 완벽을 요구하지 마세요.
            - 테스트가 통과하고 AC를 검증하고 있다면 승인하세요.

            ## specValidationReworkRequested (재작업)
            - AC의 핵심 기능이 누락되었거나 명백히 잘못 구현된 경우에만 사용하세요.
            - summary에 구체적으로 어떤 AC가 미충족인지, 무엇을 수정해야 하는지 명시하세요.
            - 현재 rework 횟수: {{reworkCount}}/3. 초과 시 실패로 전환됩니다.

            ## specValidationUserReviewRequested (사용자 확인)
            - AC 해석이 모호하거나 코드만으로 판단 불가능한 경우에만 사용하세요.
            - proposedReviewRequest를 반드시 포함하세요 (질문과 선택지).
            - 현재 사용자 리뷰 횟수: {{userReviewCount}}/3. 초과 시 실패로 전환됩니다.

            ## specValidationFailed (치명적 실패)
            - 구현이 근본적으로 잘못되어 재작업으로도 복구할 수 없는 경우에만 사용하세요.
            - 스펙 자체의 결함, 기술적 불가능 등 극단적 상황에 한정합니다.

            # 중요 원칙

            - **승인 우선**: 의심스러우면 승인하세요. rework는 비용이 큽니다.
            - **구체적 근거**: 모든 판단에 코드 파일명과 줄 번호를 근거로 제시하세요.
            - **AC 중심**: AC에 명시되지 않은 요구사항으로 거부하지 마세요.
            {{contextSection}}
            """;

    }
}
