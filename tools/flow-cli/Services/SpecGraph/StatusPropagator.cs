namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 상태 전파기. 특정 스펙의 상태 변경 시 관련 스펙의 상태를 자동으로 갱신합니다.
/// 
/// 규칙:
/// - 스펙 변경됨 → 의존하는 스펙 상태 = needs-review (단, done/deprecated 상태인 스펙은 제외)
/// - 상위 스펙 상태는 자동 집계하지 않는다. status는 각 스펙의 작업 상태만 표현한다.
/// </summary>
public class StatusPropagator
{
    private static readonly HashSet<string> ActiveStatuses = new()
    {
        "queued", "working", "needs-review"
    };

    /// <summary>
    /// 특정 스펙의 상태 변경을 전파하고, 변경이 필요한 스펙 목록을 반환합니다.
    /// 소스 스펙 자체도 결과에 포함하며, BFS로 transitive downstream까지 전파합니다.
    /// 실제 저장은 호출자가 수행합니다 (side-effect free).
    /// </summary>
    public List<(string Id, string OldStatus, string NewStatus)> Propagate(
        SpecGraph graph, string changedId, string newStatus)
    {
        var changes = new List<(string Id, string OldStatus, string NewStatus)>();

        if (!graph.Nodes.TryGetValue(changedId, out var changedNode))
            return changes;

        // 1. 소스 스펙 자체 상태 변경 포함
        if (changedNode.Status != newStatus)
            changes.Add((changedId, changedNode.Status, newStatus));

        // 2. BFS로 transitive downstream → needs-review 전파
        var visited = new HashSet<string> { changedId };
        var queue = new Queue<string>();

        if (graph.ReverseDag.TryGetValue(changedId, out var directDeps))
            foreach (var dep in directDeps)
                queue.Enqueue(dep);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (!graph.Nodes.TryGetValue(current, out var node)) continue;
            if (node.Status == "done" || node.Status == "deprecated") continue;

            if (node.Status != "needs-review")
                changes.Add((current, node.Status, "needs-review"));

            if (graph.ReverseDag.TryGetValue(current, out var nextDeps))
                foreach (var dep in nextDeps)
                    if (!visited.Contains(dep))
                        queue.Enqueue(dep);
        }

        return changes;
    }

    /// <summary>
    /// F-021-C3: 기존 스펙을 대체하는 신규 스펙 생성 시 안전 전환 분석.
    /// 기존 스펙이 활성 상태이거나 downstream 참조가 있으면 즉시 deprecated 처리하지 않도록 권장 방식을 반환한다.
    /// 실제 상태 변경은 호출자가 결정하고 수행한다 (side-effect free).
    /// </summary>
    /// <param name="graph">전체 스펙 그래프</param>
    /// <param name="oldSpecId">대체되는 기존 스펙 ID</param>
    /// <param name="newSpecId">기존 스펙을 대체하는 신규 스펙 ID</param>
    /// <returns>안전 전환 분석 결과</returns>
    public SupersedeTransitionResult PropagateSupersede(SpecGraph graph, string oldSpecId, string newSpecId)
    {
        var result = new SupersedeTransitionResult
        {
            OldSpecId = oldSpecId,
            NewSpecId = newSpecId
        };

        if (!graph.Nodes.TryGetValue(oldSpecId, out var oldSpec))
            return result;

        result.IsActiveSpec = ActiveStatuses.Contains(oldSpec.Status);

        // 활성 downstream 스펙 확인
        if (graph.ReverseDag.TryGetValue(oldSpecId, out var dependents))
        {
            result.DownstreamIds = dependents
                .Where(d => graph.Nodes.TryGetValue(d, out var dn)
                            && dn.Status != "deprecated"
                            && dn.Status != "done")
                .ToList();
        }

        result.HasActiveDownstream = result.DownstreamIds.Count > 0;

        // 권장 전환 방식 결정
        if (result.HasActiveDownstream)
        {
            result.RecommendedAction = "blocked-review";
            result.TransitionNotes =
                $"스펙 '{oldSpecId}'에 의존하는 활성 downstream 스펙이 {result.DownstreamIds.Count}개 있습니다 " +
                $"({string.Join(", ", result.DownstreamIds)}). " +
                "사용자 승인 없이 즉시 deprecated 처리하면 안 됩니다.";
        }
        else if (result.IsActiveSpec)
        {
            result.RecommendedAction = "needs-review";
            result.TransitionNotes =
                $"스펙 '{oldSpecId}'이 현재 '{oldSpec.Status}' 상태로 활성화되어 있습니다. " +
                "검토 완료 후 안전한 시점에 deprecated 처리하세요.";
        }
        else
        {
            result.RecommendedAction = "deprecate";
            result.TransitionNotes =
                $"스펙 '{oldSpecId}'은(는) 현재 '{oldSpec.Status}' 상태이며 활성 downstream 참조가 없습니다. " +
                "안전하게 deprecated 처리할 수 있습니다.";
        }

        return result;
    }
}
