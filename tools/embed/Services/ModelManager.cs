using System.Diagnostics;

namespace EmbedCLI.Services;

/// <summary>
/// HuggingFace 모델 다운로드 및 캐시 관리
/// </summary>
public class ModelManager
{
    private const string BaseUrl = "https://huggingface.co/Xenova/bge-m3/resolve/main";
    private readonly string _cacheDir;
    private readonly string _downloadLockPath;
    private static readonly TimeSpan DownloadLockStaleAfter = TimeSpan.FromHours(1);
    private static readonly TimeSpan ForegroundWaitTimeout = TimeSpan.FromMinutes(30);

    public ModelManager(string? cacheDir = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".flow",
            "models",
            "Xenova_bge-m3"
        );
        Directory.CreateDirectory(_cacheDir);
        _downloadLockPath = Path.Combine(_cacheDir, "download.lock");
    }

    public async Task<ModelEnsureResult> EnsureModelFilesAsync(
        bool backgroundDownload = false,
        bool ignoreExistingLock = false,
        CancellationToken ct = default)
    {
        var files = GetModelFiles();

        if (backgroundDownload)
        {
            if (AreFilesReady(files))
            {
                return new ModelEnsureResult
                {
                    Status = ModelAvailability.Ready,
                    Paths = BuildPaths()
                };
            }

            StartBackgroundDownloadIfNeeded(files);
            return new ModelEnsureResult { Status = ModelAvailability.Preparing };
        }

        if (IsDownloadInProgress() && !ignoreExistingLock)
        {
            await WaitForDownloadAsync(files, ct);
            if (AreFilesReady(files))
            {
                return new ModelEnsureResult
                {
                    Status = ModelAvailability.Ready,
                    Paths = BuildPaths()
                };
            }
        }

        await DownloadMissingFilesAsync(files, ct);
        return new ModelEnsureResult
        {
            Status = ModelAvailability.Ready,
            Paths = BuildPaths()
        };
    }

    private Dictionary<string, string> GetModelFiles() => new()
    {
        ["model_int8.onnx"] = $"{BaseUrl}/onnx/model_int8.onnx?download=true",
        ["tokenizer.json"] = $"{BaseUrl}/tokenizer.json?download=true",
        ["tokenizer_config.json"] = $"{BaseUrl}/tokenizer_config.json?download=true",
        ["config.json"] = $"{BaseUrl}/config.json?download=true"
    };

    private ModelPaths BuildPaths() => new()
    {
        ModelPath = Path.Combine(_cacheDir, "model_int8.onnx"),
        TokenizerPath = Path.Combine(_cacheDir, "tokenizer.json"),
        TokenizerConfigPath = Path.Combine(_cacheDir, "tokenizer_config.json"),
        ConfigPath = Path.Combine(_cacheDir, "config.json")
    };

    private bool AreFilesReady(Dictionary<string, string> files)
    {
        foreach (var fileName in files.Keys)
        {
            var localPath = Path.Combine(_cacheDir, fileName);
            if (!File.Exists(localPath) || !ValidateFile(localPath, fileName))
                return false;
        }
        return true;
    }

    private bool IsDownloadInProgress()
    {
        if (!File.Exists(_downloadLockPath))
            return false;

        if (!IsDownloadProcessAlive())
        {
            TryClearDownloadLock();
            CleanupPartialDownloads();
            return false;
        }

        if (IsDownloadLockStale())
        {
            TryClearDownloadLock();
            CleanupPartialDownloads();
            return false;
        }

        return true;
    }

    private bool IsDownloadProcessAlive()
    {
        try
        {
            var content = File.ReadAllText(_downloadLockPath);
            var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (!int.TryParse(parts[0], out var pid))
                return false;

            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private bool IsDownloadLockStale()
    {
        var lastWrite = File.GetLastWriteTimeUtc(_downloadLockPath);
        return (DateTime.UtcNow - lastWrite) > DownloadLockStaleAfter;
    }

    private void TouchDownloadLock()
    {
        Directory.CreateDirectory(_cacheDir);
        File.WriteAllText(_downloadLockPath, $"{Environment.ProcessId} {DateTime.UtcNow:O}");
    }

    private void TryClearDownloadLock()
    {
        try
        {
            if (File.Exists(_downloadLockPath))
                File.Delete(_downloadLockPath);
        }
        catch
        {
            // ignore lock cleanup failures
        }
    }

    private void CleanupPartialDownloads()
    {
        try
        {
            foreach (var tmpFile in Directory.GetFiles(_cacheDir, "*.tmp"))
                File.Delete(tmpFile);
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private async Task WaitForDownloadAsync(Dictionary<string, string> files, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start) < ForegroundWaitTimeout)
        {
            ct.ThrowIfCancellationRequested();

            if (AreFilesReady(files))
                return;

            if (!File.Exists(_downloadLockPath) || IsDownloadLockStale() || !IsDownloadProcessAlive())
            {
                TryClearDownloadLock();
                CleanupPartialDownloads();
                return;
            }

            await Task.Delay(1000, ct);
        }
    }

    private void StartBackgroundDownloadIfNeeded(Dictionary<string, string> files)
    {
        if (IsDownloadInProgress())
            return;

        if (AreFilesReady(files))
            return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "download-model --force",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            TouchDownloadLock();
            Process.Start(startInfo);
            Console.Error.WriteLine("[INFO] Model download started in background");
        }
        catch
        {
            TryClearDownloadLock();
        }
    }

    private async Task DownloadMissingFilesAsync(Dictionary<string, string> files, CancellationToken ct)
    {
        TouchDownloadLock();
        try
        {
            foreach (var (fileName, url) in files)
            {
                var localPath = Path.Combine(_cacheDir, fileName);
                if (!File.Exists(localPath) || !ValidateFile(localPath, fileName))
                {
                    Console.Error.WriteLine($"[INFO] Downloading {fileName}...");
                    await DownloadFileAsync(url, localPath, ct);
                }
            }
        }
        finally
        {
            TryClearDownloadLock();
        }
    }

    private static bool ValidateFile(string path, string fileName)
    {
        var info = new FileInfo(path);
        
        // 기본 크기 검증
        var minSizes = new Dictionary<string, long>
        {
            ["model_int8.onnx"] = 500_000_000, // 최소 500MB
            ["tokenizer.json"] = 1_000_000, // 최소 1MB
            ["tokenizer_config.json"] = 100,
            ["config.json"] = 100
        };

        return info.Exists && info.Length >= minSizes.GetValueOrDefault(fileName, 0);
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[81920];  // 80KB buffer for faster downloads
            var totalRead = 0L;
            var lastProgressUpdate = DateTime.UtcNow;
            var lastLockUpdate = DateTime.UtcNow;

            var tmpFile = destination + ".tmp";
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(tmpFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fileStream = File.Create(tmpFile);
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                // Update progress every 500ms
                if (totalBytes > 0 && (DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds > 500)
                {
                    var progress = (int)((totalRead * 100) / totalBytes);
                    var downloadedMB = totalRead / (1024.0 * 1024.0);
                    var totalMB = totalBytes / (1024.0 * 1024.0);
                    Console.Error.Write($"\r  Progress: {progress}% ({downloadedMB:F1} MB / {totalMB:F1} MB)    ");
                    lastProgressUpdate = DateTime.UtcNow;
                }

                if ((DateTime.UtcNow - lastLockUpdate).TotalSeconds > 30)
                {
                    TouchDownloadLock();
                    lastLockUpdate = DateTime.UtcNow;
                }
            }

            Console.Error.WriteLine($"\r  Progress: 100% ({totalRead / (1024.0 * 1024.0):F1} MB)                    ");
            
            // Close stream before moving file
            await fileStream.FlushAsync(ct);
            fileStream.Close();
            
            File.Move(tmpFile, destination, overwrite: true);
            Console.Error.WriteLine($"[INFO] Downloaded: {Path.GetFileName(destination)}");
        }
        catch (HttpRequestException ex)
        {
            TryDeleteTempFile(destination);
            throw new ModelDownloadException($"Failed to download {url}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            TryDeleteTempFile(destination);
            throw new ModelDownloadException($"Download timeout for {url}", ex);
        }
        catch
        {
            TryDeleteTempFile(destination);
            throw;
        }
    }

    private static void TryDeleteTempFile(string destination)
    {
        try
        {
            var tmpFile = destination + ".tmp";
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}

public class ModelPaths
{
    public required string ModelPath { get; init; }
    public required string TokenizerPath { get; init; }
    public required string TokenizerConfigPath { get; init; }
    public required string ConfigPath { get; init; }
}

public enum ModelAvailability
{
    Ready,
    Preparing
}

public class ModelEnsureResult
{
    public required ModelAvailability Status { get; init; }
    public ModelPaths? Paths { get; init; }
}

public class ModelDownloadException : Exception
{
    public ModelDownloadException(string message, Exception? inner = null) 
        : base(message, inner) { }
}
