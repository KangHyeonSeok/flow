using System.Text.Json.Serialization;

namespace FlowCLI.Models;

public class ContextPhase
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "IDLE";

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("feature_name")]
    public string FeatureName { get; set; } = "";

    [JsonPropertyName("pending_questions")]
    public List<object>? PendingQuestions { get; set; }

    [JsonPropertyName("last_decision")]
    public LastDecision? LastDecision { get; set; }

    [JsonPropertyName("requires_human")]
    public bool RequiresHuman { get; set; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; set; } = 5;

    [JsonPropertyName("backlog")]
    public BacklogInfo? Backlog { get; set; }
}

public class LastDecision
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
}

public class BacklogInfo
{
    [JsonPropertyName("is_backlog")]
    public bool IsBacklog { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("completed_reason")]
    public string CompletedReason { get; set; } = "";
}
