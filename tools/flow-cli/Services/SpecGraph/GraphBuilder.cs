namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 그래프 생성기. 트리 구조, DAG, 위상 정렬, 순환 참조 감지.
/// Kahn 알고리즘(위상 정렬) 기반 cycle 감지.
/// </summary>
public class GraphBuilder
{
    /// <summary>
    /// 스펙 목록으로부터 전체 그래프를 생성합니다.
    /// </summary>
    public SpecGraph Build(List<SpecNode> specs)
    {
        var graph = new SpecGraph();

        // 노드 등록
        foreach (var spec in specs)
        {
            graph.Nodes[spec.Id] = spec;
        }

        // 트리 구조 (parent → children)
        BuildTree(graph, specs);

        // DAG 구조 (dependencies)
        BuildDag(graph, specs);

        // 순환 참조 감지 + 위상 정렬 (Kahn 알고리즘)
        DetectCycles(graph);

        return graph;
    }

    private void BuildTree(SpecGraph graph, List<SpecNode> specs)
    {
        foreach (var spec in specs)
        {
            if (string.IsNullOrEmpty(spec.Parent))
            {
                graph.Roots.Add(spec.Id);
            }
            else
            {
                if (!graph.Tree.ContainsKey(spec.Parent))
                    graph.Tree[spec.Parent] = new List<string>();
                graph.Tree[spec.Parent].Add(spec.Id);

                // orphan 검사: parent가 노드 목록에 없으면 orphan
                if (!graph.Nodes.ContainsKey(spec.Parent))
                    graph.OrphanNodes.Add(spec.Id);
            }
        }
    }

    private void BuildDag(SpecGraph graph, List<SpecNode> specs)
    {
        foreach (var spec in specs)
        {
            graph.Dag[spec.Id] = new List<string>(spec.Dependencies);

            // 역방향 그래프 구축
            foreach (var dep in spec.Dependencies)
            {
                if (!graph.ReverseDag.ContainsKey(dep))
                    graph.ReverseDag[dep] = new List<string>();
                graph.ReverseDag[dep].Add(spec.Id);
            }
        }
    }

    /// <summary>
    /// Kahn 알고리즘(위상 정렬)으로 순환 참조를 감지합니다.
    /// 
    /// 알고리즘:
    /// 1. 모든 노드의 in-degree(진입 차수)를 계산
    /// 2. in-degree가 0인 노드를 큐에 추가
    /// 3. 큐에서 노드를 꺼내 위상 정렬 결과에 추가하고, 이 노드가 가리키는 노드들의 in-degree를 1 감소
    /// 4. in-degree가 0이 된 노드를 큐에 추가
    /// 5. 모든 노드가 처리되면 cycle 없음. 남은 노드가 있으면 cycle에 포함된 노드.
    /// </summary>
    private void DetectCycles(SpecGraph graph)
    {
        // in-degree 계산
        var inDegree = new Dictionary<string, int>();
        foreach (var nodeId in graph.Nodes.Keys)
        {
            inDegree[nodeId] = 0;
        }

        // dependencies 방향: A depends on B → A → B (A가 B를 필요로 함)
        // DAG에서: A의 dependencies = [B] 이면 B → A 방향 (B가 완료되어야 A 가능)
        // in-degree: A가 다른 노드에 의해 의존되는 횟수
        // 여기서는 reverseDag를 사용: B가 의존하는 노드에서 B로 향하는 edge
        
        // 실제로 의존성 그래프에서:
        // spec.Dependencies = ["B"] 이면 spec → B (spec이 B에 의존)
        // 이걸 DAG로 보면: edge from spec to B
        // in-degree of B += 1 (B는 spec에 의해 의존됨)
        foreach (var (nodeId, deps) in graph.Dag)
        {
            foreach (var dep in deps)
            {
                if (inDegree.ContainsKey(dep))
                    inDegree[dep]++;
            }
        }

        // in-degree 0인 노드를 큐에 추가
        var queue = new Queue<string>();
        foreach (var (nodeId, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(nodeId);
        }

        var sorted = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            // current의 dependencies를 순회
            if (graph.Dag.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (inDegree.ContainsKey(dep))
                    {
                        inDegree[dep]--;
                        if (inDegree[dep] == 0)
                            queue.Enqueue(dep);
                    }
                }
            }
        }

        // 모든 노드가 정렬되었으면 cycle 없음
        if (sorted.Count == graph.Nodes.Count)
        {
            graph.TopologicalOrder = sorted;
            graph.CycleNodes = new List<string>();
        }
        else
        {
            // 정렬되지 않은 노드들이 cycle에 포함됨
            graph.TopologicalOrder = null;
            graph.CycleNodes = graph.Nodes.Keys
                .Where(id => !sorted.Contains(id))
                .ToList();
        }
    }

    /// <summary>
    /// 트리를 텍스트 형태로 출력합니다.
    /// </summary>
    public string RenderTree(SpecGraph graph)
    {
        var lines = new List<string>();
        foreach (var root in graph.Roots.OrderBy(r => r))
        {
            RenderTreeNode(graph, root, "", true, lines);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void RenderTreeNode(SpecGraph graph, string nodeId, string prefix, bool isLast, List<string> lines)
    {
        var connector = isLast ? "└── " : "├── ";
        var node = graph.Nodes.GetValueOrDefault(nodeId);
        var status = node?.Status ?? "?";
        var title = node?.Title ?? nodeId;
        var statusIcon = GetStatusIcon(status);

        lines.Add($"{prefix}{connector}{statusIcon} [{nodeId}] {title}");

        // Condition 하위 노드
        if (node != null)
        {
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            var conditions = node.Conditions;
            for (int i = 0; i < conditions.Count; i++)
            {
                var cond = conditions[i];
                var condConnector = (i == conditions.Count - 1 && !graph.Tree.ContainsKey(nodeId)) ? "└── " : "├── ";
                var condIcon = GetStatusIcon(cond.Status);
                lines.Add($"{childPrefix}{condConnector}{condIcon} [{cond.Id}] {cond.Description}");
            }
        }

        // 하위 Feature 노드
        if (graph.Tree.TryGetValue(nodeId, out var children))
        {
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            var sorted = children.OrderBy(c => c).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                RenderTreeNode(graph, sorted[i], childPrefix, i == sorted.Count - 1, lines);
            }
        }
    }

    private static string GetStatusIcon(string status) => status switch
    {
        "verified" => "✅",
        "active" => "🔵",
        "draft" => "⬜",
        "needs-review" => "🟡",
        "deprecated" => "⛔",
        _ => "❓"
    };
}
