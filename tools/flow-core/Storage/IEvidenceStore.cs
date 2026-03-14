using FlowCore.Models;

namespace FlowCore.Storage;

/// <summary>Evidence manifest 저장소</summary>
public interface IEvidenceStore
{
    /// <summary>evidence manifest를 저장한다. 디렉토리가 없으면 생성한다.</summary>
    Task SaveManifestAsync(EvidenceManifest manifest, CancellationToken ct = default);

    /// <summary>특정 run의 evidence manifest를 로드한다.</summary>
    Task<EvidenceManifest?> LoadManifestAsync(string specId, string runId, CancellationToken ct = default);

    /// <summary>spec의 모든 evidence manifest를 로드한다 (최신순).</summary>
    Task<IReadOnlyList<EvidenceManifest>> LoadBySpecAsync(string specId, CancellationToken ct = default);

    /// <summary>evidence 디렉토리의 절대 경로를 반환한다.</summary>
    string GetEvidenceDir(string specId, string runId);
}
