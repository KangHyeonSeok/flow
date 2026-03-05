using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 의존성 그래프 기반 스펙 구현 순서 결정기.
/// Kahn 알고리즘으로 Phase별 위상 정렬 수행.
/// 동일 Phase 내에서는 priority(P1>P2>P3) → conditions 수 오름차순으로 정렬.
/// </summary>
public class SpecOrderer
{
    /// <summary>
    /// 전체 스펙 목록에서 구현 순서를 계산합니다.
    /// </summary>
    /// <param name="specs">전체 스펙 목록</param>
    /// <param name="fromId">이 스펙 기준 부분 순서 산출 (null이면 전체)</param>
    public SpecOrderResult ComputeOrder(List<SpecNode> specs, string? fromId = null)
    {
        // --from 옵션: 해당 스펙의 선행 의존성 서브그래프만 포함
        if (!string.IsNullOrEmpty(fromId))
            specs = GetPrerequisiteSubgraph(specs, fromId);

        var result = new SpecOrderResult
        {
            FromId = fromId,
            TotalSpecs = specs.Count
        };

        if (specs.Count == 0)
            return result;

        var nodeMap = specs.ToDictionary(s => s.Id);

        // in-degree 계산: spec.Dependencies = [B] → B가 먼저 구현돼야 함
        // → in-degree[A] = A가 의존하는 (먼저 구현해야 할) 스펙 수
        var inDegree = specs.ToDictionary(s => s.Id, s =>
            s.Dependencies.Count(d => nodeMap.ContainsKey(d)));

        // reverseEdges[B] = [A, C, ...]: B가 완료되면 A와 C의 in-degree가 감소함
        var reverseEdges = new Dictionary<string, List<string>>();
        foreach (var spec in specs)
        {
            foreach (var dep in spec.Dependencies)
            {
                if (!nodeMap.ContainsKey(dep)) continue;
                if (!reverseEdges.ContainsKey(dep))
                    reverseEdges[dep] = new List<string>();
                reverseEdges[dep].Add(spec.Id);
            }
        }

        var processed = new HashSet<string>();
        var phases = new List<SpecOrderPhase>();

        while (true)
        {
            // in-degree 0인 미처리 노드 = 이번 Phase에 구현 가능한 스펙
            var ready = inDegree
                .Where(kv => kv.Value == 0 && !processed.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();

            if (ready.Count == 0) break;

            // 정렬: priority 오름차순(P1<P2<P3), 동일 시 conditions 수 오름차순
            ready = ready
                .OrderBy(id => PriorityOrder(GetPriority(nodeMap[id])))
                .ThenBy(id => nodeMap[id].Conditions.Count)
                .ThenBy(id => id)
                .ToList();

            var phase = new SpecOrderPhase
            {
                Phase = phases.Count,
                Specs = ready.Select(id => new SpecOrderEntry
                {
                    Id = id,
                    Title = nodeMap[id].Title,
                    Status = nodeMap[id].Status,
                    Priority = GetPriority(nodeMap[id]),
                    ConditionsCount = nodeMap[id].Conditions.Count,
                    Dependencies = nodeMap[id].Dependencies
                        .Where(d => nodeMap.ContainsKey(d))
                        .ToList()
                }).ToList()
            };

            phases.Add(phase);

            foreach (var id in ready)
            {
                processed.Add(id);
                inDegree.Remove(id);

                // 이 spec이 완료되면 의존하던 spec들의 in-degree 감소
                if (reverseEdges.TryGetValue(id, out var dependents))
                {
                    foreach (var dep in dependents)
                    {
                        if (inDegree.ContainsKey(dep))
                            inDegree[dep]--;
                    }
                }
            }
        }

        result.Phases = phases;

        // 처리되지 않은 노드 = 순환 참조
        var cycleNodes = nodeMap.Keys.Where(id => !processed.Contains(id)).ToList();
        result.HasCycles = cycleNodes.Count > 0;
        result.CycleNodes = cycleNodes;

        return result;
    }

    /// <summary>
    /// AI 최적화 순서의 의존성 제약 위반을 검증합니다.
    /// A가 B에 의존하면 B는 A보다 먼저 나타나야 합니다.
    /// </summary>
    public List<DependencyViolation> ValidateDependencyConstraints(
        SpecOrderResult baseOrder,
        List<SpecOrderPhase> aiPhases)
    {
        var violations = new List<DependencyViolation>();

        // 각 spec의 phase 번호를 기록
        var aiPhaseOf = new Dictionary<string, int>();
        foreach (var phase in aiPhases)
            foreach (var entry in phase.Specs)
                aiPhaseOf[entry.Id] = phase.Phase;

        // 기존 스펙 맵 (의존성 확인용)
        var allEntries = baseOrder.Phases
            .SelectMany(p => p.Specs)
            .ToDictionary(e => e.Id);

        foreach (var phase in aiPhases)
        {
            foreach (var entry in phase.Specs)
            {
                if (!allEntries.TryGetValue(entry.Id, out var original))
                    continue;

                foreach (var dep in original.Dependencies)
                {
                    if (!aiPhaseOf.TryGetValue(dep, out var depPhase))
                        continue;

                    // 의존 스펙(dep)이 현재 스펙보다 같거나 늦은 Phase이면 위반
                    if (depPhase >= phase.Phase)
                    {
                        violations.Add(new DependencyViolation
                        {
                            SpecId = entry.Id,
                            DependsOn = dep,
                            Message = $"[{entry.Id}]은 [{dep}]에 의존하지만 AI 제안에서 [{dep}](Phase {depPhase})가 [{entry.Id}](Phase {phase.Phase}) 이후에 위치합니다."
                        });
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// AI 최적화 프롬프트용 JSON 텍스트를 생성합니다.
    /// </summary>
    public string BuildAiPrompt(SpecOrderResult baseOrder)
    {
        var phasesJson = JsonSerializer.Serialize(baseOrder.Phases, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return $$"""
다음은 의존성 그래프 기반으로 계산된 스펙 구현 순서입니다.
의존성 제약을 유지하면서 기술 위험도(복잡도, 불확실성)와 비즈니스 가치(사용자 영향, 수익 기여도)를 고려하여 최적화된 순서를 제안해주세요.

## 기본 계산 순서 (Phase별)
{{phasesJson}}

## 응답 형식 (반드시 JSON만 출력)
{
  "phases": [
    {
      "phase": 0,
      "specs": [
        { "id": "F-001", "title": "...", "status": "...", "priority": "P1", "conditionsCount": 3, "dependencies": [] }
      ]
    }
  ],
  "reasoning": "최적화 근거 설명"
}

## 제약 조건
1. 각 spec의 dependencies에 있는 spec은 반드시 더 낮은(or 같은) phase에 있어야 합니다.
2. Phase 번호를 0부터 순차적으로 재부여해도 됩니다.
3. JSON 외의 설명문은 절대 출력하지 마세요.
""";
    }

    /// <summary>
    /// fromId 스펙 및 그 선행 의존성 서브그래프를 반환합니다.
    /// (fromId를 구현하려면 필요한 모든 스펙 포함)
    /// </summary>
    private static List<SpecNode> GetPrerequisiteSubgraph(List<SpecNode> specs, string fromId)
    {
        var nodeMap = specs.ToDictionary(s => s.Id);
        var included = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(fromId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!included.Add(id)) continue;
            if (!nodeMap.TryGetValue(id, out var node)) continue;

            foreach (var dep in node.Dependencies)
                if (!included.Contains(dep))
                    queue.Enqueue(dep);
        }

        return specs.Where(s => included.Contains(s.Id)).ToList();
    }

    private static string GetPriority(SpecNode node)
    {
        if (!string.IsNullOrEmpty(node.Priority))
            return node.Priority.ToUpper();

        // metadata.priority fallback
        if (node.Metadata != null &&
            node.Metadata.TryGetValue("priority", out var pObj) &&
            pObj?.ToString() is string pStr)
            return pStr.ToUpper();

        return "P3";
    }

    private static int PriorityOrder(string priority) => priority switch
    {
        "P1" => 1,
        "P2" => 2,
        _    => 3  // P3 or unknown
    };
}
