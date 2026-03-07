using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// StatusPropagator 상태 전파 테스트
/// </summary>
public class StatusPropagatorTests
{
    private readonly GraphBuilder _graphBuilder = new();
    private readonly StatusPropagator _propagator = new();

    [Fact]
    public void Propagate_DependentsGetNeedsReview()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "verified"),
            Spec("F-002", status: "verified", deps: new[] { "F-001" }),
            Spec("F-003", status: "working", deps: new[] { "F-001" })
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-001", "working");

        changes.Should().Contain(c => c.Id == "F-002" && c.NewStatus == "needs-review");
        changes.Should().Contain(c => c.Id == "F-003" && c.NewStatus == "needs-review");
    }

    [Fact]
    public void Propagate_DeprecatedNotAffected()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "working"),
            Spec("F-002", status: "deprecated", deps: new[] { "F-001" })
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-001", "working");

        changes.Should().NotContain(c => c.Id == "F-002");
    }

    [Fact]
    public void Propagate_ParentChanges_AreNotAggregated()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "draft"),
            Spec("F-010", status: "verified", parent: "F-001"),
            Spec("F-011", status: "verified", parent: "F-001")
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-010", "verified");

        changes.Should().NotContain(c => c.Id == "F-001");
    }

    [Fact]
    public void Propagate_MixedChildStatuses_DoNotAffectParentStatus()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "draft"),
            Spec("F-010", status: "verified", parent: "F-001"),
            Spec("F-011", status: "draft", parent: "F-001")
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-010", "verified");

        changes.Should().NotContain(c => c.Id == "F-001");
    }

    [Fact]
    public void Propagate_ParentNeedsReview_IsNotAutoManaged()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "working"),
            Spec("F-010", status: "verified", parent: "F-001"),
            Spec("F-011", status: "needs-review", parent: "F-001")
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-010", "verified");

        changes.Should().NotContain(c => c.Id == "F-001");
    }

    [Fact]
    public void Propagate_NoChanges_WhenAlreadyNeedsReview()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "working"),
            Spec("F-002", status: "needs-review", deps: new[] { "F-001" })
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-001", "working");

        changes.Should().NotContain(c => c.Id == "F-002");
    }

    [Fact]
    public void Propagate_NoChanges_WhenNoRelations()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "working"),
            Spec("F-002", status: "working")  // 독립
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-001", "working");

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Propagate_DoneNotAffected()
    {
        var specs = new List<SpecNode>
        {
            Spec("F-001", status: "working"),
            Spec("F-002", status: "done", deps: new[] { "F-001" })
        };

        var graph = _graphBuilder.Build(specs);
        var changes = _propagator.Propagate(graph, "F-001", "working");

        // done 상태인 스펙은 needs-review로 전환되지 않음
        changes.Should().NotContain(c => c.Id == "F-002");
    }

    [Fact]
    private static SpecNode Spec(string id, string status = "draft", string? parent = null, string[]? deps = null) => new()
    {
        Id = id,
        Title = $"기능 {id}",
        Description = $"{id} 설명",
        Status = status,
        Parent = parent,
        Dependencies = deps?.ToList() ?? new List<string>()
    };
}
