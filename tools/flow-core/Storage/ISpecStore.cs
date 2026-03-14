using FlowCore.Models;

namespace FlowCore.Storage;

/// <summary>Spec 저장소. 생성 시 projectId를 고정한다.</summary>
public interface ISpecStore
{
    Task<Spec?> LoadAsync(string specId, CancellationToken ct = default);
    Task<IReadOnlyList<Spec>> LoadAllAsync(CancellationToken ct = default);
    Task<SaveResult> SaveAsync(Spec spec, int expectedVersion, CancellationToken ct = default);

    /// <summary>spec과 관련 파일(assignment, review request, activity)을 모두 삭제한다.</summary>
    Task DeleteSpecAsync(string specId, CancellationToken ct = default);
}
