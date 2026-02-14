using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// 빌드 모듈 manifest.json 역직렬화 모델.
/// 각 빌드 모듈(.flow/build/{platform}/)의 매니페스트를 표현한다.
/// </summary>
public class BuildManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("detect")]
    public BuildDetectRule? Detect { get; set; }

    [JsonPropertyName("scripts")]
    public BuildScripts? Scripts { get; set; }

    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? Args { get; set; }
}

/// <summary>
/// 프로젝트 타입 자동 감지 규칙.
/// </summary>
public class BuildDetectRule
{
    /// <summary>
    /// 감지에 사용할 파일/디렉토리 목록. 모두 존재해야 감지 성공.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

/// <summary>
/// 빌드 모듈이 제공하는 스크립트 경로 (manifest.json 기준 상대 경로).
/// </summary>
public class BuildScripts
{
    [JsonPropertyName("lint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lint { get; set; }

    [JsonPropertyName("build")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Build { get; set; }

    [JsonPropertyName("test")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Test { get; set; }

    [JsonPropertyName("run")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Run { get; set; }
}
