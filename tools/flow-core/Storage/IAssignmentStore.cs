using FlowCore.Models;

namespace FlowCore.Storage;

/// <summary>Assignment 저장소. 경로는 assignment.SpecId로 결정한다.</summary>
public interface IAssignmentStore
{
    Task<Assignment?> LoadAsync(string specId, string assignmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> LoadBySpecAsync(string specId, CancellationToken ct = default);
    Task<SaveResult> SaveAsync(Assignment assignment, CancellationToken ct = default);
}
