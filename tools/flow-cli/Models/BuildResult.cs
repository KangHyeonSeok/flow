using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 빌드/테스트 실행의 개별 단계 결과.
/// </summary>
public class BuildStepResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("output_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputPath { get; set; }

    [JsonPropertyName("stdout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stdout { get; set; }

    [JsonPropertyName("stderr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stderr { get; set; }

    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// 빌드 파이프라인 전체 결과. 여러 단계(lint/build/test/run)의 결과를 집계한다.
/// </summary>
public class BuildResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    [JsonPropertyName("project_path")]
    public string ProjectPath { get; set; } = "";

    [JsonPropertyName("total_duration_ms")]
    public long TotalDurationMs { get; set; }

    [JsonPropertyName("steps")]
    public List<BuildStepResult> Steps { get; set; } = new();

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}
