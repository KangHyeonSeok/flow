using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 공통 JSON 요청 envelope. F-003-C1: stdin/file/직접 문자열로 전달되는 명령 요청 DTO.
/// command, subcommand, payload, options, metadata 필드로 표준화된 모델.
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

    /// <summary>명령별 구조화 데이터. 명령 핸들러가 필요로 하는 구조체를 담는 payload 필드.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>명령 옵션 맵. 값은 bool/string/number/array 등 JSON 원소.</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, JsonElement>? Options { get; set; }

    /// <summary>실행 컨텍스트 메타데이터 (요청 ID, 호출자 정보, 타임스탬프 등).</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
