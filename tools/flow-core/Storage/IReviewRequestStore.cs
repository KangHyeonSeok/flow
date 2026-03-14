using FlowCore.Models;

namespace FlowCore.Storage;

/// <summary>ReviewRequest 저장소. 경로는 reviewRequest.SpecId로 결정한다.</summary>
public interface IReviewRequestStore
{
    Task<ReviewRequest?> LoadAsync(string specId, string reviewRequestId, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewRequest>> LoadBySpecAsync(string specId, CancellationToken ct = default);
    Task<SaveResult> SaveAsync(ReviewRequest reviewRequest, CancellationToken ct = default);
}
