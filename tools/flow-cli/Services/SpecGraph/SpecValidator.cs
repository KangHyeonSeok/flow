using System.Text.RegularExpressions;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// Spec 유효성 검사기. 필수 필드, 타입, 참조 무결성, 완결성 등을 검증.
/// </summary>
public class SpecValidator
{
    private static readonly Regex IdPattern = new(@"^F-\d{3}(-\d{2})?$", RegexOptions.Compiled);
    private static readonly Regex ConditionIdPattern = new(@"^F-\d{3}(-\d{2})?-C\d+$", RegexOptions.Compiled);
    private static readonly HashSet<string> ValidStatuses = new()
    {
        "draft", "active", "needs-review", "verified", "deprecated"
    };
    private static readonly HashSet<string> ValidNodeTypes = new() { "feature", "condition" };
    private static readonly HashSet<string> ValidEvidenceTypes = new()
    {
        "screenshot", "log", "metric", "test-result"
    };

    /// <summary>
    /// 단일 스펙의 유효성을 검사합니다.
    /// </summary>
    public ValidationResult ValidateSpec(SpecNode spec, bool strict = false)
    {
        var result = new ValidationResult();

        // 1. 필수 필드 검사
        if (string.IsNullOrWhiteSpace(spec.Id))
            result.Errors.Add(Error(spec.Id, "id", "ID는 필수입니다."));

        if (string.IsNullOrWhiteSpace(spec.Title))
            result.Errors.Add(Error(spec.Id, "title", "제목은 필수입니다."));

        if (string.IsNullOrWhiteSpace(spec.Description))
            result.Errors.Add(Error(spec.Id, "description", "설명은 필수입니다."));

        // 2. ID 형식 검사
        if (!string.IsNullOrWhiteSpace(spec.Id) && !IdPattern.IsMatch(spec.Id))
            result.Errors.Add(Error(spec.Id, "id", $"ID 형식이 올바르지 않습니다. 기대: F-NNN 또는 F-NNN-NN, 실제: {spec.Id}"));

        // 3. 상태 유효성
        if (!ValidStatuses.Contains(spec.Status))
            result.Errors.Add(Error(spec.Id, "status", $"유효하지 않은 상태: {spec.Status}. 허용: {string.Join(", ", ValidStatuses)}"));

        // 4. nodeType 유효성
        if (!ValidNodeTypes.Contains(spec.NodeType))
            result.Errors.Add(Error(spec.Id, "nodeType", $"유효하지 않은 노드 타입: {spec.NodeType}. 허용: feature, condition"));

        // 5. 수락 조건 검사
        if (strict && spec.NodeType == "feature" && spec.Conditions.Count < 3)
            result.Errors.Add(Error(spec.Id, "conditions", $"수락 조건이 최소 3개 필요합니다 (현재: {spec.Conditions.Count})."));

        // 6. 조건 ID 유효성
        foreach (var cond in spec.Conditions)
        {
            if (string.IsNullOrWhiteSpace(cond.Id))
                result.Errors.Add(Error(spec.Id, "conditions.id", "조건 ID는 필수입니다."));
            else if (!ConditionIdPattern.IsMatch(cond.Id))
                result.Warnings.Add(Warning(spec.Id, $"조건 ID '{cond.Id}' 가 표준 형식(F-NNN-CN)이 아닙니다."));

            if (!ValidStatuses.Contains(cond.Status))
                result.Errors.Add(Error(spec.Id, $"conditions[{cond.Id}].status", $"유효하지 않은 상태: {cond.Status}"));
        }

        // 7. Evidence 타입 검사
        foreach (var ev in spec.Evidence)
        {
            if (!ValidEvidenceTypes.Contains(ev.Type))
                result.Warnings.Add(Warning(spec.Id, $"증거 타입 '{ev.Type}'는 표준 타입이 아닙니다."));
        }

        // 8. schemaVersion 검사
        if (spec.SchemaVersion < 1)
            result.Warnings.Add(Warning(spec.Id, "schemaVersion이 설정되지 않았습니다."));

        return result;
    }

    /// <summary>
    /// 모든 스펙에 대한 참조 무결성을 검사합니다.
    /// </summary>
    public ValidationResult ValidateAll(List<SpecNode> specs, bool strict = false)
    {
        var result = new ValidationResult();
        var idSet = specs.Select(s => s.Id).ToHashSet();

        foreach (var spec in specs)
        {
            // 개별 스펙 유효성
            var specResult = ValidateSpec(spec, strict);
            result.Errors.AddRange(specResult.Errors);
            result.Warnings.AddRange(specResult.Warnings);

            // parent 참조 검사
            if (!string.IsNullOrEmpty(spec.Parent) && !idSet.Contains(spec.Parent))
                result.Errors.Add(Error(spec.Id, "parent", $"존재하지 않는 parent 참조: {spec.Parent}"));

            // dependencies 참조 검사
            foreach (var dep in spec.Dependencies)
            {
                if (!idSet.Contains(dep))
                    result.Errors.Add(Error(spec.Id, "dependencies", $"존재하지 않는 의존 참조: {dep}"));
            }

            // 자기 참조 검사
            if (spec.Dependencies.Contains(spec.Id))
                result.Errors.Add(Error(spec.Id, "dependencies", "자기 자신에 대한 의존은 허용되지 않습니다."));

            if (spec.Parent == spec.Id)
                result.Errors.Add(Error(spec.Id, "parent", "자기 자신을 parent로 설정할 수 없습니다."));

            // ID 중복 검사
            var idCount = specs.Count(s => s.Id == spec.Id);
            if (idCount > 1)
                result.Errors.Add(Error(spec.Id, "id", "중복된 ID가 존재합니다."));
        }

        return result;
    }

    private static ValidationError Error(string specId, string field, string message)
        => new() { SpecId = specId, Field = field, Message = message };

    private static ValidationWarning Warning(string specId, string message)
        => new() { SpecId = specId, Message = message };
}
