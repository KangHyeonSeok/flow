using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 표준 JSON 응답 계약. F-002-C3/C4: success, command, data, message, metadata, error, exit_code 포함.
/// </summary>
public class CommandResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>F-002-C4: 구조화된 오류 정보 (code, message, details)</summary>
    [JsonPropertyName("error")]
    public ErrorInfo? Error { get; set; }

    /// <summary>F-002-C4: 프로세스 종료 코드</summary>
    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; set; }

    /// <summary>F-002-C3: 실행 메타데이터 (타임스탬프, 버전 등)</summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }
}
