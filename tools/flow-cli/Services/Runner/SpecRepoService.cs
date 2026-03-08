using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowCLI.Services.Runner;

/// <summary>
/// 스펙 저장소를 ~/.flow/specs/{저장소이름}/ 경로에 clone/pull하여 관리하는 서비스.
/// RunnerConfig.SpecRepository(git URL)에서 저장소 이름을 추출하고
/// 최신 스펙을 동기화한다.
/// </summary>
public class SpecRepoService
{
    // 로컬 변경분(local) 중 원격이 건드리지 않은 경우에만 복구할 필드 목록.
    // working 상태/runner 휘발성 필드는 더 이상 spec JSON에 기록되지 않으므로 제거됨.
    private static readonly string[][] SafeRestorePaths =
    [
        ["status"],
        ["metadata", "userPriorityHint"],
        ["metadata", "lastError"],
        ["metadata", "lastErrorAt"],
        ["metadata", "lastCompletedAt"],
        ["metadata", "lastCompletedBy"],
        ["metadata", "lastVerifiedAt"],
        ["metadata", "lastVerifiedBy"],
        ["metadata", "verificationSource"],
        // 사용자 답변 보존 필드: feedbackStore가 로컬에서 수정하므로 git pull 시 덮어쓰기 방지
        ["metadata", "questions"],
        ["metadata", "lastAnsweredAt"],
        ["metadata", "reviewDisposition"],
        ["metadata", "plannerState"],
        ["metadata", "questionStatus"],
        ["metadata", "review", "questions"],
        ["metadata", "review", "additionalInformationRequests"],
    ];

    private readonly RunnerLogService _log;
    private readonly string _specRepository;
    private readonly string _specBranch;
    private readonly string _repoName;
    private readonly string _localPath;
    private string _specsDir;

    /// <summary>
    /// 스펙 저장소 서비스 생성.
    /// </summary>
    /// <param name="specRepository">git URL (예: https://github.com/user/flow-spec.git)</param>
    /// <param name="specBranch">브랜치 이름 (기본: main)</param>
    /// <param name="localCachePath">로컬 캐시 경로 (예: .flow/spec-cache/)</param>
    /// <param name="log">로그 서비스</param>
    public SpecRepoService(string specRepository, string specBranch, string localCachePath, RunnerLogService log)
    {
        _log = log;
        _specRepository = specRepository;
        _specBranch = specBranch;

        // git URL에서 저장소 이름 추출
        _repoName = ExtractRepoName(specRepository);

        // 로컬 캐시 경로 (.flow/spec-cache/)
        _localPath = localCachePath;

        // 스펙 JSON 디렉토리: 저장소 루트의 specs/ 또는 docs/specs/ 구조를 런타임에 감지
        _specsDir = ResolveSpecsDir(_localPath);
    }

    /// <summary>
    /// 스펙 저장소 서비스 생성 (하위 호환 - localCachePath 없음).
    /// ~/.flow/specs/{저장소이름}/ 경로를 사용한다.
    /// </summary>
    [Obsolete("localCachePath를 명시적으로 지정하는 생성자를 사용하세요.")]
    public SpecRepoService(string specRepository, string specBranch, RunnerLogService log)
    {
        _log = log;
        _specRepository = specRepository;
        _specBranch = specBranch;

        // git URL에서 저장소 이름 추출
        _repoName = ExtractRepoName(specRepository);

        // ~/.flow/specs/{저장소이름}/
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localPath = Path.Combine(userHome, ".flow", "specs", _repoName);

        // 스펙 JSON 디렉토리: 저장소 루트의 specs/ 또는 docs/specs/ 구조를 런타임에 감지
        _specsDir = ResolveSpecsDir(_localPath);
    }

    /// <summary>로컬 체크아웃 경로 (예: ~/.flow/specs/flow-spec/)</summary>
    public string LocalPath => _localPath;

    /// <summary>스펙 JSON 디렉토리 (예: ~/.flow/specs/flow-spec/specs/)</summary>
    public string SpecsDir => _specsDir;

    /// <summary>추출된 저장소 이름</summary>
    public string RepoName => _repoName;

    /// <summary>
    /// 저장소 루트에서 스펙 JSON 디렉토리를 결정한다.
    /// specs/ → docs/specs/ 순으로 탐색하여 존재하는 경로를 반환.
    /// 초기 clone 전에는 specs/ 경로를 기본값으로 반환.
    /// </summary>
    private static string ResolveSpecsDir(string localPath)
    {
        // 1. 저장소 루트의 specs/ (현재 flow-spec 저장소 구조)
        var rootSpecs = Path.Combine(localPath, "specs");
        if (Directory.Exists(rootSpecs) && Directory.GetFiles(rootSpecs, "*.json").Length > 0)
            return rootSpecs;

        // 2. docs/specs/ 구조 (레거시 또는 다른 저장소 구조)
        var docsSpecs = Path.Combine(localPath, "docs", "specs");
        if (Directory.Exists(docsSpecs) && Directory.GetFiles(docsSpecs, "*.json").Length > 0)
            return docsSpecs;

        // 3. clone 전이거나 아직 비어 있으면 specs/ 기본값 반환
        return rootSpecs;
    }

    /// <summary>
    /// 스펙 저장소를 동기화한다.
    /// 최초: git clone, 이후: git pull.
    /// 실패 시 기존 캐시로 폴백.
    /// sync 완료 후 스펙 디렉토리 경로를 재확인한다 (첫 clone 이후 구조 반영).
    /// </summary>
    /// <returns>성공 여부</returns>
    public async Task<bool> SyncAsync()
    {
        try
        {
            bool success;
            if (IsCloned())
            {
                success = await PullAsync();
            }
            else
            {
                success = await CloneAsync();
            }

            // clone/pull 완료 후 specs 디렉토리 경로 재확인 (첫 clone 이후 구조가 달라질 수 있음)
            if (success)
                _specsDir = ResolveSpecsDir(_localPath);

            return success;
        }
        catch (Exception ex)
        {
            // 기존 캐시가 있으면 폴백 — 오래된 스펙으로 실행될 수 있음을 ERROR로 명시
            if (Directory.Exists(_specsDir) && Directory.GetFiles(_specsDir, "*.json").Length > 0)
            {
                _log.Error("spec-repo", $"스펙 저장소 동기화 예외, 이전 캐시로 폴백 (오래된 스펙 사용 주의): {ex.Message}");
                return true;
            }

            _log.Error("spec-repo", $"스펙 저장소 동기화 예외, 사용 가능한 캐시 없음: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 이미 clone되어 있는지 확인
    /// </summary>
    private bool IsCloned()
    {
        return Directory.Exists(Path.Combine(_localPath, ".git"));
    }

    /// <summary>
    /// git clone 실행
    /// </summary>
    private async Task<bool> CloneAsync()
    {
        _log.Info("spec-repo", $"스펙 저장소 clone 시작: {_specRepository} → {_localPath}");

        // 부모 디렉토리 생성
        var parentDir = Path.GetDirectoryName(_localPath);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        var result = await RunGitAsync($"clone --branch {_specBranch} --single-branch \"{_specRepository}\" \"{_localPath}\"");
        if (!result.Success)
        {
            _log.Error("spec-repo", $"git clone 실패: {result.Error}");
            return false;
        }

        _log.Info("spec-repo", $"스펙 저장소 clone 완료: {_localPath}");
        return true;
    }

    /// <summary>
    /// git pull 실행
    /// </summary>
    private async Task<bool> PullAsync()
    {
        _log.Info("spec-repo", $"스펙 저장소 pull 시작: {_localPath}");

        var statusResult = await RunGitAsync("status --porcelain", _localPath);
        if (!statusResult.Success)
        {
            _log.Warn("spec-repo", $"git status 실패: {statusResult.Error}");
        }
        else if (!string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return await PullWithRecoveryAsync();
        }

        var result = await RunGitAsync($"pull origin {_specBranch} --rebase=false", _localPath);
        if (!result.Success)
        {
            // pull 실패 시 기존 캐시가 있으면 폴백 — 오래된 스펙으로 실행될 수 있음을 ERROR로 명시
            if (Directory.Exists(_specsDir) && Directory.GetFiles(_specsDir, "*.json").Length > 0)
            {
                _log.Error("spec-repo", $"git pull 실패 — 이전 캐시로 폴백 (오래된 스펙 사용 주의): {result.Error}");
                return true;
            }

            _log.Error("spec-repo", $"git pull 실패, 사용 가능한 캐시 없음: {result.Error}");
            return false;
        }

        _log.Info("spec-repo", "스펙 저장소 pull 완료");
        return true;
    }

    private async Task<bool> PullWithRecoveryAsync()
    {
        var stashMessage = $"flow-spec-sync-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var stashResult = await RunGitAsync($"stash push --include-untracked -m \"{stashMessage}\"", _localPath);
        if (!stashResult.Success)
        {
            _log.Warn("spec-repo", $"git stash 실패: {stashResult.Error}");
            return false;
        }

        if (stashResult.Output.Contains("No local changes to save", StringComparison.OrdinalIgnoreCase))
        {
            var cleanPull = await RunGitAsync($"pull origin {_specBranch} --rebase=false", _localPath);
            if (!cleanPull.Success)
            {
                _log.Warn("spec-repo", $"git pull 실패: {cleanPull.Error}");
                return false;
            }

            _log.Info("spec-repo", "스펙 저장소 pull 완료");
            return true;
        }

        const string stashRef = "stash@{0}";
        var pullResult = await RunGitAsync($"pull origin {_specBranch} --rebase=false", _localPath);
        if (!pullResult.Success)
        {
            _log.Warn("spec-repo", $"git pull 실패: {pullResult.Error}");
            await RestoreStashAfterFailedPullAsync(stashRef);
            return false;
        }

        try
        {
            var recovery = await RestoreSpecsFromStashAsync(stashRef);
            if (recovery.RestoredFileCount > 0 || recovery.RestoredFieldCount > 0 || recovery.SkippedFileCount > 0)
            {
                _log.Info(
                    "spec-repo",
                    $"stash 복구 완료: files={recovery.RestoredFileCount}, fields={recovery.RestoredFieldCount}, skipped={recovery.SkippedFileCount}"
                );
            }
        }
        catch (Exception ex)
        {
            _log.Warn("spec-repo", $"stash 복구 중 오류 발생 (pull은 완료됨, 원격 스펙 사용): {ex.Message}");
        }
        finally
        {
            var dropResult = await RunGitAsync($"stash drop {stashRef}", _localPath);
            if (!dropResult.Success)
            {
                _log.Warn("spec-repo", $"stash drop 실패: {dropResult.Error}");
            }
        }

        _log.Info("spec-repo", "스펙 저장소 pull 완료");
        return true;
    }

    private async Task RestoreStashAfterFailedPullAsync(string stashRef)
    {
        var restoreResult = await RunGitAsync($"stash pop --index {stashRef}", _localPath);
        if (!restoreResult.Success)
        {
            _log.Warn("spec-repo", $"pull 실패 후 stash 복원 실패: {restoreResult.Error}");
        }
    }

    private async Task<SpecRecoverySummary> RestoreSpecsFromStashAsync(string stashRef)
    {
        var summary = new SpecRecoverySummary();
        var changedFiles = await GetStashedFilesAsync(stashRef);

        foreach (var relativePath in changedFiles)
        {
            if (!IsSpecJsonPath(relativePath))
                continue;

            try
            {
                await RestoreSingleSpecFromStashAsync(stashRef, relativePath, summary);
            }
            catch (Exception ex)
            {
                _log.Warn("spec-repo", $"stash 복구 중 파일 오류 (건너뜀): {relativePath} — {ex.Message}");
                summary.SkippedFileCount++;
            }
        }

        return summary;
    }

    private async Task RestoreSingleSpecFromStashAsync(string stashRef, string relativePath, SpecRecoverySummary summary)
    {
        var currentPath = Path.Combine(_localPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var baseJson = await TryGetGitFileTextAsync($"{stashRef}^1", relativePath);
        var localJson = await TryGetGitFileTextAsync(stashRef, relativePath);
        var currentJson = File.Exists(currentPath) ? await File.ReadAllTextAsync(currentPath) : null;

        if (localJson is null)
        {
            summary.SkippedFileCount++;
            return;
        }

        if (currentJson is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
            await File.WriteAllTextAsync(currentPath, localJson);
            summary.RestoredFileCount++;
            return;
        }

        var merge = MergeSpecJson(baseJson, localJson, currentJson);
        if (!merge.Changed)
            return;

        await File.WriteAllTextAsync(currentPath, merge.MergedJson);
        summary.RestoredFieldCount += merge.RestoredPathCount;
    }

    private async Task<List<string>> GetStashedFilesAsync(string stashRef)
    {
        var result = await RunGitAsync($"stash show --name-only --format= --include-untracked {stashRef}", _localPath);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<string?> TryGetGitFileTextAsync(string gitRef, string relativePath)
    {
        var result = await RunGitAsync($"show {gitRef}:{relativePath}", _localPath);
        return result.Success ? result.Output : null;
    }

    private static bool IsSpecJsonPath(string relativePath)
    {
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;

        return relativePath.StartsWith("specs/", StringComparison.Ordinal)
            || relativePath.StartsWith("docs/specs/", StringComparison.Ordinal);
    }

    internal static SpecJsonMergeResult MergeSpecJson(string? baseJson, string localJson, string currentJson)
    {
        JsonNode? baseNode = ParseJson(baseJson);
        var localNode = ParseJson(localJson) ?? throw new InvalidOperationException("로컬 stash 스펙 JSON 파싱 실패");
        var currentNode = ParseJson(currentJson) ?? throw new InvalidOperationException("현재 스펙 JSON 파싱 실패");

        if (localNode is not JsonObject || currentNode is not JsonObject)
            throw new InvalidOperationException("스펙 JSON 루트는 object여야 합니다.");

        var mergedNode = currentNode.DeepClone();
        var restoredPathCount = 0;

        foreach (var path in SafeRestorePaths)
        {
            var baseValue = GetPathValue(baseNode, path);
            var localValue = GetPathValue(localNode, path);
            var currentValue = GetPathValue(currentNode, path);

            var localChanged = !JsonDeepEquals(baseValue, localValue);
            var remoteChanged = !JsonDeepEquals(baseValue, currentValue);
            if (!localChanged || remoteChanged)
                continue;

            SetPathValue(mergedNode, path, localValue?.DeepClone());
            restoredPathCount++;
        }

        if (restoredPathCount == 0)
            return new SpecJsonMergeResult(false, currentJson, 0);

        return new SpecJsonMergeResult(true, mergedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), restoredPathCount);
    }

    private static JsonNode? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonNode.Parse(json);
    }

    private static JsonNode? GetPathValue(JsonNode? root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static void SetPathValue(JsonNode root, IReadOnlyList<string> path, JsonNode? value)
    {
        if (path.Count == 0 || root is not JsonObject current)
            return;

        for (var i = 0; i < path.Count - 1; i++)
        {
            var segment = path[i];
            if (current[segment] is not JsonObject child)
            {
                if (value is null)
                    return;

                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }

        var leaf = path[^1];
        if (value is null)
        {
            current.Remove(leaf);
            return;
        }

        current[leaf] = value;
    }

    private static bool JsonDeepEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;

        return JsonNode.DeepEquals(left, right);
    }

    /// <summary>
    /// 스펙 저장소에 변경사항을 커밋하고 push한다.
    /// Runner가 스펙 상태를 업데이트한 후 호출.
    /// </summary>
    public async Task<bool> CommitAndPushAsync(string message)
    {
        var addResult = await RunGitAsync("add -A", _localPath);
        if (!addResult.Success)
        {
            _log.Error("spec-repo", $"git add 실패: {addResult.Error}");
            return false;
        }

        // 변경사항이 있는지 확인
        var diffResult = await RunGitAsync("diff --cached --quiet", _localPath);
        if (diffResult.Success)
        {
            _log.Info("spec-repo", "스펙 변경사항 없음, 커밋 스킵");
            return true;
        }

        var commitResult = await RunGitAsync($"commit -m \"{message}\"", _localPath);
        if (!commitResult.Success)
        {
            _log.Error("spec-repo", $"git commit 실패: {commitResult.Error}");
            return false;
        }

        var pushResult = await RunGitAsync($"push origin {_specBranch}", _localPath);
        if (!pushResult.Success)
        {
            _log.Warn("spec-repo", $"git push 실패: {pushResult.Error}");
            return false;
        }

        _log.Info("spec-repo", "스펙 변경사항 commit & push 완료");
        return true;
    }

    /// <summary>
    /// git URL에서 저장소 이름을 추출한다.
    /// 예: "https://github.com/user/flow-spec.git" → "flow-spec"
    ///     "git@github.com:user/my-specs.git" → "my-specs"
    ///     "/local/path/to/repo" → "repo"
    /// </summary>
    internal static string ExtractRepoName(string gitUrl)
    {
        // .git 접미사 제거
        var url = gitUrl.TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];

        // 마지막 경로 세그먼트 추출
        var lastSlash = url.LastIndexOfAny(['/', '\\', ':']);
        var name = lastSlash >= 0 ? url[(lastSlash + 1)..] : url;

        // 빈 문자열 방지
        return string.IsNullOrWhiteSpace(name) ? "spec-repo" : name;
    }

    private async Task<GitResult> RunGitAsync(string arguments, string? workDir = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workDir ?? Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // UTF-8 인코딩 명시: 한글 등 멀티바이트 문자가 포함된 파일 내용을 정확히 읽기 위함
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new GitResult
            {
                Success = process.ExitCode == 0,
                Output = stdout.Trim(),
                Error = stderr.Trim(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new GitResult
            {
                Success = false,
                Error = $"git 실행 실패: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    private class GitResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public int ExitCode { get; set; }
    }

    private sealed class SpecRecoverySummary
    {
        public int RestoredFileCount { get; set; }
        public int RestoredFieldCount { get; set; }
        public int SkippedFileCount { get; set; }
    }

    internal sealed record SpecJsonMergeResult(bool Changed, string MergedJson, int RestoredPathCount);
}
