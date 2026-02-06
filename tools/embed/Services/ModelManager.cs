namespace EmbedCLI.Services;

/// <summary>
/// HuggingFace 모델 다운로드 및 캐시 관리
/// </summary>
public class ModelManager
{
    private const string BaseUrl = "https://huggingface.co/Xenova/bge-m3/resolve/main";
    private readonly string _cacheDir;

    public ModelManager(string? cacheDir = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".flow",
            "models",
            "Xenova_bge-m3"
        );
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<ModelPaths> EnsureModelFilesAsync(CancellationToken ct = default)
    {
        var files = new Dictionary<string, string>
        {
            ["model.onnx"] = $"{BaseUrl}/onnx/model.onnx?download=true",
            ["model.onnx_data"] = $"{BaseUrl}/onnx/model.onnx_data?download=true",
            ["tokenizer.json"] = $"{BaseUrl}/tokenizer.json?download=true",
            ["tokenizer_config.json"] = $"{BaseUrl}/tokenizer_config.json?download=true",
            ["config.json"] = $"{BaseUrl}/config.json?download=true"
        };

        foreach (var (fileName, url) in files)
        {
            var localPath = Path.Combine(_cacheDir, fileName);
            
            if (!File.Exists(localPath) || !ValidateFile(localPath, fileName))
            {
                Console.Error.WriteLine($"[INFO] Downloading {fileName}...");
                await DownloadFileAsync(url, localPath, ct);
            }
        }

        return new ModelPaths
        {
            ModelPath = Path.Combine(_cacheDir, "model.onnx"),
            TokenizerPath = Path.Combine(_cacheDir, "tokenizer.json"),
            TokenizerConfigPath = Path.Combine(_cacheDir, "tokenizer_config.json"),
            ConfigPath = Path.Combine(_cacheDir, "config.json")
        };
    }

    private static bool ValidateFile(string path, string fileName)
    {
        var info = new FileInfo(path);
        
        // 기본 크기 검증
        var minSizes = new Dictionary<string, long>
        {
            ["model.onnx"] = 100_000,       // 외부 데이터 사용 (소형 메타)
            ["model.onnx_data"] = 500_000_000, // 최소 500MB
            ["tokenizer.json"] = 1_000_000, // 최소 1MB
            ["tokenizer_config.json"] = 100,
            ["config.json"] = 100
        };

        return info.Exists && info.Length >= minSizes.GetValueOrDefault(fileName, 0);
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken ct)
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
            throw new ModelDownloadException($"Failed to download {url}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ModelDownloadException($"Download timeout for {url}", ex);
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

public class ModelDownloadException : Exception
{
    public ModelDownloadException(string message, Exception? inner = null) 
        : base(message, inner) { }
}
