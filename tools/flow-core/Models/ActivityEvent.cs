using System.Text.Json;

namespace FlowCore.Models;

/// <summary>Activity log 이벤트</summary>
public sealed class ActivityEvent
{
    public required string EventId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string SpecId { get; init; }
    public required string Actor { get; init; }
    public required ActivityAction Action { get; init; }
    public required string SourceType { get; init; }
    public required int BaseVersion { get; init; }
    public required FlowState State { get; init; }
    public required ProcessingStatus ProcessingStatus { get; init; }
    public required string Message { get; init; }

    // optional
    public string? AssignmentId { get; init; }
    public string? ReviewRequestId { get; init; }
    public string? CorrelationId { get; init; }
    public JsonElement? Payload { get; init; }
}
