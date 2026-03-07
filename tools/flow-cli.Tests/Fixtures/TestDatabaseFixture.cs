using FlowCLI.Models;
using FlowCLI.Services;

namespace FlowCLI.Tests.Fixtures;

/// <summary>
/// Shared test fixture that creates an isolated in-memory SQLite database
/// with sample records for testing DatabaseService.Query().
/// Implements IDisposable for cleanup of temporary DB files.
/// </summary>
public class TestDatabaseFixture : IDisposable
{
    private readonly string _tempDir;
    private bool _disposed;

    public string TestDbPath { get; }
    public IReadOnlyList<TaskRecord> SampleRecords { get; }

    public TestDatabaseFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Create .flow directory so PathResolver protected ctor works
        Directory.CreateDirectory(Path.Combine(_tempDir, ".flow"));

        TestDbPath = Path.Combine(_tempDir, "test.db");

        SampleRecords = CreateSampleRecords();
        SeedDatabase();
    }

    /// <summary>
    /// Creates a fresh DatabaseService instance pointing at the test database.
    /// </summary>
    public DatabaseService CreateService()
    {
        var resolver = new TestPathResolver(TestDbPath, _tempDir);
        return new DatabaseService(resolver);
    }

    private static IReadOnlyList<TaskRecord> CreateSampleRecords()
    {
        return new List<TaskRecord>
        {
            new()
            {
                Content = "CLI command implementation",
                CanonicalTags = "cli,command,interface",
                FeatureName = "cli_feature",
                CommitId = "abc123",
                StateAtCreation = "COMPLETED",
                Metadata = "{}",
                PlanText = "Plan for CLI feature",
                ResultText = "Successfully implemented"
            },
            new()
            {
                Content = "Database schema migration",
                CanonicalTags = "database,schema,migration",
                FeatureName = "db_migration",
                CommitId = "def456",
                StateAtCreation = "EXECUTING",
                Metadata = "{\"priority\": \"high\"}",
                PlanText = "Plan for DB migration",
                ResultText = "Migration completed"
            },
            new()
            {
                Content = "Special chars: <tag> & \"quotes\" 'apostrophe'",
                CanonicalTags = "special,chars,edge-case",
                FeatureName = "special_chars_feature",
                CommitId = "ghi789",
                StateAtCreation = "IDLE",
                Metadata = "{\"type\": \"edge-case\"}",
                PlanText = "Plan with <special> chars",
                ResultText = "Handled special chars"
            },
            new()
            {
                Content = "유니코드 테스트 문서",
                CanonicalTags = "unicode,한글,テスト",
                FeatureName = "unicode_feature",
                CommitId = "jkl012",
                StateAtCreation = "VALIDATING",
                Metadata = "{\"lang\": \"multi\"}",
                PlanText = "유니코드 계획서",
                ResultText = "유니코드 결과"
            },
            new()
            {
                Content = "UPPERCASE CONTENT TEST",
                CanonicalTags = "UPPER,CASE,TAGS",
                FeatureName = "UPPER_FEATURE",
                CommitId = "MNO345",
                StateAtCreation = "BLOCKED",
                Metadata = "{\"case\": \"upper\"}",
                PlanText = "UPPERCASE PLAN",
                ResultText = "UPPERCASE RESULT"
            }
        };
    }

    private void SeedDatabase()
    {
        using var service = CreateService();
        foreach (var record in SampleRecords)
        {
            service.AddDocument(record);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Clean up temp directory and DB file
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup; temp files will be purged by OS
            }
        }

        _disposed = true;
    }
}
