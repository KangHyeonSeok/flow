using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 표준 오류 정보 모델. F-002-C4: 오류 응답의 error.code, error.message, error.details 계약.
/// </summary>
public class ErrorInfo
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "ERROR";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("details")]
    public object? Details { get; set; }
}
