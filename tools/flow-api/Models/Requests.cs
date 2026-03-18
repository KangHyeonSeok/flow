namespace FlowApi.Models;

public sealed record CreateSpecRequest(
    string Title,
    string? Type = null,
    string? Problem = null,
    string? Goal = null,
    List<AcRequest>? AcceptanceCriteria = null,
    string? RiskLevel = null);

public sealed record AcRequest(
    string Text,
    bool Testable = true,
    string? Notes = null);

public sealed record UpdateSpecRequest(
    int Version,
    string? Title = null,
    string? Problem = null,
    string? Goal = null,
    List<AcRequest>? AcceptanceCriteria = null,
    string? RiskLevel = null);

public sealed record SubmitEventRequest(
    string Event,
    int Version);

public sealed record SubmitReviewResponseRequest(
    string Type,
    string? SelectedOptionId = null,
    string? Comment = null);
