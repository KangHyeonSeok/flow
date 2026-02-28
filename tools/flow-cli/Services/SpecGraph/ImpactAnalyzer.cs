namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 영향 분석기. 특정 스펙 변경 시 하위/의존 스펙의 영향 범위를 계산.
/// BFS 기반으로 전파하며, 최대 depth를 설정하여 무한 전파를 방지.
/// </summary>
public class ImpactAnalyzer
{
    private readonly int _defaultMaxDepth;

    public ImpactAnalyzer(int defaultMaxDepth = 10)
    {
        _defaultMaxDepth = defaultMaxDepth;
    }

    /// <summary>
    /// 특정 스펙의 변경이 미치는 영향 범위를 BFS로 계산합니다.
    /// 
    /// 탐색 방향:
    /// 1. 하위 노드 (tree: parent → children)
    /// 2. 의존하는 노드 (reverseDag: B depends on A → A 변경 시 B 영향)
    /// </summary>
    public ImpactResult Analyze(SpecGraph graph, string sourceId, int? maxDepth = null)
    {
        var depth = maxDepth ?? _defaultMaxDepth;

        if (!graph.Nodes.ContainsKey(sourceId))
            throw new ArgumentException($"스펙 '{sourceId}'이(가) 존재하지 않습니다.");

        var result = new ImpactResult
        {
            SourceId = sourceId,
            MaxDepth = depth
        };

        var visited = new HashSet<string> { sourceId };
        var queue = new Queue<(string id, int currentDepth, string relation)>();

        // 1. 하위 노드 탐색 (tree children)
        if (graph.Tree.TryGetValue(sourceId, out var children))
        {
            foreach (var child in children)
            {
                queue.Enqueue((child, 1, "child"));
            }
        }

        // 2. 의존하는 노드 탐색 (reverse DAG)
        if (graph.ReverseDag.TryGetValue(sourceId, out var dependents))
        {
            foreach (var dep in dependents)
            {
                queue.Enqueue((dep, 1, "dependent"));
            }
        }

        while (queue.Count > 0)
        {
            var (id, currentDepth, relation) = queue.Dequeue();

            if (visited.Contains(id) || currentDepth > depth)
                continue;

            visited.Add(id);

            var node = graph.Nodes.GetValueOrDefault(id);
            result.ImpactedNodes.Add(new ImpactedNode
            {
                Id = id,
                Title = node?.Title ?? id,
                Depth = currentDepth,
                Relation = currentDepth > 1 ? "transitive" : relation
            });

            // 재귀적으로 하위 노드와 의존 노드를 탐색
            if (currentDepth < depth)
            {
                // 하위 노드
                if (graph.Tree.TryGetValue(id, out var subChildren))
                {
                    foreach (var child in subChildren)
                    {
                        if (!visited.Contains(child))
                            queue.Enqueue((child, currentDepth + 1, "child"));
                    }
                }

                // 의존하는 노드
                if (graph.ReverseDag.TryGetValue(id, out var subDeps))
                {
                    foreach (var dep in subDeps)
                    {
                        if (!visited.Contains(dep))
                            queue.Enqueue((dep, currentDepth + 1, "dependent"));
                    }
                }
            }
        }

        return result;
    }
}
