namespace FlowCore.Models;

/// <summary>Spec 의존 관계</summary>
public sealed class Dependency
{
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public IReadOnlyList<string> Blocks { get; init; } = [];
}
