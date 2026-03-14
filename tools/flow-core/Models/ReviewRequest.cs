namespace FlowCore.Models;

/// <summary>사용자 검토 요청</summary>
public sealed class ReviewRequest
{
    public required string Id { get; init; }
    public required string SpecId { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reason { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string>? Questions { get; init; }
    public ReviewRequestStatus Status { get; set; } = ReviewRequestStatus.Open;
    public DateTimeOffset? DeadlineAt { get; init; }
    public ReviewResponse? Response { get; set; }
    public string? Resolution { get; set; }
}

/// <summary>사용자 검토 응답</summary>
public sealed class ReviewResponse
{
    public required string RespondedBy { get; init; }
    public required DateTimeOffset RespondedAt { get; init; }
    public required ReviewResponseType Type { get; init; }
    public string? SelectedOptionId { get; init; }
    public string? Comment { get; init; }
}

/// <summary>검토 응답 유형</summary>
public enum ReviewResponseType
{
    ApproveOption,
    RejectWithComment,
    PartialEditApprove
}
