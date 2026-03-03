using System.Text.Json.Serialization;

namespace FlowCLI.Models;

/// <summary>
/// .flow/config.json 설정 모델.
/// specRepository, Runner 동작에 필요한 모든 설정을 포함한다.
/// (runner-config.json은 config.json으로 통합됨)
/// </summary>
public class FlowConfig
{
    // ── 스펙 저장소 ─────────────────────────────────────────────

    /// <summary>스펙 저장소 git URL. 예: https://github.com/user/flow-spec.git</summary>
    [JsonPropertyName("specRepository")]
    public string? SpecRepository { get; set; }

    /// <summary>스펙 저장소 브랜치 (기본: main)</summary>
    [JsonPropertyName("specBranch")]
    public string SpecBranch { get; set; } = "main";

    // ── Runner 동작 설정 ─────────────────────────────────────────

    /// <summary>스펙 그래프 pull 주기 (분)</summary>
    [JsonPropertyName("pollIntervalMinutes")]
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>동시 구현 스펙 수</summary>
    [JsonPropertyName("maxConcurrentSpecs")]
    public int MaxConcurrentSpecs { get; set; } = 1;

    /// <summary>로그 디렉토리 (.flow 기준 상대 경로)</summary>
    [JsonPropertyName("logDir")]
    public string LogDir { get; set; } = "logs";

    /// <summary>PID 파일 경로 (.flow 기준 상대 경로)</summary>
    [JsonPropertyName("pidFile")]
    public string PidFile { get; set; } = "runner.pid";

    /// <summary>Copilot CLI 기본 모델</summary>
    [JsonPropertyName("copilotModel")]
    public string CopilotModel { get; set; } = "claude-sonnet-4.6";

    /// <summary>Copilot CLI 실행 파일명 (CopilotCliPath 미설정 시 사용)</summary>
    [JsonPropertyName("copilotCommand")]
    public string CopilotCommand { get; set; } = "copilot";

    /// <summary>Copilot CLI 절대 경로. 설정 시 CopilotCommand보다 우선 적용. .ps1 파일은 pwsh로 자동 실행.</summary>
    [JsonPropertyName("copilotCliPath")]
    public string? CopilotCliPath { get; set; }

    /// <summary>구현 대상 스펙 상태 목록</summary>
    [JsonPropertyName("targetStatuses")]
    public string[] TargetStatuses { get; set; } = ["draft", "needs-review"];

    /// <summary>Worktree 기본 디렉토리 (.flow 기준 상대 경로)</summary>
    [JsonPropertyName("worktreeDir")]
    public string WorktreeDir { get; set; } = "worktrees";

    /// <summary>Copilot 호출 타임아웃 (분)</summary>
    [JsonPropertyName("copilotTimeoutMinutes")]
    public int CopilotTimeoutMinutes { get; set; } = 30;

    /// <summary>스펙 그래프 리포 원격 이름</summary>
    [JsonPropertyName("remoteName")]
    public string RemoteName { get; set; } = "origin";

    /// <summary>메인 브랜치 이름</summary>
    [JsonPropertyName("mainBranch")]
    public string MainBranch { get; set; } = "main";

    // ── 로깅 설정 ────────────────────────────────────────────────

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
