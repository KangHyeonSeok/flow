using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// .flow/config.json 설정 모델.
/// specRepository(git URL) 및 Runner 동작에 필요한 설정을 포함한다.
/// </summary>
public class FlowConfig
{
    /// <summary>
    /// 스펙 저장소 git URL (Runner 필수 설정).
    /// 예: https://github.com/user/flow-spec.git
    /// Runner 실행 시 이 URL에서 스펙을 동기화한다.
    /// </summary>
    [JsonPropertyName("specRepository")]
    public string? SpecRepository { get; set; }

    /// <summary>스펙 저장소 브랜치 (기본: main)</summary>
    [JsonPropertyName("specBranch")]
    public string SpecBranch { get; set; } = "main";

    /// <summary>로깅 설정</summary>
    [JsonPropertyName("logging")]
    public FlowLoggingConfig? Logging { get; set; }
}

/// <summary>로깅 설정</summary>
public class FlowLoggingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
}
