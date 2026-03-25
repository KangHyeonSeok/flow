namespace FlowApi.Models;

public sealed record CreateSpecRequest(
    string Title,
    string? Type = null,
    string? EpicId = null,
    string? Problem = null,
    string? Goal = null,
    List<AcRequest>? AcceptanceCriteria = null,
    string? RiskLevel = null,
    string? Context = null,
    string? NonGoals = null,
    string? ImplementationNotes = null,
    string? TestPlan = null);

public sealed record AcRequest(
    string Text,
    bool Testable = true,
    string? Notes = null);

public sealed record UpdateSpecRequest(
    int Version,
    string? EpicId = null,
    string? Title = null,
    string? Problem = null,
    string? Goal = null,
    List<AcRequest>? AcceptanceCriteria = null,
    string? RiskLevel = null,
    string? Context = null,
    string? NonGoals = null,
    string? ImplementationNotes = null,
    string? TestPlan = null);

public sealed record SubmitEventRequest(
    string Event,
    int Version);

public sealed record SubmitValidationRequest(
    int Version,
    string? Outcome = null);

public sealed record SubmitReviewResponseRequest(
    string Type,
    string? SelectedOptionId = null,
    string? Comment = null);

public sealed record UpdateProjectDocumentRequest(
    int Version,
    string? Title = null,
    string? Summary = null,
    string? Problem = null,
    List<string>? Goals = null,
    List<string>? NonGoals = null,
    List<string>? ContextAndConstraints = null,
    List<string>? ArchitectureOverview = null);

public sealed record UpdateEpicDocumentRequest(
    int Version,
    string? Title = null,
    string? Summary = null,
    string? Problem = null,
    string? Goal = null,
    List<string>? Scope = null,
    List<string>? NonGoals = null,
    List<string>? SuccessCriteria = null,
    List<string>? ChildSpecIds = null,
    List<string>? Dependencies = null,
    List<string>? RelatedDocs = null,
    string? Owner = null,
    string? Priority = null);
