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

            1. 한 spec은 한 번의 구현 사이클에서 끝낼 수 있어야 한다.
            2. feature는 acceptance criteria 3-5개 수준을 유지한다.
            3. 6개 이상이면 분해를 우선한다.
            4. umbrella spec은 만들지 않는다.
            5. 이미 존재하는 spec을 재사용할 수 있으면 dependsOn에 기존 ID를 넣는다.
            6. 각 acceptance criterion은 테스트 가능한 문장으로 작성한다.

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
