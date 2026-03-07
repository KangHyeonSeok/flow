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
    /// <summary>
    /// 특정 스펙의 상태 변경을 전파하고, 변경이 필요한 스펙 목록을 반환합니다.
    /// 실제 저장은 호출자가 수행합니다 (side-effect free).
    /// </summary>
    public List<(string Id, string OldStatus, string NewStatus)> Propagate(
        SpecGraph graph, string changedId, string newStatus)
    {
        var changes = new List<(string Id, string OldStatus, string NewStatus)>();

        if (!graph.Nodes.ContainsKey(changedId))
            return changes;

        // 1. 의존하는 노드들을 needs-review로 전환
        if (graph.ReverseDag.TryGetValue(changedId, out var dependents))
        {
            foreach (var depId in dependents)
            {
                if (graph.Nodes.TryGetValue(depId, out var depNode))
                {
                    if (depNode.Status != "needs-review" && depNode.Status != "deprecated" && depNode.Status != "done")
                    {
                        changes.Add((depId, depNode.Status, "needs-review"));
                    }
                }
            }
        }

        return changes;
    }
}
