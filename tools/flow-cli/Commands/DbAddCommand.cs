using Cocona;
using FlowCLI.Models;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("db-add", Description = "Add document to RAG database")]
    public void DbAdd(
        [Option("content", Description = "Document content")] string content = "",
        [Option("tags", Description = "Canonical tags (comma-separated)")] string tags = "",
        [Option("commit-id", Description = "Git commit hash")] string commitId = "",
        [Option("feature", Description = "Feature name")] string? featureName = null,
        [Option("plan", Description = "Path to plan.md")] string? planPath = null,
        [Option("result", Description = "Path to result.md")] string? resultPath = null,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content is required. Use --content to specify.");

            // Resolve plan text from file path
            var planText = "";
            var resolvedPlanPath = string.IsNullOrWhiteSpace(planPath)
                ? null
                : (Path.IsPathRooted(planPath)
                    ? planPath
                    : Path.Combine(PathResolver.ProjectRoot, planPath));

            if (!string.IsNullOrEmpty(resolvedPlanPath) && File.Exists(resolvedPlanPath))
                planText = File.ReadAllText(resolvedPlanPath);

            // Resolve result text from file path
            var resultText = "";
            var resolvedResultPath = string.IsNullOrWhiteSpace(resultPath)
                ? null
                : (Path.IsPathRooted(resultPath)
                    ? resultPath
                    : Path.Combine(PathResolver.ProjectRoot, resultPath));

            if (!string.IsNullOrEmpty(resolvedResultPath) && File.Exists(resolvedResultPath))
                resultText = File.ReadAllText(resolvedResultPath);

            var record = new TaskRecord
            {
                Content = content,
                CanonicalTags = tags,
                CommitId = commitId,
                FeatureName = featureName ?? "",
                StateAtCreation = "",
                PlanText = planText,
                ResultText = resultText
            };

            var id = DatabaseService.AddDocument(record);

            JsonOutput.Write(JsonOutput.Success("db-add", new
            {
                id,
                feature_name = featureName ?? "",
                tags
            }, $"문서 추가됨 (ID: {id})"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("db-add", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
