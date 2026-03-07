using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// ImpactAnalyzer 영향 분석 테스트
/// </summary>
public class ImpactAnalyzerTests
{
    private readonly GraphBuilder _graphBuilder = new();
    private readonly ImpactAnalyzer _analyzer = new(defaultMaxDepth: 10);

    [Fact]
    public void Analyze_DirectChildren_Found()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-010", parent: "F-001"),
            Spec("F-011", parent: "F-001")
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001");

        impact.ImpactedNodes.Should().HaveCount(2);
        impact.ImpactedNodes.Should().Contain(n => n.Id == "F-010" && n.Relation == "child");
        impact.ImpactedNodes.Should().Contain(n => n.Id == "F-011" && n.Relation == "child");
    }

    [Fact]
    public void Analyze_Dependents_Found()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-002", deps: new[] { "F-001" }),
            Spec("F-003", deps: new[] { "F-001" })
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001");

        impact.ImpactedNodes.Should().HaveCount(2);
        impact.ImpactedNodes.Should().Contain(n => n.Id == "F-002" && n.Relation == "dependent");
    }

    [Fact]
    public void Analyze_TransitiveImpact_Found()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-002", deps: new[] { "F-001" }),
            Spec("F-003", deps: new[] { "F-002" }) // transitive dependency on F-001
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001");

        impact.ImpactedNodes.Should().HaveCount(2);
        impact.ImpactedNodes.Should().Contain(n => n.Id == "F-003" && n.Relation == "transitive");
    }

    [Fact]
    public void Analyze_DepthLimit_Respected()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-002", deps: new[] { "F-001" }),
            Spec("F-003", deps: new[] { "F-002" }),
            Spec("F-004", deps: new[] { "F-003" })
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001", maxDepth: 1);

        impact.ImpactedNodes.Should().HaveCount(1);
        impact.ImpactedNodes.Should().Contain(n => n.Id == "F-002");
    }

    [Fact]
    public void Analyze_NonExistentSpec_ThrowsException()
    {
        var specs = new List<SpecNode> { Spec("F-001") };
        var graph = _graphBuilder.Build(specs);

        var act = () => _analyzer.Analyze(graph, "F-999");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Analyze_NoImpact_EmptyResult()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-002")  // 완전 독립, 의존 관계 없음
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001");

        impact.ImpactedNodes.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MixedChildAndDependent()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001"),
            Spec("F-010", parent: "F-001"),                    // child
            Spec("F-002", deps: new[] { "F-001" }),            // dependent
            Spec("F-003", parent: "F-001", deps: new[] { "F-001" }) // both
        };

        var graph = _graphBuilder.Build(specs);
        var impact = _analyzer.Analyze(graph, "F-001");

        impact.ImpactedNodes.Should().HaveCount(3);
    }

    private static SpecNode Spec(string id, string? parent = null, string[]? deps = null) => new()
    {
        Id = id,
        Title = $"기능 {id}",
        Description = $"{id} 설명",
        Parent = parent,
        Dependencies = deps?.ToList() ?? new List<string>()
    };
}
