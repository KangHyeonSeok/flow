namespace FlowCLI.Models;

public class TaskRecord
{
    public int Id { get; set; }
    public string Content { get; set; } = "";
    public string CanonicalTags { get; set; } = "";
    public string CommitId { get; set; } = "";
    public string PlanText { get; set; } = "";
    public string ResultText { get; set; } = "";
    public string FeatureName { get; set; } = "";
    public string StateAtCreation { get; set; } = "";
    public string Metadata { get; set; } = "{}";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
