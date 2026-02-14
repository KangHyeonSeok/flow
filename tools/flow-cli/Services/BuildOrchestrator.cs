using System.Diagnostics;
using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// 빌드 파이프라인의 핵심 오케스트레이터.
/// 프로젝트 타입 감지 → 모듈 확인/다운로드 → 단계별 실행(lint→build→test→run) → 결과 집계.
/// </summary>
public class BuildOrchestrator
{
    private readonly PathResolver _paths;
    private readonly BuildModuleManager _moduleManager;
    private readonly ScriptRunner _scriptRunner;

    /// <summary>
    /// 프로젝트 타입 자동 감지 규칙 (하드코딩 기본값).
    /// 모듈 미설치 상태에서도 감지 가능하도록.
    /// </summary>
    private static readonly Dictionary<string, string[]> DefaultDetectRules = new()
    {
        ["unity"] = new[] { "Assets", "ProjectSettings" },
        ["flutter"] = new[] { "pubspec.yaml" },
        ["python"] = new[] { "setup.py", "pyproject.toml", "requirements.txt" },
        ["node"] = new[] { "package.json" },
        ["dotnet"] = new[] { "*.sln", "*.csproj" }
    };

    /// <summary>
    /// 감지 우선순위 순서. 더 구체적인 프로젝트 타입을 먼저 검사.
    /// </summary>
    private static readonly string[] DetectOrder = { "unity", "flutter", "dotnet", "python", "node" };

    public BuildOrchestrator(PathResolver paths, BuildModuleManager moduleManager, ScriptRunner scriptRunner)
    {
        _paths = paths;
        _moduleManager = moduleManager;
        _scriptRunner = scriptRunner;
    }

    /// <summary>
    /// 프로젝트 디렉토리를 분석하여 플랫폼 타입을 감지한다.
    /// </summary>
    /// <param name="projectPath">프로젝트 루트 경로</param>
    /// <returns>감지된 플랫폼 이름 (unity/python/node/dotnet/flutter) 또는 "unknown"</returns>
    public string DetectPlatform(string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return "unknown";

        foreach (var platform in DetectOrder)
        {
            // 1. 설치된 모듈의 detect 규칙 우선 사용
            var manifest = _moduleManager.LoadManifest(platform);
            if (manifest?.Detect?.Files is { Count: > 0 } files)
            {
                if (AllFilesExist(projectPath, files))
                    return platform;
                continue;
            }

            // 2. 기본 감지 규칙 사용
            if (DefaultDetectRules.TryGetValue(platform, out var defaultFiles))
            {
                if (AnyFileExists(projectPath, defaultFiles))
                    return platform;
            }
        }

        return "unknown";
    }

    /// <summary>
    /// 모듈 설치 여부를 확인하고, 미설치 시 사용자에게 다운로드 여부를 확인한다.
    /// </summary>
    /// <returns>성공 시 null, 실패 시 에러 메시지</returns>
    public string? CheckAndInstallModule(string platform)
    {
        if (_moduleManager.IsInstalled(platform))
            return null;

        // 사용자 확인
        Console.Write($"{platform} 빌드 모듈이 설치되지 않았습니다. 다운로드 하시겠습니까? (Y/n): ");
        string? input;
        try
        {
            input = Console.ReadLine();
        }
        catch
        {
            // CI 환경 등에서 stdin이 없는 경우
            input = "n";
        }

        var answer = string.IsNullOrWhiteSpace(input) ? "y" : input.Trim().ToLowerInvariant();
        if (answer != "y" && answer != "yes")
        {
            return $"{platform} 빌드 모듈이 필요합니다. 모듈 없이는 빌드를 진행할 수 없습니다.";
        }

        Console.WriteLine($"{platform} 빌드 모듈 다운로드 중...");
        var error = _moduleManager.DownloadModule(platform);
        if (error != null)
        {
            return $"모듈 다운로드 실패: {error}";
        }

        Console.WriteLine($"{platform} 빌드 모듈 설치 완료.");
        return null;
    }

    /// <summary>
    /// 요청된 빌드 단계를 순서대로 실행하고 결과를 집계한다.
    /// 실패 시 즉시 중단 (fail-fast).
    /// </summary>
    /// <param name="projectPath">프로젝트 루트 경로</param>
    /// <param name="platform">플랫폼 이름</param>
    /// <param name="steps">실행할 단계 목록 (순서대로: lint, build, test, run)</param>
    /// <param name="extraParams">추가 파라미터 (스크립트에 전달)</param>
    /// <param name="timeoutMs">각 단계 타임아웃 (밀리초)</param>
    public BuildResult Execute(
        string projectPath,
        string platform,
        IReadOnlyList<string> steps,
        Dictionary<string, string>? extraParams = null,
        int timeoutMs = 300_000)
    {
        var result = new BuildResult
        {
            Platform = platform,
            ProjectPath = projectPath
        };

        var sw = Stopwatch.StartNew();

        foreach (var step in steps)
        {
            var scriptPath = _moduleManager.GetScriptPath(platform, step);
            if (string.IsNullOrEmpty(scriptPath))
            {
                result.Steps.Add(new BuildStepResult
                {
                    Success = false,
                    Action = step,
                    Platform = platform,
                    ExitCode = -1,
                    Stderr = $"'{step}' 단계의 스크립트를 찾을 수 없습니다."
                });
                result.Success = false;
                result.Message = $"'{step}' 단계에서 실패: 스크립트 없음";
                result.TotalDurationMs = sw.ElapsedMilliseconds;
                return result;
            }

            if (!File.Exists(scriptPath))
            {
                result.Steps.Add(new BuildStepResult
                {
                    Success = false,
                    Action = step,
                    Platform = platform,
                    ExitCode = -1,
                    Stderr = $"스크립트 파일이 존재하지 않습니다: {scriptPath}"
                });
                result.Success = false;
                result.Message = $"'{step}' 단계에서 실패: 스크립트 파일 없음";
                result.TotalDurationMs = sw.ElapsedMilliseconds;
                return result;
            }

            // 스크립트 파라미터 조합
            var parameters = new Dictionary<string, string>
            {
                ["ProjectPath"] = projectPath
            };

            if (extraParams != null)
            {
                foreach (var (key, value) in extraParams)
                    parameters[key] = value;
            }

            var stepSw = Stopwatch.StartNew();
            var scriptResult = _scriptRunner.RunScript(scriptPath, parameters, timeoutMs: timeoutMs);
            stepSw.Stop();

            var stepResult = new BuildStepResult
            {
                Success = scriptResult.IsSuccess,
                Action = step,
                Platform = platform,
                DurationMs = stepSw.ElapsedMilliseconds,
                ExitCode = scriptResult.ExitCode,
                Stdout = scriptResult.Stdout,
                Stderr = scriptResult.Stderr
            };

            result.Steps.Add(stepResult);

            if (!scriptResult.IsSuccess)
            {
                result.Success = false;
                result.Message = scriptResult.TimedOut
                    ? $"'{step}' 단계에서 타임아웃"
                    : $"'{step}' 단계에서 실패 (exit code: {scriptResult.ExitCode})";
                result.TotalDurationMs = sw.ElapsedMilliseconds;
                return result;
            }
        }

        sw.Stop();
        result.Success = true;
        result.TotalDurationMs = sw.ElapsedMilliseconds;
        result.Message = $"모든 단계 완료 ({steps.Count}개)";
        return result;
    }

    /// <summary>
    /// 모든 파일/디렉토리가 존재하는지 확인 (manifest detect 규칙용).
    /// </summary>
    private static bool AllFilesExist(string basePath, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var fullPath = Path.Combine(basePath, file);
            if (file.EndsWith('/') || file.EndsWith('\\'))
            {
                if (!Directory.Exists(fullPath.TrimEnd('/', '\\')))
                    return false;
            }
            else
            {
                if (!File.Exists(fullPath))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 하나라도 파일/디렉토리가 존재하는지 확인 (기본 감지 규칙용).
    /// 글로브 패턴 (*.ext)도 지원.
    /// </summary>
    private static bool AnyFileExists(string basePath, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.Contains('*'))
            {
                // 글로브 패턴 — 디렉토리에서 매칭 파일 탐색
                try
                {
                    if (Directory.GetFiles(basePath, pattern, SearchOption.TopDirectoryOnly).Length > 0)
                        return true;
                }
                catch { /* ignore */ }
            }
            else if (pattern.EndsWith('/') || pattern.EndsWith('\\'))
            {
                if (Directory.Exists(Path.Combine(basePath, pattern.TrimEnd('/', '\\'))))
                    return true;
            }
            else
            {
                var fullPath = Path.Combine(basePath, pattern);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return true;
            }
        }
        return false;
    }
}
