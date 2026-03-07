using System.Text.Json.Serialization;

namespace FlowCLI.Services.TestSync;

/// <summary>
/// 테스트 실행 결과 (xUnit/Jest/pytest 파싱 후 정규화된 포맷, F-014).
/// </summary>
public class TestRunResult
{
    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "generic"; // xunit | jest | pytest | generic

    [JsonPropertyName("runAt")]
    public string? RunAt { get; set; }

    [JsonPropertyName("tests")]
    public List<TestCaseResult> Tests { get; set; } = new();
}

/// <summary>
/// 개별 테스트 케이스 결과.
/// </summary>
public class TestCaseResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("suite")]
    public string? Suite { get; set; }

    /// <summary>passed | failed | skipped | flaky</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("durationMs")]
    public double? DurationMs { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>xUnit Trait 어노테이션: {"Spec": "F-014-C1"}</summary>
    [JsonPropertyName("traits")]
    public Dictionary<string, string> Traits { get; set; } = new();

    /// <summary>pytest/Jest 마커: ["spec:F-014-C1"]</summary>
    [JsonPropertyName("markers")]
    public List<string> Markers { get; set; } = new();
}

/// <summary>
/// spec-test-sync 실행 결과 (F-014-C1).
/// </summary>
public class TestSyncResult
{
    [JsonPropertyName("totalTests")]
    public int TotalTests { get; set; }

    [JsonPropertyName("mappedTests")]
    public int MappedTests { get; set; }

    [JsonPropertyName("unmappedTests")]
    public int UnmappedTests { get; set; }

    [JsonPropertyName("updatedSpecs")]
    public int UpdatedSpecs { get; set; }

    [JsonPropertyName("quarantinedTests")]
    public int QuarantinedTests { get; set; }

    [JsonPropertyName("mappings")]
    public List<TestMappingEntry> Mappings { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 테스트-조건 매핑 항목.
/// </summary>
public class TestMappingEntry
{
    [JsonPropertyName("testId")]
    public string TestId { get; set; } = "";

    [JsonPropertyName("testName")]
    public string TestName { get; set; } = "";

    [JsonPropertyName("conditionId")]
    public string ConditionId { get; set; } = "";

    [JsonPropertyName("specId")]
    public string SpecId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("quarantined")]
    public bool Quarantined { get; set; }
}

/// <summary>
/// 스펙 테스트 건강도 리포트 (F-014-C2).
/// </summary>
public class TestHealthReport
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("totalSpecs")]
    public int TotalSpecs { get; set; }

    [JsonPropertyName("healthySpecs")]
    public int HealthySpecs { get; set; }

    [JsonPropertyName("failedSpecs")]
    public int FailedSpecs { get; set; }

    [JsonPropertyName("unresolvedSpecs")]
    public int UnresolvedSpecs { get; set; }

    [JsonPropertyName("specs")]
    public List<SpecHealthEntry> Specs { get; set; } = new();
}

/// <summary>
/// 개별 스펙의 건강도 요약.
/// </summary>
public class SpecHealthEntry
{
    [JsonPropertyName("specId")]
    public string SpecId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("healthScore")]
    public double HealthScore { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("passed")]
    public int Passed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("flaky")]
    public int Flaky { get; set; }

    /// <summary>테스트 없는 조건 수 (unresolved)</summary>
    [JsonPropertyName("unresolved")]
    public int Unresolved { get; set; }

    /// <summary>up | down | stable</summary>
    [JsonPropertyName("trend")]
    public string Trend { get; set; } = "stable";

    [JsonPropertyName("conditions")]
    public List<ConditionHealthEntry> Conditions { get; set; } = new();
}

/// <summary>
/// 개별 조건의 건강도 요약.
/// </summary>
public class ConditionHealthEntry
{
    [JsonPropertyName("conditionId")]
    public string ConditionId { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("healthScore")]
    public double HealthScore { get; set; }

    [JsonPropertyName("flakyScore")]
    public double FlakyScore { get; set; }

    [JsonPropertyName("quarantined")]
    public bool Quarantined { get; set; }

    [JsonPropertyName("testCount")]
    public int TestCount { get; set; }

    /// <summary>healthy | failed | unresolved | quarantined</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}
