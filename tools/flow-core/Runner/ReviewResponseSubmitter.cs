using FlowCore.Models;
using FlowCore.Storage;

namespace FlowCore.Runner;

/// <summary>응답 제출 결과 유형</summary>
public enum SubmitResultKind
{
    Success,
    NeedsPlannerReregistration,
    Error
}

/// <summary>ReviewRequest 응답 제출 결과</summary>
public sealed class SubmitResult
{
    public required SubmitResultKind Kind { get; init; }
    public FlowEvent? ProposedEvent { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SpecId { get; init; }
    public ReviewResponse? Response { get; init; }

    /// <summary>검증된 ReviewRequest (아직 Answered로 저장되지 않음, 호출자가 커밋해야 함)</summary>
    public ReviewRequest? ValidatedReviewRequest { get; init; }

    public static SubmitResult Success(FlowEvent ev, ReviewRequest validatedRR) => new()
    {
        Kind = SubmitResultKind.Success,
        ProposedEvent = ev,
        ValidatedReviewRequest = validatedRR
    };

    public static SubmitResult PlannerReregistration(string specId, ReviewResponse response, ReviewRequest validatedRR) => new()
    {
        Kind = SubmitResultKind.NeedsPlannerReregistration,
        SpecId = specId,
        Response = response,
        ValidatedReviewRequest = validatedRR
    };

    public static SubmitResult Fail(string message) => new()
    {
        Kind = SubmitResultKind.Error,
        ErrorMessage = message
    };
}

/// <summary>
/// 사용자 ReviewRequest 응답 제출 서비스.
/// RR에 응답을 기록하고, 후속 이벤트를 결정하여 반환한다.
/// 상태 전이 자체는 호출자(Runner)가 수행한다.
/// </summary>
public sealed class ReviewResponseSubmitter
{
    private readonly IFlowStore _store;

    public ReviewResponseSubmitter(IFlowStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 응답을 검증하고 후속 처리 신호를 반환한다.
    /// RR 상태 변경은 저장하지 않는다 — 호출자가 후속 전이 성공 후 CommitAsync로 커밋해야 한다.
    /// </summary>
    public async Task<SubmitResult> SubmitResponseAsync(
        string specId,
        string reviewRequestId,
        ReviewResponse response,
        CancellationToken ct = default)
    {
        // 1. Load spec
        var spec = await _store.LoadAsync(specId, ct);
        if (spec == null)
            return SubmitResult.Fail($"spec {specId} not found");

        // 2. Load review request
        var rr = await ((IReviewRequestStore)_store).LoadAsync(specId, reviewRequestId, ct);
        if (rr == null)
            return SubmitResult.Fail($"review request {reviewRequestId} not found");

        if (rr.Status != ReviewRequestStatus.Open)
            return SubmitResult.Fail($"review request {reviewRequestId} is not open (status: {rr.Status})");

        // 3. Prepare RR mutation in-memory only (do NOT persist yet)
        rr.Response = response;
        rr.Status = ReviewRequestStatus.Answered;

        // 4. Determine next action based on spec state
        if (spec.State == FlowState.Failed)
        {
            return SubmitResult.PlannerReregistration(specId, response, rr);
        }

        if (spec.State == FlowState.Review && spec.ProcessingStatus == ProcessingStatus.UserReview)
        {
            return SubmitResult.Success(FlowEvent.UserReviewSubmitted, rr);
        }

        return SubmitResult.Fail(
            $"unexpected state for response: {spec.State}/{spec.ProcessingStatus}");
    }

    /// <summary>
    /// 검증된 RR 상태 변경을 영속화한다. 후속 전이가 성공한 후에만 호출해야 한다.
    /// </summary>
    public async Task CommitAsync(ReviewRequest rr, CancellationToken ct = default)
    {
        await ((IReviewRequestStore)_store).SaveAsync(rr, ct);
    }
}
