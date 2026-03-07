namespace FlowCLI.Services.Runner;

/// <summary>
/// Runner 설정. .flow/runner-config.json 또는 CLI 옵션으로 제공.
/// </summary>
public class RunnerConfig
{
    /// <summary>스펙 그래프 pull 주기 (분)</summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>동시 구현 스펙 수</summary>
    public int MaxConcurrentSpecs { get; set; } = 1;

    /// <summary>로그 디렉토리 (.flow 기준 상대 경로)</summary>
    public string LogDir { get; set; } = "logs";

    /// <summary>PID 파일 경로 (.flow 기준 상대 경로)</summary>
    public string PidFile { get; set; } = "runner.pid";

    /// <summary>Copilot CLI 기본 모델</summary>
    public string CopilotModel { get; set; } = "claude-sonnet-4.6";

    /// <summary>Copilot CLI 실행 파일명 (CopilotCliPath 미설정 시 사용)</summary>
    public string CopilotCommand { get; set; } = "copilot";

    /// <summary>Copilot CLI 절대 경로. 설정 시 CopilotCommand보다 우선 적용. .ps1 파일은 pwsh로 자동 실행.</summary>
    public string? CopilotCliPath { get; set; }

    /// <summary>구현 대상 스펙 상태 목록. 기본적으로 queued 상태만 처리한다.</summary>
    public string[] TargetStatuses { get; set; } = ["queued"];

    /// <summary>Worktree 기본 디렉토리 (.flow 기준 상대 경로)</summary>
    public string WorktreeDir { get; set; } = "worktrees";

    /// <summary>Copilot 호출 타임아웃 (분)</summary>
    public int CopilotTimeoutMinutes { get; set; } = 30;

    /// <summary>스펙 그래프 리포 원격 이름</summary>
    public string RemoteName { get; set; } = "origin";

    /// <summary>메인 브랜치 이름</summary>
    public string MainBranch { get; set; } = "main";

    /// <summary>스펙 저장소 git URL (필수). 예: https://github.com/user/flow-spec.git</summary>
    public string? SpecRepository { get; set; }

    /// <summary>스펙 저장소 브랜치 (기본: main)</summary>
    public string SpecBranch { get; set; } = "main";

    // ── GitHub 이슈 연동 (F-070-C11~C15) ─────────────────

    /// <summary>GitHub 이슈 폴링 주기 (분)</summary>
    public int IssuePollIntervalMinutes { get; set; } = 10;

    /// <summary>GitHub 저장소 (owner/repo 형식)</summary>
    public string? GitHubRepo { get; set; }

    /// <summary>GitHub PAT. 환경변수 GITHUB_TOKEN 우선.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>스펙 연결 댓글 템플릿</summary>
    public string SpecLinkCommentTemplate { get; set; } = "Linked spec: {specId}";

    /// <summary>스펙 연결 라벨</summary>
    public string SpecLinkLabel { get; set; } = "spec-linked";

    /// <summary>자동 생성 스펙 라벨</summary>
    public string AutoCreateSpecLabel { get; set; } = "spec-auto-created";

    /// <summary>GitHub 이슈 연동 활성화</summary>
    public bool GitHubIssuesEnabled { get; set; } = false;
}

/// <summary>
/// Runner 인스턴스 정보. 비정상 종료 감지에 사용.
/// </summary>
public class RunnerInstance
{
    public string InstanceId { get; set; } = "";
    public int ProcessId { get; set; }
    public string StartedAt { get; set; } = "";
    public string Status { get; set; } = "running"; // running | stopped | crashed
    public string? CurrentSpecId { get; set; }
}

/// <summary>
/// 스펙 작업 결과
/// </summary>
public class SpecWorkResult
{
    public string SpecId { get; set; } = "";
    public bool Success { get; set; }
    public string Action { get; set; } = ""; // implement | merge-resolve | error-fix
    public string? ErrorMessage { get; set; }
    public string StartedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
    public string? WorktreePath { get; set; }
    public string? BranchName { get; set; }
}

/// <summary>
/// Runner 로그 엔트리
/// </summary>
public class RunnerLogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "INFO"; // INFO | WARN | ERROR
    public string InstanceId { get; set; } = "";
    public string? SpecId { get; set; }
    public string Action { get; set; } = "";
    public string Message { get; set; } = "";

    public override string ToString()
        => $"[{Timestamp}] [{Level}] [{InstanceId}] {(SpecId != null ? $"[{SpecId}] " : "")}{Action}: {Message}";
}

/// <summary>
/// 손상 스펙 JSON 진단 레코드 (F-025)
/// </summary>
public class BrokenSpecDiagRecord
{
    public string SpecId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string DetectedAt { get; set; } = "";
    public string? FileMtime { get; set; }
    /// <summary>unresolved | resolved | escalated</summary>
    public string Status { get; set; } = "unresolved";
    public string? ResolvedAt { get; set; }
    public string? LastCheckedAt { get; set; }
    public string? FailReason { get; set; }
    public int RepairAttempts { get; set; }
}

/// <summary>
/// 손상 스펙 진단 캐시 (.flow/spec-cache/broken-spec-diag.json) (F-025)
/// </summary>
public class BrokenSpecDiagCache
{
    public int Version { get; set; } = 1;
    public List<BrokenSpecDiagRecord> Records { get; set; } = new();
}

/// <summary>
/// GitHub 이슈 처리 결과 (F-070-C15)
/// </summary>
public class IssueProcessResult
{
    public int IssueNumber { get; set; }
    public string IssueTitle { get; set; } = "";
    public string Action { get; set; } = ""; // linked | created | skipped | error
    public string? SpecId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// GitHub 이슈 간소화 모델
/// </summary>
public class GitHubIssueInfo
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> Labels { get; set; } = new();
    public string State { get; set; } = "open";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// 이슈 연관도 기반 큐 우선순위 정보 (F-015-C2).
/// metadata.queuePriority에 저장된다.
/// </summary>
public class QueuePriorityInfo
{
    /// <summary>정규화된 우선순위 점수 (높을수록 우선 처리)</summary>
    public double Score { get; set; }

    /// <summary>점수 산출 근거 목록 (각 신호별 기여도 설명)</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>마지막 점수 갱신 시각 (ISO 8601)</summary>
    public string LastRefreshedAt { get; set; } = "";
}
