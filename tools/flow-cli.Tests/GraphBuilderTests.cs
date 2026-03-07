using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// GraphBuilder 테스트 — Kahn 알고리즘 순환 참조 감지 중심.
/// 문서 요구: 정상 DAG / 자기 참조 / 2-node cycle / N-node cycle / 다중 독립 cycle / 빈 그래프
/// </summary>
public class GraphBuilderTests
{
    private readonly GraphBuilder _builder = new();

    // ─── 1. 정상 DAG (cycle 없음) ───────────────────────────────────────

    [Fact]
    public void Build_NormalDag_NoCycles()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-002" }),
            Spec("F-002", deps: new[] { "F-003" }),
            Spec("F-003")
        };

        var graph = _builder.Build(specs);

        graph.CycleNodes.Should().BeEmpty();
        graph.TopologicalOrder.Should().NotBeNull();
        graph.TopologicalOrder.Should().HaveCount(3);
    }

    // ─── 2. 자기 참조 (self-loop) ───────────────────────────────────────

    [Fact]
    public void Build_SelfReference_DetectsCycle()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-001" })
        };

        var graph = _builder.Build(specs);

        graph.CycleNodes.Should().Contain("F-001");
    }

    // ─── 3. 2-node cycle ────────────────────────────────────────────────

    [Fact]
    public void Build_TwoNodeCycle_DetectedByKahn()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-002" }),
            Spec("F-002", deps: new[] { "F-001" })
        };

        var graph = _builder.Build(specs);

        graph.CycleNodes.Should().HaveCount(2);
        graph.CycleNodes.Should().Contain("F-001");
        graph.CycleNodes.Should().Contain("F-002");
        graph.TopologicalOrder.Should().BeNull();
    }

    // ─── 4. N-node cycle (3개 이상) ─────────────────────────────────────

    [Fact]
    public void Build_ThreeNodeCycle_DetectedByKahn()
    {
        // A → B → C → A (triangle cycle)
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-002" }),
            Spec("F-002", deps: new[] { "F-003" }),
            Spec("F-003", deps: new[] { "F-001" })
        };

        var graph = _builder.Build(specs);

        graph.CycleNodes.Should().HaveCount(3);
        graph.TopologicalOrder.Should().BeNull();
    }

    // ─── 5. 다중 독립 cycle ─────────────────────────────────────────────

    [Fact]
    public void Build_MultipleCycles_AllDetected()
    {
        // Cycle 1: A ↔ B
        // Cycle 2: C ↔ D
        // Independent: E (no cycle)
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-002" }),
            Spec("F-002", deps: new[] { "F-001" }),
            Spec("F-003", deps: new[] { "F-004" }),
            Spec("F-004", deps: new[] { "F-003" }),
            Spec("F-005") // 독립 노드, cycle 없음
        };

        var graph = _builder.Build(specs);

        graph.CycleNodes.Should().HaveCount(4);
        graph.CycleNodes.Should().Contain("F-001");
        graph.CycleNodes.Should().Contain("F-002");
        graph.CycleNodes.Should().Contain("F-003");
        graph.CycleNodes.Should().Contain("F-004");
        graph.CycleNodes.Should().NotContain("F-005");
    }

    // ─── 6. 빈 그래프 ──────────────────────────────────────────────────

    [Fact]
    public void Build_EmptyGraph_NoErrors()
    {
        var specs = new List<SpecNode>();
        var graph = _builder.Build(specs);

        graph.Nodes.Should().BeEmpty();
        graph.CycleNodes.Should().BeEmpty();
        graph.TopologicalOrder.Should().NotBeNull();
        graph.TopologicalOrder.Should().BeEmpty();
    }

    // ─── 7. 트리 구조 테스트 ────────────────────────────────────────────

    [Fact]
    public void Build_TreeStructure_ParentChildRelation()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-010", parent: "F-001"),
            Spec("F-011", parent: "F-001"),
            Spec("F-012", parent: "F-001")
        };

        var graph = _builder.Build(specs);

        graph.Roots.Should().Contain("F-001");
        graph.Tree["F-001"].Should().HaveCount(3);
        graph.Tree["F-001"].Should().Contain("F-010");
    }

    // ─── 8. Orphan 노드 감지 ────────────────────────────────────────────

    [Fact]
    public void Build_OrphanNode_Detected()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-010", parent: "F-999") // F-999 doesn't exist
        };

        var graph = _builder.Build(specs);
        graph.OrphanNodes.Should().Contain("F-010");
    }

    // ─── 9. 역방향 DAG 생성 ────────────────────────────────────────────

    [Fact]
    public void Build_ReverseDag_Correct()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-002", deps: new[] { "F-001" }),
            Spec("F-003", deps: new[] { "F-001" })
        };

        var graph = _builder.Build(specs);

        graph.ReverseDag.Should().ContainKey("F-001");
        graph.ReverseDag["F-001"].Should().Contain("F-002");
        graph.ReverseDag["F-001"].Should().Contain("F-003");
    }

    // ─── 10. 트리 렌더링 ───────────────────────────────────────────────

    [Fact]
    public void RenderTree_ProducesFormattedOutput()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "인증 시스템", Description = "desc", Status = "working" },
            new() { Id = "F-010", Title = "로그인", Description = "desc", Status = "verified", Parent = "F-001" },
            new() { Id = "F-011", Title = "비밀번호 재설정", Description = "desc", Status = "draft", Parent = "F-001" }
        };

        var graph = _builder.Build(specs);
        var tree = _builder.RenderTree(graph);

        tree.Should().Contain("F-001");
        tree.Should().Contain("인증 시스템");
        tree.Should().Contain("F-010");
        tree.Should().Contain("로그인");
        tree.Should().Contain("✅"); // verified
        tree.Should().Contain("🔵"); // working
        tree.Should().Contain("⬜"); // draft
    }

    // ─── 11. DAG with mixed cycle and non-cycle nodes ───────────────────

    [Fact]
    public void Build_MixedCycleAndNonCycle_SeparatedCorrectly()
    {
        // F-001 → F-002 → F-003 → F-002 (cycle: F-002, F-003)
        // F-004 depends on F-001 (no cycle)
        var specs = new List<SpecNode>
        {
            Spec("F-001", deps: new[] { "F-002" }),
            Spec("F-002", deps: new[] { "F-003" }),
            Spec("F-003", deps: new[] { "F-002" }),
            Spec("F-004", deps: new[] { "F-001" })
        };

        var graph = _builder.Build(specs);

        // F-002 and F-003 are in the cycle (mutual dependency)
        graph.CycleNodes.Should().Contain("F-002");
        graph.CycleNodes.Should().Contain("F-003");
        // F-001 and F-004 have in-degree 0, so they get resolved by Kahn's algorithm
        // even though F-001 depends on a cycle member
        graph.CycleNodes.Should().NotContain("F-001");
        graph.CycleNodes.Should().NotContain("F-004");
    }

    // ─── 12. Condition 하위 노드 렌더링 ─────────────────────────────────

    [Fact]
    public void RenderTree_ShowsConditions()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "로그인",
                Description = "desc",
                Status = "verified",
                Conditions = new List<SpecCondition>
                {
                    new() { Id = "F-001-C1", Description = "유효한 자격증명 시 토큰 발급", Status = "verified" },
                    new() { Id = "F-001-C2", Description = "무효한 자격증명 시 에러", Status = "working" }
                }
            }
        };

        var graph = _builder.Build(specs);
        var tree = _builder.RenderTree(graph);

        tree.Should().Contain("F-001-C1");
        tree.Should().Contain("유효한 자격증명 시 토큰 발급");
        tree.Should().Contain("F-001-C2");
    }

    // ─── Helper ─────────────────────────────────────────────────────────

    private static SpecNode Spec(string id, string? parent = null, string[]? deps = null) => new()
    {
        Id = id,
        Title = $"기능 {id}",
        Description = $"{id} 설명",
        Status = "draft",
        Parent = parent,
        Dependencies = deps?.ToList() ?? new List<string>()
    };
}
