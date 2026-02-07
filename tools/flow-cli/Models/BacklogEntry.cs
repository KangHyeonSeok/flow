namespace FlowCLI.Models;

public class BacklogEntry
{
    public string FeatureName { get; set; } = "";
    public string PlanPath { get; set; } = "";
    public bool NeedsReview { get; set; }
}
