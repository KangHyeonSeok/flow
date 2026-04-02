using System.Text;
using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowCore.Planning;

/// <summary>경로 B 전용: 사용자 요청 분해 프롬프트 생성</summary>
public sealed class PlannerPromptBuilder
{
    public string BuildPrompt(string userRequest, IReadOnlyList<Spec> existingSpecs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""
            # 역할: Planner — 요청 분해

            당신은 소프트웨어 스펙 플래너입니다.
            사용자의 요청을 AI가 자율적으로 구현할 수 있는 draft spec 단위로 분해하세요.
            """);

        sb.AppendLine("# 현재 프로젝트 스펙 목록");
        sb.AppendLine();
        if (existingSpecs.Count == 0)
        {
            sb.AppendLine("(없음)");
        }
        else
        {
            foreach (var spec in existingSpecs)
            {
                var deps = spec.Dependencies.DependsOn.Count > 0
                    ? string.Join(", ", spec.Dependencies.DependsOn)
                    : "-";
                sb.AppendLine($"- {spec.Id}: {spec.Title} [{spec.State}/{spec.ProcessingStatus}] deps=[{deps}]");
            }
        }
        sb.AppendLine();

        sb.AppendLine("# 사용자 요청");
        sb.AppendLine();
        sb.AppendLine(userRequest);
        sb.AppendLine();

        sb.AppendLine("""
            # 분해 원칙

            1. 한 spec은 한 번의 구현 사이클(테스트 생성 → 구현 → 검증)에서 끝낼 수 있어야 한다.
            2. feature는 acceptance criteria 3-5개 수준으로 유지한다. 6개 이상이면 분해를 우선한다.
            3. umbrella spec (자체 AC 없이 하위만 가지는 스펙)은 만들지 않는다.
            4. 이미 존재하는 spec을 재사용할 수 있으면 dependsOn에 기존 ID를 넣는다.
            5. 각 acceptance criterion은 Given-When-Then 형식의 테스트 가능한 문장으로 작성한다.

            # 분해 판단 기준

            아래 신호가 보이면 분해가 필요하다:
            - AC가 6개 이상
            - 서로 다른 서브시스템 3개 이상이 하나의 요청에 등장
            - "그리고", "또는", "추가로"가 반복되어 서로 독립적인 기능이 묶여 있음
            - 한 스펙의 추정 변경 파일이 15개 이상

            분해하지 않아야 할 경우:
            - AC가 3개 이하이고 단일 모듈 내 변경
            - 이미 충분히 작은 task (설정 변경, 단일 파일 수정 등)

            # 위험도 기준

            - low: 기존 패턴 내 변경, 새 파일 1~3개
            - medium: 새 모듈 추가 또는 기존 구조 변경, 파일 4~10개
            - high: 아키텍처 변경, 다수 모듈 영향, 마이그레이션 필요

            # AC 작성 규칙

            - Given-When-Then 형식 필수
            - 각 AC는 하나의 행동만 검증
            - 측정 불가능한 조건 사용 금지 ("빠르게", "깔끔하게" 등)
            - testable: true는 자동 테스트 가능한 경우에만 설정

            # 응답 형식

            반드시 아래 형식의 JSON 블록 1개를 응답에 포함하세요:

            ```json
            {
              "specs": [
                {
                  "title": "스펙 제목",
                  "type": "feature",
                  "problem": "해결할 문제",
                  "goal": "달성 목표",
                  "acceptanceCriteria": [
                    { "text": "Given ... When ... Then ...", "testable": true }
                  ],
                  "riskLevel": "medium",
                  "dependsOn": ["spec-001"],
                  "internalDependsOn": [0]
                }
              ],
              "summary": "분해 결과 요약"
            }
            ```

            internalDependsOn은 같은 응답 내 specs 배열의 0-based 인덱스입니다.
            """);

        return sb.ToString();
    }
}
