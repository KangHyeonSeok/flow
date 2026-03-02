using System.Diagnostics;

namespace FlowCLI.Services.Runner;

/// <summary>
/// 스펙 저장소를 ~/.flow/specs/{저장소이름}/ 경로에 clone/pull하여 관리하는 서비스.
/// RunnerConfig.SpecRepository(git URL)에서 저장소 이름을 추출하고
/// 최신 스펙을 동기화한다.
/// </summary>
public class SpecRepoService
{
    private readonly RunnerLogService _log;
    private readonly string _specRepository;
    private readonly string _specBranch;
    private readonly string _repoName;
    private readonly string _localPath;
    private readonly string _specsDir;

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

        // 스펙 JSON이 있는 하위 디렉토리 (docs/specs/ 구조)
        _specsDir = Path.Combine(_localPath, "docs", "specs");
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

        // 스펙 JSON이 있는 하위 디렉토리
        _specsDir = Path.Combine(_localPath, "docs", "specs");
    }

    /// <summary>로컬 체크아웃 경로 (예: ~/.flow/specs/flow-spec/)</summary>
    public string LocalPath => _localPath;

    /// <summary>스펙 JSON 디렉토리 (예: ~/.flow/specs/flow-spec/specs/)</summary>
    public string SpecsDir => _specsDir;

    /// <summary>추출된 저장소 이름</summary>
    public string RepoName => _repoName;

    /// <summary>
    /// 스펙 저장소를 동기화한다.
    /// 최초: git clone, 이후: git pull.
    /// 실패 시 기존 캐시로 폴백.
    /// </summary>
    /// <returns>성공 여부</returns>
    public async Task<bool> SyncAsync()
    {
        try
        {
            if (IsCloned())
            {
                return await PullAsync();
            }
            else
            {
                return await CloneAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Error("spec-repo", $"스펙 저장소 동기화 예외: {ex.Message}");

            // 기존 캐시가 있으면 폴백
            if (Directory.Exists(_specsDir) && Directory.GetFiles(_specsDir, "*.json").Length > 0)
            {
                _log.Warn("spec-repo", "이전 캐시로 폴백합니다.");
                return true;
            }

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

        var result = await RunGitAsync($"pull origin {_specBranch} --rebase=false", _localPath);
        if (!result.Success)
        {
            _log.Warn("spec-repo", $"git pull 실패: {result.Error}");

            // pull 실패 시 기존 캐시가 있으면 폴백
            if (Directory.Exists(_specsDir) && Directory.GetFiles(_specsDir, "*.json").Length > 0)
            {
                _log.Warn("spec-repo", "이전 캐시로 폴백합니다.");
                return true;
            }

            return false;
        }

        _log.Info("spec-repo", "스펙 저장소 pull 완료");
        return true;
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
                    CreateNoWindow = true
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
}
