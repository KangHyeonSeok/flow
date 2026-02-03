using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmbedCLI.Services;

/// <summary>
/// SHA256 해시 기반 임베딩 캐시 서비스
/// </summary>
public class CacheService
{
    private const int MaxEntries = 1000;
    private const long MaxFileSizeBytes = 50_000_000; // 50MB
    private readonly string _cacheFile;
    private CacheData? _cache;

    public CacheService(string? cacheDir = null)
    {
        cacheDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "flow-embed",
            "cache"
        );
        Directory.CreateDirectory(cacheDir);
        _cacheFile = Path.Combine(cacheDir, "embeddings.json");
        LoadCache();
    }

    public CacheResult Get(string text)
    {
        var hash = ComputeHash(text);
        
        if (_cache?.Entries.TryGetValue(hash, out var entry) == true)
        {
            // 히트 카운트 증가
            entry.HitCount++;
            entry.LastAccessed = DateTime.UtcNow;
            
            var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            Console.Error.WriteLine($"[DEBUG] Cache hit: {preview}");
            
            return new CacheResult
            {
                Hit = true,
                Embedding = entry.Embedding
            };
        }

        return new CacheResult { Hit = false };
    }

    public void Set(string text, float[] embedding)
    {
        var hash = ComputeHash(text);
        
        if (_cache == null)
        {
            _cache = new CacheData { Entries = new Dictionary<string, CacheEntry>() };
        }

        // LRU 정책: 최대 개수 초과 시 가장 오래된 항목 제거
        if (_cache.Entries.Count >= MaxEntries)
        {
            var oldest = _cache.Entries
                .OrderBy(e => e.Value.LastAccessed)
                .First();
            _cache.Entries.Remove(oldest.Key);
            Console.Error.WriteLine("[DEBUG] Cache eviction (LRU)");
        }

        var preview = text.Length > 50 ? text.Substring(0, 50) : text;
        _cache.Entries[hash] = new CacheEntry
        {
            TextPreview = preview,
            Embedding = embedding,
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            HitCount = 0
        };

        SaveCache();
    }

    private void LoadCache()
    {
        if (!File.Exists(_cacheFile))
        {
            _cache = new CacheData { Entries = new Dictionary<string, CacheEntry>() };
            return;
        }

        try
        {
            var json = File.ReadAllText(_cacheFile);
            _cache = JsonSerializer.Deserialize(json, CacheJsonContext.Default.CacheData);
            
            Console.Error.WriteLine($"[DEBUG] Cache loaded: {_cache?.Entries.Count ?? 0} entries");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARNING] Cache load failed: {ex.Message}");
            _cache = new CacheData { Entries = new Dictionary<string, CacheEntry>() };
        }
    }

    private void SaveCache()
    {
        try
        {
            // 크기 제한 체크
            var json = JsonSerializer.Serialize(_cache, CacheJsonContext.Default.CacheData);
            if (json.Length > MaxFileSizeBytes)
            {
                Console.Error.WriteLine("[WARNING] Cache size limit exceeded, clearing old entries");
                CleanupOldEntries();
                json = JsonSerializer.Serialize(_cache, CacheJsonContext.Default.CacheData);
            }

            File.WriteAllText(_cacheFile, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARNING] Cache save failed: {ex.Message}");
        }
    }

    private void CleanupOldEntries()
    {
        if (_cache == null) return;

        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var toRemove = _cache.Entries
            .Where(e => e.Value.LastAccessed < cutoffDate)
            .Select(e => e.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _cache.Entries.Remove(key);
        }

        Console.Error.WriteLine($"[DEBUG] Removed {toRemove.Count} old cache entries");
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public void PrintStats()
    {
        if (_cache == null)
        {
            Console.WriteLine("[CACHE STATS] No cache data");
            return;
        }

        var totalHits = _cache.Entries.Sum(e => e.Value.HitCount);
        Console.WriteLine("[CACHE STATS]");
        Console.WriteLine($"  Total entries: {_cache.Entries.Count}");
        Console.WriteLine($"  Total hits: {totalHits}");
        Console.WriteLine($"  Cache file: {_cacheFile}");
        
        if (File.Exists(_cacheFile))
        {
            var fileSize = new FileInfo(_cacheFile).Length;
            Console.WriteLine($"  File size: {fileSize / 1024.0:F2} KB");
        }
    }

    public void Clear()
    {
        _cache = new CacheData { Entries = new Dictionary<string, CacheEntry>() };
        if (File.Exists(_cacheFile))
        {
            File.Delete(_cacheFile);
        }
        Console.WriteLine("[CACHE] Cleared");
    }
}

public class CacheResult
{
    public required bool Hit { get; init; }
    public float[]? Embedding { get; init; }
}

public class CacheData
{
    public Dictionary<string, CacheEntry> Entries { get; set; } = new();
}

public class CacheEntry
{
    public string TextPreview { get; set; } = "";
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public int HitCount { get; set; }
}
