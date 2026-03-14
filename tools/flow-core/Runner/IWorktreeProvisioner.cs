using FlowCore.Models;

namespace FlowCore.Runner;

/// <summary>
/// Worktree lifecycle 추상화.
/// Implementation/TestValidation assignment에 격리된 작업 디렉토리를 제공한다.
/// flow-cli의 GitWorktreeService를 이 인터페이스의 구현체로 감싸는 방향.
/// </summary>
public interface IWorktreeProvisioner
{
    /// <summary>specId 기반 worktree를 생성하거나 기존 worktree를 재사용한다.</summary>
    Task<WorktreeProvisionResult> CreateAsync(string specId, CancellationToken ct = default);

    /// <summary>specId에 해당하는 worktree를 정리한다. best-effort.</summary>
    Task CleanupAsync(string specId, CancellationToken ct = default);
}

/// <summary>Worktree provisioning 결과</summary>
public sealed class WorktreeProvisionResult
{
    public bool Success { get; init; }
    public string? WorktreeId { get; init; }
    public string? Path { get; init; }
    public string? Branch { get; init; }
}
