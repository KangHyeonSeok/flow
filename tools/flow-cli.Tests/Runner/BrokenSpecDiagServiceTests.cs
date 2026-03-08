using System.Text.Json;
using FlowCLI.Services.Runner;

namespace FlowCLI.Tests.Runner;

public class BrokenSpecDiagServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _specCacheDir;
    private readonly string _specsDir;
    private readonly string _diagCachePath;
    private readonly BrokenSpecDiagService _service;

    public BrokenSpecDiagServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-broken-diag-{Guid.NewGuid():N}");
        _specCacheDir = Path.Combine(_tempDir, "spec-cache");
        _specsDir = Path.Combine(_specCacheDir, "specs");
        _diagCachePath = Path.Combine(_specCacheDir, "broken-spec-diag.json");

        Directory.CreateDirectory(_specsDir);

        var log = new RunnerLogService(_tempDir, "logs", "broken-spec-diag-test");
        _service = new BrokenSpecDiagService(_specCacheDir, log);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void MarkResolved_ResolvesStaleRecordsForSameSpecId()
    {
        var localPath = WriteSpec("F-031");
        WriteCache(new BrokenSpecDiagCache
        {
            Records =
            [
                new BrokenSpecDiagRecord
                {
                    SpecId = "F-031",
                    FilePath = @"d:\Projects\flow\.flow\spec-cache\specs\F-031.json",
                    Status = "unresolved",
                    DetectedAt = DateTime.UtcNow.ToString("o")
                }
            ]
        });

        _service.MarkResolved(localPath);

        var cache = ReadCache();
        var record = Assert.Single(cache.Records);
        Assert.Equal("resolved", record.Status);
        Assert.Equal(localPath, record.FilePath);
        Assert.Equal("F-031", record.SpecId);
        Assert.NotNull(record.ResolvedAt);
    }

    [Fact]
    public void GetUnresolved_NormalizesForeignPathsToCurrentSpecDirectory()
    {
        var localPath = WriteSpec("F-032");
        WriteCache(new BrokenSpecDiagCache
        {
            Records =
            [
                new BrokenSpecDiagRecord
                {
                    SpecId = "F-032",
                    FilePath = @"d:\Projects\flow\.flow\spec-cache\specs\F-032.json",
                    Status = "unresolved",
                    DetectedAt = DateTime.UtcNow.ToString("o"),
                    RepairAttempts = 1
                }
            ]
        });

        var unresolved = _service.GetUnresolved(_specsDir);

        var record = Assert.Single(unresolved);
        Assert.Equal(localPath, record.FilePath);
        Assert.Equal(1, record.RepairAttempts);

        var cache = ReadCache();
        Assert.Equal(localPath, Assert.Single(cache.Records).FilePath);
    }

    private string WriteSpec(string specId)
    {
        var path = Path.Combine(_specsDir, $"{specId}.json");
        File.WriteAllText(path, """{"id":"placeholder"}""");
        return path;
    }

    private void WriteCache(BrokenSpecDiagCache cache)
    {
        Directory.CreateDirectory(_specCacheDir);
        File.WriteAllText(_diagCachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private BrokenSpecDiagCache ReadCache()
    {
        return JsonSerializer.Deserialize<BrokenSpecDiagCache>(File.ReadAllText(_diagCachePath))!;
    }
}
