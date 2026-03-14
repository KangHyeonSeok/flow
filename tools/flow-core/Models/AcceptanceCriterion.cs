namespace FlowCore.Models;

/// <summary>검증 가능한 인수 기준</summary>
public sealed class AcceptanceCriterion
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public bool Testable { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string>? RelatedTestIds { get; init; }
}
