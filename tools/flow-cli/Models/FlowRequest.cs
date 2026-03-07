using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 공통 JSON 요청 계약. F-002-C1: stdin/file/직접 문자열로 전달되는 명령 요청 DTO.
/// </summary>
public class FlowRequest
{
    /// <summary>최상위 명령 이름 (예: "build", "spec-create", "db-query")</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>하위 명령 이름 (예: "e2e" for test command)</summary>
    [JsonPropertyName("subcommand")]
    public string? Subcommand { get; set; }

    /// <summary>순서 있는 위치 인자 목록</summary>
    [JsonPropertyName("arguments")]
    public string[]? Arguments { get; set; }

    /// <summary>명령 옵션 맵. 값은 bool/string/number/array 등 JSON 원소.</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, JsonElement>? Options { get; set; }
}
