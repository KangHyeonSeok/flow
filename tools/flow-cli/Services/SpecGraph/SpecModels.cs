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
    public List<string> CodeRefs { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<SpecEvidence> Evidence { get; set; } = new();
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
    public List<string> CodeRefs { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<SpecEvidence> Evidence { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

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
