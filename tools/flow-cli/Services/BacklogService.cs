using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// Manages the backlog queue: peek, pop, and queue file operations.
/// Handles moving feature directories from backlogs/ to implements/.
/// </summary>
public class BacklogService
{
    private readonly PathResolver _paths;

    public BacklogService(PathResolver paths) => _paths = paths;

    /// <summary>Read all entries from the queue file.</summary>
    public List<string> GetQueue()
    {
        var queuePath = Path.Combine(_paths.BacklogsDir, "queue");
        if (!File.Exists(queuePath)) return [];
        return File.ReadAllLines(queuePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    /// <summary>Preview the next backlog entry without removing it.</summary>
    public BacklogEntry? Peek()
    {
        var queue = GetQueue();
        if (queue.Count == 0) return null;

        var featureName = queue[0].Trim();
        var backlogFeatureDir = Path.Combine(_paths.BacklogsDir, featureName);
        var planPath = Path.Combine(backlogFeatureDir, "plan.md");
        var needReviewPath = Path.Combine(backlogFeatureDir, "need-review-plan.md");

        return new BacklogEntry
        {
            FeatureName = featureName,
            PlanPath = File.Exists(planPath) ? planPath
                     : File.Exists(needReviewPath) ? needReviewPath
                     : "",
            NeedsReview = File.Exists(needReviewPath) && !File.Exists(planPath)
        };
    }

    /// <summary>
    /// Pop the next backlog entry: move feature dir to implements/, update queue file.
    /// </summary>
    public BacklogEntry? Pop()
    {
        var entry = Peek();
        if (entry == null) return null;

        var featureName = entry.FeatureName;
        var backlogFeatureDir = Path.Combine(_paths.BacklogsDir, featureName);
        var implementDir = _paths.GetFeatureDir(featureName);

        // Move backlog feature dir → implements
        if (Directory.Exists(backlogFeatureDir))
        {
            if (Directory.Exists(implementDir))
            {
                // Merge: copy files into existing implements dir
                foreach (var file in Directory.GetFiles(backlogFeatureDir))
                {
                    var dest = Path.Combine(implementDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }
                Directory.Delete(backlogFeatureDir, recursive: true);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(implementDir)!);
                Directory.Move(backlogFeatureDir, implementDir);
            }
        }

        // Rename need-review-plan.md → plan.md if needed
        if (Directory.Exists(implementDir))
        {
            var needReviewPath = Path.Combine(implementDir, "need-review-plan.md");
            var planPath = Path.Combine(implementDir, "plan.md");
            if (File.Exists(needReviewPath) && !File.Exists(planPath))
                File.Move(needReviewPath, planPath);
        }

        // Update queue file
        var queue = GetQueue();
        queue.RemoveAt(0);
        var queueFilePath = Path.Combine(_paths.BacklogsDir, "queue");

        if (queue.Count == 0)
        {
            if (File.Exists(queueFilePath)) File.Delete(queueFilePath);
            var rationalePath = Path.Combine(_paths.BacklogsDir, "queue-rationale.md");
            if (File.Exists(rationalePath)) File.Delete(rationalePath);
        }
        else
        {
            File.WriteAllLines(queueFilePath, queue);
        }

        return entry;
    }
}
