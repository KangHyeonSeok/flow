using FlowCore.Models;

namespace FlowCore.Rules;

/// <summary>의존성 cascade 계산 및 cycle 검출 순수 함수</summary>
public static class DependencyEvaluator
{
    private static readonly ProcessingStatus[] NormalStatuses =
    [
        ProcessingStatus.Pending,
        ProcessingStatus.InProgress,
        ProcessingStatus.InReview,
        ProcessingStatus.Done
    ];

    /// <summary>
    /// 변경된 spec의 상태를 기반으로 downstream spec들에 발생할 이벤트를 계산한다.
    /// </summary>
    public static IReadOnlyList<DependencyEffect> Evaluate(DependencyInput input)
    {
        var effects = new List<DependencyEffect>();
        var changed = input.ChangedSpec;

        // FlowState.Failed 전이 → downstream에 DependencyFailed
        if (changed.State == FlowState.Failed && input.PreviousState != FlowState.Failed)
        {
            foreach (var ds in input.DownstreamSpecs)
            {
                effects.Add(new DependencyEffect
                {
                    TargetSpecId = ds.Id,
                    Event = FlowEvent.DependencyFailed
                });
            }
            return effects;
        }

        // ProcessingStatus가 OnHold 또는 Error로 전이 → downstream에 DependencyBlocked
        if (IsBlockedStatus(changed.ProcessingStatus) && !IsBlockedStatus(input.PreviousProcessingStatus))
        {
            foreach (var ds in input.DownstreamSpecs)
            {
                effects.Add(new DependencyEffect
                {
                    TargetSpecId = ds.Id,
                    Event = FlowEvent.DependencyBlocked
                });
            }
            return effects;
        }

        // 정상 상태로 복귀 → OnHold인 downstream 중 모든 upstream이 정상인 것에만 DependencyResolved
        if (IsNormalStatus(changed.ProcessingStatus) && IsBlockedStatus(input.PreviousProcessingStatus))
        {
            // AllUpstreamSpecs를 ID로 인덱싱 (ChangedSpec의 최신 상태가 이미 반영된 상태)
            var upstreamById = new Dictionary<string, SpecSnapshot>();
            foreach (var us in input.AllUpstreamSpecs)
                upstreamById[us.Id] = us;

            foreach (var ds in input.DownstreamSpecs)
            {
                if (ds.ProcessingStatus != ProcessingStatus.OnHold)
                    continue;

                // 이 downstream의 모든 upstream이 정상인지 확인
                if (HasAnyBlockedUpstream(ds, upstreamById))
                    continue;

                effects.Add(new DependencyEffect
                {
                    TargetSpecId = ds.Id,
                    Event = FlowEvent.DependencyResolved
                });
            }
        }

        return effects;
    }

    /// <summary>전체 spec 그래프에서 cycle을 검출한다. 결과는 결정적이며 중복이 없다.</summary>
    public static IReadOnlyList<DependencyCycle> DetectCycles(
        IReadOnlyList<(string SpecId, IReadOnlyList<string> DependsOn)> graph)
    {
        var adj = new Dictionary<string, List<string>>();
        var allNodes = new HashSet<string>();

        foreach (var (specId, dependsOn) in graph)
        {
            allNodes.Add(specId);
            if (!adj.ContainsKey(specId))
                adj[specId] = [];
            foreach (var dep in dependsOn)
            {
                adj[specId].Add(dep);
                allNodes.Add(dep);
                if (!adj.ContainsKey(dep))
                    adj[dep] = [];
            }
        }

        var rawCycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();
        var stack = new List<string>();

        // 정렬된 순서로 순회하여 결정적 DFS 시작점 보장
        var sortedNodes = allNodes.OrderBy(n => n, StringComparer.Ordinal).ToList();

        foreach (var node in sortedNodes)
        {
            if (!visited.Contains(node))
                Dfs(node, adj, visited, onStack, stack, rawCycles);
        }

        // 중복 cycle 제거: cycle을 정규화(최소 원소 기준 회전 후 정렬 키 생성)하여 deduplicate
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<DependencyCycle>();

        foreach (var cycleNodes in rawCycles)
        {
            var key = NormalizeCycleKey(cycleNodes);
            if (seen.Add(key))
                cycles.Add(new DependencyCycle { SpecIds = cycleNodes });
        }

        return cycles;
    }

    /// <summary>cycle 노드 리스트를 최소 원소 기준으로 회전시킨 문자열 키를 반환한다.</summary>
    private static string NormalizeCycleKey(List<string> cycleNodes)
    {
        if (cycleNodes.Count == 0) return "";
        var minIdx = 0;
        for (int i = 1; i < cycleNodes.Count; i++)
        {
            if (string.Compare(cycleNodes[i], cycleNodes[minIdx], StringComparison.Ordinal) < 0)
                minIdx = i;
        }
        // 최소 원소부터 시작하도록 회전
        var rotated = new List<string>(cycleNodes.Count);
        for (int i = 0; i < cycleNodes.Count; i++)
            rotated.Add(cycleNodes[(minIdx + i) % cycleNodes.Count]);
        return string.Join("→", rotated);
    }

    private static void Dfs(
        string node,
        Dictionary<string, List<string>> adj,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> stack,
        List<List<string>> cycles)
    {
        visited.Add(node);
        onStack.Add(node);
        stack.Add(node);

        // 인접 노드도 정렬하여 결정적 순회
        foreach (var neighbor in adj[node].OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!visited.Contains(neighbor))
            {
                Dfs(neighbor, adj, visited, onStack, stack, cycles);
            }
            else if (onStack.Contains(neighbor))
            {
                // cycle 발견: stack에서 neighbor부터 현재까지 추출
                var cycleStart = stack.IndexOf(neighbor);
                var cycleNodes = stack.Skip(cycleStart).ToList();
                cycles.Add(cycleNodes);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        onStack.Remove(node);
    }

    /// <summary>downstream의 dependsOn 중 하나라도 blocked/failed 상태인 upstream이 있는지 확인한다.</summary>
    private static bool HasAnyBlockedUpstream(
        SpecSnapshot downstream, Dictionary<string, SpecSnapshot> upstreamById)
    {
        foreach (var upId in downstream.DependsOn)
        {
            if (!upstreamById.TryGetValue(upId, out var upstream))
                continue; // upstream 정보 없으면 안전 측으로 skip

            if (upstream.State == FlowState.Failed || IsBlockedStatus(upstream.ProcessingStatus))
                return true;
        }
        return false;
    }

    private static bool IsBlockedStatus(ProcessingStatus status) =>
        status == ProcessingStatus.OnHold || status == ProcessingStatus.Error;

    private static bool IsNormalStatus(ProcessingStatus status) =>
        Array.IndexOf(NormalStatuses, status) >= 0;
}
