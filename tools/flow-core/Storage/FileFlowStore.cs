using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowCore.Storage;

/// <summary>파일 기반 IFlowStore 구현</summary>
public sealed class FileFlowStore : IFlowStore
{
    private readonly string _specsRoot;
    private readonly string _archivedRoot;

    public FileFlowStore(string projectId, string? flowHome = null)
    {
        var home = flowHome
            ?? Environment.GetEnvironmentVariable("FLOW_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow");
        _specsRoot = Path.Combine(home, "projects", projectId, "specs");
        _archivedRoot = Path.Combine(home, "projects", projectId, "specs-archived");
    }

    // ── Path helpers ──

    private string SpecDir(string specId) => Path.Combine(_specsRoot, specId);
    private string ArchivedSpecDir(string specId) => Path.Combine(_archivedRoot, specId);
    private string ArchivedSpecFile(string specId) => Path.Combine(ArchivedSpecDir(specId), "spec.json");
    private string SpecFile(string specId) => Path.Combine(SpecDir(specId), "spec.json");
    private string AssignmentFile(string specId, string asgId) =>
        Path.Combine(SpecDir(specId), "assignments", $"{asgId}.json");
    private string AssignmentsDir(string specId) => Path.Combine(SpecDir(specId), "assignments");
    private string ReviewRequestFile(string specId, string rrId) =>
        Path.Combine(SpecDir(specId), "review-requests", $"{rrId}.json");
    private string ReviewRequestsDir(string specId) => Path.Combine(SpecDir(specId), "review-requests");
    private string ActivityDir(string specId) => Path.Combine(SpecDir(specId), "activity");

    // ── ISpecStore ──

    public async Task<Spec?> LoadAsync(string specId, CancellationToken ct = default)
    {
        var path = SpecFile(specId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return SpecPruner.Deserialize(json);
    }

    public async Task<IReadOnlyList<Spec>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_specsRoot))
            return [];

        var specs = new List<Spec>();
        foreach (var dir in Directory.GetDirectories(_specsRoot))
        {
            var specId = Path.GetFileName(dir);
            var spec = await LoadAsync(specId, ct);
            if (spec is not null)
                specs.Add(spec);
        }
        return specs;
    }

    public async Task<SaveResult> SaveAsync(Spec spec, int expectedVersion, CancellationToken ct = default)
    {
        var path = SpecFile(spec.Id);

        // CAS 검증
        if (File.Exists(path))
        {
            var existingJson = await File.ReadAllTextAsync(path, ct);
            var existing = SpecPruner.Deserialize(existingJson);
            if (existing.Version != expectedVersion)
                return SaveResult.ConflictAt(existing.Version);
        }
        else if (expectedVersion != 0)
        {
            return SaveResult.ConflictAt(0);
        }

        var json = SpecPruner.Serialize(spec);
        await AtomicWriteAsync(path, json, ct);
        return SaveResult.Ok();
    }

    public Task DeleteSpecAsync(string specId, CancellationToken ct = default)
    {
        var dir = SpecDir(specId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public async Task ArchiveAsync(string specId, CancellationToken ct = default)
    {
        var srcDir = SpecDir(specId);
        if (!Directory.Exists(srcDir)) return;

        var destDir = ArchivedSpecDir(specId);

        try
        {
            Directory.CreateDirectory(destDir);

            // Copy all files recursively
            var srcFiles = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories);
            foreach (var srcFile in srcFiles)
            {
                var relativePath = Path.GetRelativePath(srcDir, srcFile);
                var destFile = Path.Combine(destDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(srcFile, destFile, overwrite: true);
            }

            // Verify: all source files must exist in dest
            foreach (var srcFile in srcFiles)
            {
                var relativePath = Path.GetRelativePath(srcDir, srcFile);
                var destFile = Path.Combine(destDir, relativePath);
                if (!File.Exists(destFile))
                    throw new InvalidOperationException(
                        $"Archive verification failed for {specId}: missing {relativePath}");
            }
        }
        catch
        {
            // Rollback: remove partial archive
            try
            {
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);
            }
            catch { /* best-effort rollback */ }

            throw;
        }

        // Delete original only after full copy + verification
        Directory.Delete(srcDir, recursive: true);
    }

    public async Task<Spec?> LoadArchivedAsync(string specId, CancellationToken ct = default)
    {
        var path = ArchivedSpecFile(specId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return SpecPruner.Deserialize(json);
    }

    // ── IAssignmentStore ──

    public async Task<Assignment?> LoadAsync(string specId, string assignmentId, CancellationToken ct = default)
    {
        var path = AssignmentFile(specId, assignmentId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<Assignment>(json, FlowJsonOptions.Default);
    }

    public async Task<IReadOnlyList<Assignment>> LoadBySpecAsync(string specId, CancellationToken ct = default)
    {
        var dir = AssignmentsDir(specId);
        if (!Directory.Exists(dir)) return [];

        var assignments = new List<Assignment>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var asg = JsonSerializer.Deserialize<Assignment>(json, FlowJsonOptions.Default);
            if (asg is not null)
                assignments.Add(asg);
        }
        return assignments;
    }

    public async Task<SaveResult> SaveAsync(Assignment assignment, CancellationToken ct = default)
    {
        var path = AssignmentFile(assignment.SpecId, assignment.Id);
        var json = JsonSerializer.Serialize(assignment, FlowJsonOptions.Default);
        await AtomicWriteAsync(path, json, ct);
        return SaveResult.Ok();
    }

    // ── IReviewRequestStore (명시적 구현: LoadAsync/LoadBySpecAsync 시그니처 충돌 방지) ──

    async Task<ReviewRequest?> IReviewRequestStore.LoadAsync(string specId, string reviewRequestId, CancellationToken ct)
    {
        var path = ReviewRequestFile(specId, reviewRequestId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ReviewRequest>(json, FlowJsonOptions.Default);
    }

    async Task<IReadOnlyList<ReviewRequest>> IReviewRequestStore.LoadBySpecAsync(string specId, CancellationToken ct)
    {
        var dir = ReviewRequestsDir(specId);
        if (!Directory.Exists(dir)) return [];

        var requests = new List<ReviewRequest>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var rr = JsonSerializer.Deserialize<ReviewRequest>(json, FlowJsonOptions.Default);
            if (rr is not null)
                requests.Add(rr);
        }
        return requests;
    }

    async Task<SaveResult> IReviewRequestStore.SaveAsync(ReviewRequest reviewRequest, CancellationToken ct)
    {
        var path = ReviewRequestFile(reviewRequest.SpecId, reviewRequest.Id);
        var json = JsonSerializer.Serialize(reviewRequest, FlowJsonOptions.Default);
        await AtomicWriteAsync(path, json, ct);
        return SaveResult.Ok();
    }

    // ── IActivityStore ──

    public async Task AppendAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        var dir = ActivityDir(activityEvent.SpecId);
        Directory.CreateDirectory(dir);

        var date = activityEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-dd");
        var path = Path.Combine(dir, $"{date}.jsonl");
        var line = JsonSerializer.Serialize(activityEvent, FlowJsonOptions.Compact) + "\n";

        await using var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(line.AsMemory(), ct);
    }

    public async Task<IReadOnlyList<ActivityEvent>> LoadRecentAsync(
        string specId, int maxCount, CancellationToken ct = default)
    {
        var dir = ActivityDir(specId);
        if (!Directory.Exists(dir)) return [];

        var files = Directory.GetFiles(dir, "*.jsonl")
            .OrderByDescending(f => f)
            .ToList();

        var events = new List<ActivityEvent>();
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var evt = JsonSerializer.Deserialize<ActivityEvent>(lines[i], FlowJsonOptions.Compact);
                if (evt is not null)
                    events.Add(evt);
                if (events.Count >= maxCount)
                    return events;
            }
        }
        return events;
    }

    // ── Atomic write ──

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, ct);
        File.Move(tempPath, path, overwrite: true);
    }
}
