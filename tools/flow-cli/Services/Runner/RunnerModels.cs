namespace FlowCLI.Services.Runner;

/// <summary>
/// Runner 설정. .flow/runner-config.json 또는 CLI 옵션으로 제공.
/// </summary>
public class RunnerConfig
{
    /// <summary>스펙 그래프 pull 주기 (분)</summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>검토 대기 스펙 재검토 주기 (초)</summary>
    public int ReviewPollIntervalSeconds { get; set; } = 30;

    /// <summary>동시 구현 스펙 수</summary>
    public int MaxConcurrentSpecs { get; set; } = 1;

    /// <summary>로그 디렉토리 (.flow 기준 상대 경로)</summary>
    public string LogDir { get; set; } = "logs";

    /// <summary>PID 파일 경로 (.flow 기준 상대 경로)</summary>
    public string PidFile { get; set; } = "runner.pid";

    /// <summary>Copilot CLI 기본 모델</summary>
    public string CopilotModel { get; set; } = "gpt-5.4";

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

    /// <summary>한 poll 주기 내 최대 즉시 재스케줄 사이클 수 (F-031-C5). busy-wait 방지 상한.</summary>
    public int MaxReschedulesPerPoll { get; set; } = 10;

    /// <summary>스펙 자동 구현 최대 연속 실패 횟수. 초과 시 사용자 개입이 필요한 질문을 자동 생성한다. 0이면 비활성화.</summary>
    public int MaxImplementationAttempts { get; set; } = 3;

    /// <summary>Copilot rate limit 발생 시 재시도 전 대기 시간(초)</summary>
    public int RateLimitCooldownSeconds { get; set; } = 300;

    /// <summary>Copilot transport 오류 발생 시 재시도 전 대기 시간(초)</summary>
    public int TransportErrorCooldownSeconds { get; set; } = 120;

    /// <summary>비정상 종료/실행 충돌 복구 시 재시도 전 대기 시간(초)</summary>
    public int ExecutionCrashCooldownSeconds { get; set; } = 60;
}

public sealed class RunnerQueuePlan
{
    public string[] TargetStatuses { get; init; } = [];
    public int TotalCandidates { get; init; }
    public int ReadyCount { get; init; }
    public int BlockedCount { get; init; }
    public string? NextSpecId { get; init; }
    public List<RunnerQueueCandidate> ReadySpecs { get; init; } = [];
    public List<RunnerBlockedSpec> BlockedSpecs { get; init; } = [];
}

public sealed class RunnerQueueCandidate
{
    public string SpecId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public int Rank { get; init; }
    public double IssuePriorityScore { get; init; }
    public bool IsFallback { get; init; }
    public int DependencyCount { get; init; }
    public string[] Dependencies { get; init; } = [];
}

public sealed class RunnerBlockedSpec
{
    public string SpecId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public string Reason { get; init; } = "";
    public string[] UnmetDependencies { get; init; } = [];
    public int OpenQuestionCount { get; init; }
    public string? RetryNotBefore { get; init; }
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
    public string Action { get; set; } = ""; // implement | merge-resolve | error-fix | auto-verify | repair
    public string? ErrorMessage { get; set; }
    public string StartedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
    public string? WorktreePath { get; set; }
    public string? BranchName { get; set; }

    /// <summary>
    /// 이 결과가 즉시 재스케줄을 유발하는 상태 전환을 포함하는지 여부 (F-031-C1).
    /// working → needs-review/verified/done/queued 전환 또는 needs-review → verified 전환 시 true.
    /// </summary>
    public bool TriggeredReschedule { get; set; }
}

/// <summary>
/// Copilot CLI가 반환하는 검토 질문 항목.
/// </summary>
public class SpecReviewQuestion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "clarification";
    public string Question { get; set; } = "";
    public string Why { get; set; } = "";
    public string Status { get; set; } = "open";
    public string? Answer { get; set; }
    public string? AnsweredAt { get; set; }
    public string? RequestedAt { get; set; }
    public string? RequestedBy { get; set; }
}

/// <summary>
/// Copilot CLI 기반 검토 결과 모델.
/// </summary>
public class SpecReviewAnalysis
{
    public string Summary { get; set; } = "";
    public List<string> FailureReasons { get; set; } = new();
    public List<string> Alternatives { get; set; } = new();
    public List<string> SuggestedAttempts { get; set; } = new();
    public List<string> VerifiedConditionIds { get; set; } = new();
    public bool RequiresUserInput { get; set; }
    public List<string> AdditionalInformationRequests { get; set; } = new();
    public List<SpecReviewQuestion> Questions { get; set; } = new();
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

