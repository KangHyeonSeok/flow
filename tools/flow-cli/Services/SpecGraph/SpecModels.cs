using System.Text.Json.Serialization;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// Evidence — 동작 증거. screenshot | log | metric | test-result
/// </summary>
public class SpecEvidence
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "log";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("capturedAt")]
    public string? CapturedAt { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

/// <summary>
/// TestLink — 조건에 연결된 테스트 결과 (F-014).
/// </summary>
public class TestLink
{
    [JsonPropertyName("testId")]
    public string TestId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("suite")]
    public string? Suite { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // passed | failed | skipped | flaky | quarantined

    [JsonPropertyName("durationMs")]
    public double? DurationMs { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("runAt")]
    public string? RunAt { get; set; }

    [JsonPropertyName("quarantined")]
    public bool Quarantined { get; set; }

    /// <summary>연속 flaky 감지를 위한 최근 실행 이력 (최대 10개)</summary>
    [JsonPropertyName("flakyHistory")]
    public List<string> FlakyHistory { get; set; } = new();
}

/// <summary>
/// Condition — 수락 조건. 그래프에서 하위 노드로 취급.
/// </summary>
public class SpecCondition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = "condition";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("codeRefs")]
    [JsonConverter(typeof(CodeRefsJsonConverter))]
    public List<string> CodeRefs { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<SpecEvidence> Evidence { get; set; } = new();

    /// <summary>연결된 테스트 결과 목록 (F-014)</summary>
    [JsonPropertyName("tests")]
    [JsonConverter(typeof(TestLinkJsonConverter))]
    public List<TestLink> Tests { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// SpecActivityStatusChange — activity가 유발한 spec 상태 변경.
/// </summary>
public class SpecActivityStatusChange
{
    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";
}

/// <summary>
/// SpecConditionUpdate — activity에 기록되는 condition 상태 변경 결과.
/// </summary>
public class SpecConditionUpdate
{
    [JsonPropertyName("conditionId")]
    public string ConditionId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

/// <summary>
/// SpecActivityEntry — 스펙의 append-only 활동 이력 항목.
/// </summary>
public class SpecActivityEntry
{
    [JsonPropertyName("at")]
    public string At { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "system";

    [JsonPropertyName("actor")]
    public string? Actor { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("artifacts")]
    public List<string> Artifacts { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();

    [JsonPropertyName("conditionUpdates")]
    public List<SpecConditionUpdate> ConditionUpdates { get; set; } = new();

    [JsonPropertyName("statusChange")]
    public SpecActivityStatusChange? StatusChange { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("relatedIds")]
    public List<string> RelatedIds { get; set; } = new();

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "handoff";
}

/// <summary>
/// SpecNode — 기능 스펙의 최상위 모델. JSON 스키마 v2.
/// </summary>
public class SpecNode
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = "feature";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("conditions")]
    public List<SpecCondition> Conditions { get; set; } = new();

    [JsonPropertyName("codeRefs")]
    [JsonConverter(typeof(CodeRefsJsonConverter))]
    public List<string> CodeRefs { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<SpecEvidence> Evidence { get; set; } = new();

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }  // P1 | P2 | P3 (기본 P3)

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    // ── 관계 메타데이터 (F-022) ──────────────────────────────────────────

    /// <summary>
    /// 이 스펙이 실질적으로 대체하는 이전 스펙 ID 목록.
    /// 대상 스펙의 책임 경계·아키텍처가 변경되어 신규 스펙으로 분리할 때 사용.
    /// 대상 스펙에는 supersededBy에 이 스펙 ID가 기록되어야 한다 (양방향 연결).
    /// 허용 값: F-NNN 또는 F-NNN-NN 형식의 스펙 ID 목록.
    /// </summary>
    [JsonPropertyName("supersedes")]
    public List<string> Supersedes { get; set; } = new();

    /// <summary>
    /// 이 스펙을 대체한 신규 스펙 ID 목록.
    /// supersedes의 역방향 포인터. 이 스펙이 deprecated/done 처리될 때 함께 기록.
    /// </summary>
    [JsonPropertyName("supersededBy")]
    public List<string> SupersededBy { get; set; } = new();

    /// <summary>
    /// 이 스펙(task)이 in-place로 수정하는 대상 스펙 ID 목록.
    /// 대상 스펙의 정체성(title/conditions 과반)은 그대로이고 구현 세부사항만 바꿀 때 사용.
    /// 대상 스펙에는 mutatedBy에 이 스펙 ID가 기록되어야 한다 (양방향 연결).
    /// </summary>
    [JsonPropertyName("mutates")]
    public List<string> Mutates { get; set; } = new();

    /// <summary>
    /// 이 스펙을 in-place 수정한 task 스펙 ID 목록.
    /// mutates의 역방향 포인터.
    /// </summary>
    [JsonPropertyName("mutatedBy")]
    public List<string> MutatedBy { get; set; } = new();

    /// <summary>스펙 활동 이력. append-only 로그.</summary>
    [JsonPropertyName("activity")]
    public List<SpecActivityEntry> Activity { get; set; } = new();

    // ─────────────────────────────────────────────────────────────────────

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// 그래프 분석 결과를 담는 모델.
/// </summary>
public class SpecGraph
{
    /// <summary>모든 Feature 노드</summary>
    public Dictionary<string, SpecNode> Nodes { get; set; } = new();

    /// <summary>parent 기반 트리 구조 (parentId → childIds)</summary>
    public Dictionary<string, List<string>> Tree { get; set; } = new();

    /// <summary>루트 노드 ID 목록 (parent가 null인 노드)</summary>
    public List<string> Roots { get; set; } = new();

    /// <summary>의존성 기반 DAG (nodeId → dependencyIds)</summary>
    public Dictionary<string, List<string>> Dag { get; set; } = new();

    /// <summary>역방향 의존성 (nodeId → 이 노드에 의존하는 노드들)</summary>
    public Dictionary<string, List<string>> ReverseDag { get; set; } = new();

    /// <summary>위상 정렬 순서 (cycle 없을 때)</summary>
    public List<string>? TopologicalOrder { get; set; }

    /// <summary>cycle에 포함된 노드 ID 목록</summary>
    public List<string> CycleNodes { get; set; } = new();

    /// <summary>orphan 노드 (존재하지 않는 parent를 참조하는 노드)</summary>
    public List<string> OrphanNodes { get; set; } = new();

    /// <summary>
    /// 대체 관계 그래프 (newSpecId → [supersededSpecIds]).
    /// A.Supersedes = [B] 이면 SupersedesGraph[A] = [B].
    /// </summary>
    public Dictionary<string, List<string>> SupersedesGraph { get; set; } = new();

    /// <summary>
    /// 변형 관계 그래프 (mutatingSpecId → [targetSpecIds]).
    /// A.Mutates = [B] 이면 MutatesGraph[A] = [B].
    /// </summary>
    public Dictionary<string, List<string>> MutatesGraph { get; set; } = new();
}

/// <summary>
/// 스펙 대체(Supersede) 안전 전환 분석 결과 (F-021-C3).
/// PropagateSupersede() 호출 결과로 반환된다.
/// </summary>
public class SupersedeTransitionResult
{
    /// <summary>대체되는 기존 스펙 ID</summary>
    public string OldSpecId { get; set; } = "";

    /// <summary>기존 스펙을 대체하는 신규 스펙 ID</summary>
    public string NewSpecId { get; set; } = "";

    /// <summary>기존 스펙이 queued/working/needs-review 상태인지 여부</summary>
    public bool IsActiveSpec { get; set; }

    /// <summary>기존 스펙을 참조하는 활성 downstream 스펙이 있는지 여부</summary>
    public bool HasActiveDownstream { get; set; }

    /// <summary>
    /// 권장 전환 방식.
    /// "deprecate": 즉시 deprecated 처리 가능.
    /// "needs-review": 스펙 자체가 활성 상태 — 검토 후 전환.
    /// "blocked-review": 활성 downstream 참조 존재 — 사용자 승인 필요.
    /// </summary>
    public string RecommendedAction { get; set; } = "";

    /// <summary>기존 스펙에 의존하는 활성 downstream 스펙 ID 목록</summary>
    public List<string> DownstreamIds { get; set; } = new();

    /// <summary>전환 시 고려사항 안내 메시지</summary>
    public string TransitionNotes { get; set; } = "";
}

/// <summary>영향 분석 결과</summary>
public class ImpactResult
{
    public string SourceId { get; set; } = "";
    public int MaxDepth { get; set; }
    public List<ImpactedNode> ImpactedNodes { get; set; } = new();
}

public class ImpactedNode
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Depth { get; set; }
    public string Relation { get; set; } = ""; // "child" | "dependent" | "transitive"
}

/// <summary>코드 참조 검증 결과</summary>
public class CodeRefCheckResult
{
    public int TotalRefs { get; set; }
    public int ValidRefs { get; set; }
    public int InvalidRefs { get; set; }
    public List<InvalidCodeRef> InvalidItems { get; set; } = new();
    public double HealthPercent => TotalRefs > 0 ? Math.Round(ValidRefs * 100.0 / TotalRefs, 1) : 100;
}

public class InvalidCodeRef
{
    public string SpecId { get; set; } = "";
    public string CodeRef { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>유효성 검사 결과</summary>
public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
}

public class ValidationError
{
    public string SpecId { get; set; } = "";
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ValidationWarning
{
    public string SpecId { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>spec-order 결과 – 위상 정렬 기반 구현 순서</summary>
public class SpecOrderResult
{
    public List<SpecOrderPhase> Phases { get; set; } = new();
    public bool HasCycles { get; set; }
    public List<string> CycleNodes { get; set; } = new();
    public string? FromId { get; set; }
    public int TotalSpecs { get; set; }
}

/// <summary>하나의 Phase (동시 구현 가능한 스펙 그룹)</summary>
public class SpecOrderPhase
{
    public int Phase { get; set; }
    public List<SpecOrderEntry> Specs { get; set; } = new();
}

/// <summary>Phase 내 개별 스펙 항목</summary>
public class SpecOrderEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "P3";
    public int ConditionsCount { get; set; }
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>AI 최적화 순서 제안</summary>
public class AiOrderSuggestion
{
    public List<SpecOrderPhase> Phases { get; set; } = new();
    public string Reasoning { get; set; } = "";
}

/// <summary>의존성 제약 위반 항목</summary>
public class DependencyViolation
{
    public string SpecId { get; set; } = "";
    public string DependsOn { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// Planner 자동 queued 승격 평가 결과 (F-019).
/// EvaluateEligibility 호출 결과로 반환된다.
/// </summary>
public class PlannerAutoQueueEligibility
{
    /// <summary>자동 승격 가능 여부</summary>
    public bool IsEligible { get; set; }

    /// <summary>승격 불가 사유. IsEligible=false일 때 설명.</summary>
    public string? BlockReason { get; set; }

    /// <summary>
    /// 승격 신뢰도 (0.0 ~ 1.0).
    /// description 길이, conditions 수, dependencies, codeRefs 충족 여부를 기반으로 계산.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>미해결 질문 수. 0이어야 승격 가능.</summary>
    public int UnresolvedQuestions { get; set; }

    /// <summary>
    /// 승격 후 plannerState 값.
    /// 승격 가능: "standby", 대기: "waiting-user-input"
    /// </summary>
    public string PlannerState { get; set; } = "";
}
