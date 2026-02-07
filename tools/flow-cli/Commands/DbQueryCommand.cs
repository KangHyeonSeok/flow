using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("db-query", Description = "Search documents in RAG database")]
    public void DbQuery(
        [Option("query", Description = "Search query")] string query = "",
        [Option("plan", Description = "Include plan text in results")] bool plan = false,
        [Option("result", Description = "Include result text in results")] bool result = false,
        [Option("tags", Description = "Filter by tags (comma-separated)")] string? tags = null,
        [Option("top", Description = "Number of results to return")] int top = 5,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var records = DatabaseService.Query(query, tags, top, plan, result);

            var results = records.Select(r =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["content"] = r.Content,
                    ["canonical_tags"] = r.CanonicalTags,
                    ["feature_name"] = r.FeatureName,
                    ["commit_id"] = r.CommitId,
                    ["created_at"] = r.CreatedAt
                };
                if (plan) item["plan_text"] = r.PlanText;
                if (result) item["result_text"] = r.ResultText;
                return item;
            }).ToList();

            JsonOutput.Write(JsonOutput.Success("db-query", new
            {
                query,
                count = records.Count,
                results
            }, $"{records.Count}건 검색됨"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("db-query", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
