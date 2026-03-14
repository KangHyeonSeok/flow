using FlowCore.Models;
using FlowCore.Rules;
using FluentAssertions;

namespace FlowCore.Tests;

public class DependencyEvaluatorTests
{
    private static SpecSnapshot MakeSnapshot(string id,
        FlowState state = FlowState.Draft,
        ProcessingStatus ps = ProcessingStatus.Pending,
        IReadOnlyList<string>? dependsOn = null) => new()
    {
        Id = id, ProjectId = "proj-001",
        State = state, ProcessingStatus = ps,
        DependsOn = dependsOn ?? []
    };

    // ── Evaluate ──

    [Fact]
    public void Failed_EmitsDependencyFailed_ToAllDownstream()
    {
        var input = new DependencyInput
        {
            ChangedSpec = MakeSnapshot("upstream", FlowState.Failed, ProcessingStatus.Error),
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.InProgress,
            DownstreamSpecs = [MakeSnapshot("ds-1"), MakeSnapshot("ds-2")]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().HaveCount(2);
        effects.Should().OnlyContain(e => e.Event == FlowEvent.DependencyFailed);
    }

    [Fact]
    public void OnHold_EmitsDependencyBlocked_ToAllDownstream()
    {
        var input = new DependencyInput
        {
            ChangedSpec = MakeSnapshot("upstream", FlowState.Implementation, ProcessingStatus.OnHold),
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.InProgress,
            DownstreamSpecs = [MakeSnapshot("ds-1")]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().ContainSingle()
            .Which.Event.Should().Be(FlowEvent.DependencyBlocked);
    }

    [Fact]
    public void Error_EmitsDependencyBlocked()
    {
        var input = new DependencyInput
        {
            ChangedSpec = MakeSnapshot("upstream", FlowState.Implementation, ProcessingStatus.Error),
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.InProgress,
            DownstreamSpecs = [MakeSnapshot("ds-1")]
        };

        var effects = DependencyEvaluator.Evaluate(input);
        effects.Should().ContainSingle()
            .Which.Event.Should().Be(FlowEvent.DependencyBlocked);
    }

    [Fact]
    public void Recovery_EmitsDependencyResolved_OnlyToOnHoldDownstream()
    {
        var changedUpstream = MakeSnapshot("upstream", FlowState.Implementation, ProcessingStatus.Pending);

        var input = new DependencyInput
        {
            ChangedSpec = changedUpstream,
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.OnHold,
            DownstreamSpecs =
            [
                MakeSnapshot("ds-onhold", ps: ProcessingStatus.OnHold, dependsOn: ["upstream"]),
                MakeSnapshot("ds-pending", ps: ProcessingStatus.Pending, dependsOn: ["upstream"])
            ],
            AllUpstreamSpecs = [changedUpstream]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().ContainSingle();
        effects[0].TargetSpecId.Should().Be("ds-onhold");
        effects[0].Event.Should().Be(FlowEvent.DependencyResolved);
    }

    [Fact]
    public void Recovery_MultiUpstream_DoesNotResolveIfOtherUpstreamStillBlocked()
    {
        // upstream-A 가 정상 복귀했지만, upstream-B 는 여전히 OnHold
        var upstreamA = MakeSnapshot("upstream-A", FlowState.Implementation, ProcessingStatus.Pending);
        var upstreamB = MakeSnapshot("upstream-B", FlowState.Implementation, ProcessingStatus.OnHold);

        var input = new DependencyInput
        {
            ChangedSpec = upstreamA,
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.OnHold,
            DownstreamSpecs =
            [
                MakeSnapshot("ds-multi", ps: ProcessingStatus.OnHold,
                    dependsOn: ["upstream-A", "upstream-B"])
            ],
            AllUpstreamSpecs = [upstreamA, upstreamB]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().BeEmpty("다른 upstream(upstream-B)이 아직 OnHold이므로 resolve하면 안 된다");
    }

    [Fact]
    public void Recovery_MultiUpstream_ResolvesWhenAllUpstreamsNormal()
    {
        // upstream-A, upstream-B 모두 정상
        var upstreamA = MakeSnapshot("upstream-A", FlowState.Implementation, ProcessingStatus.Pending);
        var upstreamB = MakeSnapshot("upstream-B", FlowState.Implementation, ProcessingStatus.InProgress);

        var input = new DependencyInput
        {
            ChangedSpec = upstreamA,
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.OnHold,
            DownstreamSpecs =
            [
                MakeSnapshot("ds-multi", ps: ProcessingStatus.OnHold,
                    dependsOn: ["upstream-A", "upstream-B"])
            ],
            AllUpstreamSpecs = [upstreamA, upstreamB]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().ContainSingle();
        effects[0].TargetSpecId.Should().Be("ds-multi");
        effects[0].Event.Should().Be(FlowEvent.DependencyResolved);
    }

    [Fact]
    public void Recovery_MultiUpstream_DoesNotResolveIfOtherUpstreamFailed()
    {
        // upstream-A 정상 복귀, upstream-B Failed
        var upstreamA = MakeSnapshot("upstream-A", FlowState.Implementation, ProcessingStatus.Pending);
        var upstreamB = MakeSnapshot("upstream-B", FlowState.Failed, ProcessingStatus.Error);

        var input = new DependencyInput
        {
            ChangedSpec = upstreamA,
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.OnHold,
            DownstreamSpecs =
            [
                MakeSnapshot("ds-multi", ps: ProcessingStatus.OnHold,
                    dependsOn: ["upstream-A", "upstream-B"])
            ],
            AllUpstreamSpecs = [upstreamA, upstreamB]
        };

        var effects = DependencyEvaluator.Evaluate(input);

        effects.Should().BeEmpty("다른 upstream(upstream-B)이 Failed이므로 resolve하면 안 된다");
    }

    [Fact]
    public void NoStateChange_NoEffects()
    {
        var input = new DependencyInput
        {
            ChangedSpec = MakeSnapshot("upstream", FlowState.Implementation, ProcessingStatus.InProgress),
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.Pending,
            DownstreamSpecs = [MakeSnapshot("ds-1")]
        };

        var effects = DependencyEvaluator.Evaluate(input);
        effects.Should().BeEmpty();
    }

    [Fact]
    public void NoDownstream_NoEffects()
    {
        var input = new DependencyInput
        {
            ChangedSpec = MakeSnapshot("upstream", FlowState.Failed, ProcessingStatus.Error),
            PreviousState = FlowState.Implementation,
            PreviousProcessingStatus = ProcessingStatus.InProgress,
            DownstreamSpecs = []
        };

        var effects = DependencyEvaluator.Evaluate(input);
        effects.Should().BeEmpty();
    }

    // ── DetectCycles ──

    [Fact]
    public void NoCycles_ReturnsEmpty()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", Array.Empty<string>())
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().BeEmpty();
    }

    [Fact]
    public void SimpleCycle_Detected()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["B"]),
            ("B", ["A"])
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().ContainSingle();
        cycles[0].SpecIds.Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void ThreeNodeCycle_Detected()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", ["A"])
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().ContainSingle();
        cycles[0].SpecIds.Should().HaveCount(3);
    }

    [Fact]
    public void SelfCycle_Detected()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["A"])
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().ContainSingle();
    }

    [Fact]
    public void DisconnectedGraph_NoCycles()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", Array.Empty<string>()),
            ("B", Array.Empty<string>()),
            ("C", ["D"]),
            ("D", Array.Empty<string>())
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().BeEmpty();
    }

    [Fact]
    public void DetectCycles_NoDuplicates()
    {
        // A→B→A 는 B→A→B와 같은 cycle — 하나만 보고되어야 한다
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["B"]),
            ("B", ["A"])
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().HaveCount(1, "같은 cycle이 중복 보고되면 안 된다");
    }

    [Fact]
    public void DetectCycles_Deterministic()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("C", ["A"]),
            ("A", ["B"]),
            ("B", ["C"])
        };

        var cycles1 = DependencyEvaluator.DetectCycles(graph);
        var cycles2 = DependencyEvaluator.DetectCycles(graph);

        cycles1.Should().HaveCount(1);
        cycles2.Should().HaveCount(1);
        cycles1[0].SpecIds.Should().BeEquivalentTo(cycles2[0].SpecIds,
            opts => opts.WithStrictOrdering(),
            "같은 입력에 대해 항상 같은 순서로 cycle을 반환해야 한다");
    }

    [Fact]
    public void DetectCycles_TwoIndependentCycles_BothDetected()
    {
        var graph = new List<(string, IReadOnlyList<string>)>
        {
            ("A", ["B"]),
            ("B", ["A"]),
            ("X", ["Y"]),
            ("Y", ["X"])
        };

        var cycles = DependencyEvaluator.DetectCycles(graph);
        cycles.Should().HaveCount(2);
    }
}
