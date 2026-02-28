namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 상태 전파기. 특정 스펙의 상태 변경 시 관련 스펙의 상태를 자동으로 갱신합니다.
/// 
/// 규칙:
/// - 스펙 변경됨 → 의존하는 스펙 상태 = needs-review
/// - 상위 스펙의 상태는 하위 스펙의 상태를 집계:
///   - 모든 하위가 verified → parent = verified
///   - 일부 verified → parent = active  
///   - verified 없음 → parent = draft
///   - 하나라도 needs-review → parent = needs-review
///   - 모두 deprecated → parent = deprecated
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
                    if (depNode.Status != "needs-review" && depNode.Status != "deprecated")
                    {
                        changes.Add((depId, depNode.Status, "needs-review"));
                    }
                }
            }
        }

        // 2. 상위 노드 상태를 하위 노드 상태로 집계
        var node = graph.Nodes[changedId];
        if (!string.IsNullOrEmpty(node.Parent) && graph.Nodes.ContainsKey(node.Parent))
        {
            var parentChanges = AggregateParentStatus(graph, node.Parent, changedId, newStatus);
            changes.AddRange(parentChanges);
        }

        return changes;
    }

    /// <summary>
    /// 상위 스펙의 상태를 하위 스펙의 상태로 집계합니다.
    /// </summary>
    private List<(string Id, string OldStatus, string NewStatus)> AggregateParentStatus(
        SpecGraph graph, string parentId, string changedChildId, string changedChildStatus)
    {
        var changes = new List<(string Id, string OldStatus, string NewStatus)>();

        if (!graph.Nodes.TryGetValue(parentId, out var parent))
            return changes;

        // 하위 노드 상태 수집
        var childStatuses = new List<string>();
        if (graph.Tree.TryGetValue(parentId, out var childIds))
        {
            foreach (var childId in childIds)
            {
                if (childId == changedChildId)
                    childStatuses.Add(changedChildStatus);
                else if (graph.Nodes.TryGetValue(childId, out var child))
                    childStatuses.Add(child.Status);
            }
        }

        if (childStatuses.Count == 0)
            return changes;

        // 집계 로직
        string newParentStatus;
        if (childStatuses.Any(s => s == "needs-review"))
            newParentStatus = "needs-review";
        else if (childStatuses.All(s => s == "verified"))
            newParentStatus = "verified";
        else if (childStatuses.All(s => s == "deprecated"))
            newParentStatus = "deprecated";
        else if (childStatuses.Any(s => s == "verified" || s == "active"))
            newParentStatus = "active";
        else
            newParentStatus = "draft";

        if (parent.Status != newParentStatus)
        {
            changes.Add((parentId, parent.Status, newParentStatus));

            // 재귀적으로 상위로 전파
            if (!string.IsNullOrEmpty(parent.Parent))
            {
                var upperChanges = AggregateParentStatus(graph, parent.Parent, parentId, newParentStatus);
                changes.AddRange(upperChanges);
            }
        }

        return changes;
    }
}
