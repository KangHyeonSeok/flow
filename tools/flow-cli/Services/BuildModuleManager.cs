using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// 빌드 모듈의 설치 여부 확인, GitHub Releases에서 다운로드, manifest.json 로딩을 담당.
/// 모듈은 .flow/build/{platform}/에 설치된다.
/// </summary>
public class BuildModuleManager
{
    private readonly PathResolver _paths;
    private readonly string _repo;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    static BuildModuleManager()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "flow-cli");
    }

    public BuildModuleManager(PathResolver paths, string repo = "KangHyeonSeok/flow")
    {
        _paths = paths;
        _repo = repo;
    }

    /// <summary>
    /// 특정 플랫폼의 빌드 모듈이 설치되어 있는지 확인한다.
    /// </summary>
    public bool IsInstalled(string platform)
    {
        var manifestPath = _paths.GetBuildManifestPath(platform);
        return File.Exists(manifestPath);
    }

    /// <summary>
    /// 특정 플랫폼의 빌드 모듈 디렉토리 전체 경로를 반환한다.
    /// </summary>
    public string GetModulePath(string platform)
        => _paths.GetBuildModulePath(platform);

    /// <summary>
    /// manifest.json을 읽어 BuildManifest 객체로 반환한다.
    /// </summary>
    public BuildManifest? LoadManifest(string platform)
    {
        var manifestPath = _paths.GetBuildManifestPath(platform);
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<BuildManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 특정 스크립트의 전체 경로를 반환한다.
    /// manifest의 scripts 경로를 모듈 디렉토리와 결합.
    /// </summary>
    public string? GetScriptPath(string platform, string action)
    {
        var manifest = LoadManifest(platform);
        if (manifest?.Scripts == null)
            return null;

        var scriptRelative = action.ToLowerInvariant() switch
        {
            "lint" => manifest.Scripts.Lint,
            "build" => manifest.Scripts.Build,
            "test" => manifest.Scripts.Test,
            "run" => manifest.Scripts.Run,
            _ => null
        };

        if (string.IsNullOrEmpty(scriptRelative))
            return null;

        return Path.Combine(GetModulePath(platform), scriptRelative);
    }

    /// <summary>
    /// GitHub Releases에서 빌드 모듈을 다운로드하여 설치한다.
    /// </summary>
    /// <returns>성공 시 null, 실패 시 에러 메시지</returns>
    public string? DownloadModule(string platform)
    {
        try
        {
            // 1. 최신 릴리즈 정보 조회
            var apiUrl = $"https://api.github.com/repos/{_repo}/releases/latest";
            var response = HttpClient.GetAsync(apiUrl).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return $"GitHub API 요청 실패: {response.StatusCode}";

            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var release = JsonSerializer.Deserialize<JsonElement>(responseBody);

            // 2. build-module-{platform}.zip 에셋 찾기
            var assetName = $"build-module-{platform}.zip";
            string? downloadUrl = null;

            if (release.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        name.GetString()?.Equals(assetName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        if (asset.TryGetProperty("browser_download_url", out var url))
                        {
                            downloadUrl = url.GetString();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                return $"릴리즈에서 '{assetName}'을 찾을 수 없습니다. 모듈이 아직 배포되지 않았을 수 있습니다.";

            // 3. ZIP 다운로드
            var tempZip = Path.Combine(Path.GetTempPath(), $"flow-build-module-{platform}-{Guid.NewGuid():N}.zip");
            try
            {
                var zipBytes = HttpClient.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(tempZip, zipBytes);

                // 4. 모듈 디렉토리에 압축 해제
                var modulePath = GetModulePath(platform);
                if (Directory.Exists(modulePath))
                    Directory.Delete(modulePath, true);

                Directory.CreateDirectory(modulePath);
                ZipFile.ExtractToDirectory(tempZip, modulePath, overwriteFiles: true);

                // 5. manifest.json 존재 확인
                if (!IsInstalled(platform))
                    return $"모듈 설치 후 manifest.json을 찾을 수 없습니다. ZIP 구조가 올바르지 않을 수 있습니다.";

                return null; // 성공
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }
        catch (HttpRequestException ex)
        {
            return $"다운로드 실패: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"모듈 설치 실패: {ex.Message}";
        }
    }

    /// <summary>
    /// 로컬 ZIP 파일에서 빌드 모듈을 설치한다 (오프라인/테스트용).
    /// </summary>
    /// <returns>성공 시 null, 실패 시 에러 메시지</returns>
    public string? InstallFromZip(string platform, string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath))
                return $"ZIP 파일을 찾을 수 없습니다: {zipPath}";

            var modulePath = GetModulePath(platform);
            if (Directory.Exists(modulePath))
                Directory.Delete(modulePath, true);

            Directory.CreateDirectory(modulePath);
            ZipFile.ExtractToDirectory(zipPath, modulePath, overwriteFiles: true);

            if (!IsInstalled(platform))
                return "manifest.json을 찾을 수 없습니다. ZIP 구조가 올바르지 않습니다.";

            return null;
        }
        catch (Exception ex)
        {
            return $"ZIP 설치 실패: {ex.Message}";
        }
    }
}
