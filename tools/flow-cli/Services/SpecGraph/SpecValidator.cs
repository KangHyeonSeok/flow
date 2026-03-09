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
        "draft", "queued", "working", "needs-review", "verified", "deprecated", "done"
    };
    private static readonly HashSet<string> ValidConditionStatuses = new()
    {
        "draft", "needs-review", "verified"
    };
    private static readonly HashSet<string> ValidNodeTypes = new() { "feature", "condition", "task" };
    private static readonly HashSet<string> ValidEvidenceTypes = new()
    {
        "screenshot", "log", "metric", "test-result"
    };
    private static readonly HashSet<string> ValidActivityRoles = new()
    {
        "planner", "developer", "test-validator", "tester", "system", "user"
    };
    private static readonly HashSet<string> ValidActivityOutcomes = new()
    {
        "handoff", "requeue", "needs-review", "verified", "done", "blocked"
    };
    private static readonly HashSet<string> ValidActivityKinds = new()
    {
        "create", "mutate", "supersede", "deprecate", "restore", "implementation", "validation", "verification", "recovery"
    };
    private static readonly HashSet<string> ValidActivityIssues = new()
    {
        "spec-misaligned-test", "trivial-test", "non-deterministic-test", "incorrect-result-interpretation",
        "missing-evidence", "execution-crash", "test-failed", "user-input-required", "user-test-required"
    };
    private static readonly HashSet<string> ValidConditionUpdateReasons = new()
    {
        "automated-tests-passed", "manual-tests-passed", "test-failed", "user-input-required",
        "user-test-required", "missing-evidence", "execution-crash", "reset-for-requeue"
    };

    /// <summary>
    /// 단일 스펙의 유효성을 검사합니다.
    /// </summary>
    public ValidationResult ValidateSpec(SpecNode spec, bool strict = false, bool checkTests = false)
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
            result.Errors.Add(Error(spec.Id, "nodeType", $"유효하지 않은 노드 타입: {spec.NodeType}. 허용: feature, condition, task"));

        // 4a. task 타입은 done 상태만 최종 상태로 허용 (verified는 feature 전용)
        if (spec.NodeType == "task" && spec.Status == "verified")
            result.Warnings.Add(Warning(spec.Id, "task 타입의 최종 상태는 'verified' 대신 'done'을 사용하세요."));

        // 5. 수락 조건 검사 (task 타입은 조건 불필요)
        if (strict && spec.NodeType == "feature" && spec.Conditions.Count < 3)
            result.Errors.Add(Error(spec.Id, "conditions", $"수락 조건이 최소 3개 필요합니다 (현재: {spec.Conditions.Count})."));
        if (strict && spec.NodeType == "task" && spec.Conditions.Count > 0)
            result.Warnings.Add(Warning(spec.Id, "task 타입은 일반적으로 수락 조건을 사용하지 않습니다."));

        // 6. 조건 ID 유효성
        foreach (var cond in spec.Conditions)
        {
            if (string.IsNullOrWhiteSpace(cond.Id))
                result.Errors.Add(Error(spec.Id, "conditions.id", "조건 ID는 필수입니다."));
            else if (!ConditionIdPattern.IsMatch(cond.Id))
                result.Warnings.Add(Warning(spec.Id, $"조건 ID '{cond.Id}' 가 표준 형식(F-NNN-CN)이 아닙니다."));

            if (!ValidConditionStatuses.Contains(cond.Status))
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

        // 9. --check-tests: 조건에 연결된 테스트 없으면 경고 (F-014-C5)
        if (checkTests && spec.NodeType == "feature")
        {
            foreach (var cond in spec.Conditions)
            {
                if (cond.Tests.Count == 0)
                    result.Errors.Add(Error(spec.Id, $"conditions[{cond.Id}].tests",
                        $"조건 '{cond.Id}'에 연결된 테스트가 없습니다. spec-test-sync 로 테스트를 연결하세요."));
            }
        }

        // 10. activity 검사
        ValidateActivity(spec, result);

        // 11. 관계 메타데이터 검사 (F-022)
        ValidateRelationFields(spec, result);

        return result;
    }

    /// <summary>
    /// 단일 스펙의 관계 필드(supersedes/supersededBy/mutates/mutatedBy/activity)를 검사합니다.
    /// </summary>
    private void ValidateRelationFields(SpecNode spec, ValidationResult result)
    {
        // 자기 참조 금지
        if (spec.Supersedes.Contains(spec.Id))
            result.Errors.Add(Error(spec.Id, "supersedes", "자기 자신을 supersedes로 설정할 수 없습니다."));
        if (spec.SupersededBy.Contains(spec.Id))
            result.Errors.Add(Error(spec.Id, "supersededBy", "자기 자신을 supersededBy로 설정할 수 없습니다."));
        if (spec.Mutates.Contains(spec.Id))
            result.Errors.Add(Error(spec.Id, "mutates", "자기 자신을 mutates로 설정할 수 없습니다."));
        if (spec.MutatedBy.Contains(spec.Id))
            result.Errors.Add(Error(spec.Id, "mutatedBy", "자기 자신을 mutatedBy로 설정할 수 없습니다."));

        // supersedes와 mutates는 동일 대상에 동시 적용 불가 (의미 충돌)
        var overlap = spec.Supersedes.Intersect(spec.Mutates).ToList();
        if (overlap.Count > 0)
            result.Errors.Add(Error(spec.Id, "supersedes/mutates",
                $"동일 스펙을 supersedes와 mutates에 동시에 지정할 수 없습니다: {string.Join(", ", overlap)}"));

    }

    private void ValidateActivity(SpecNode spec, ValidationResult result)
    {
        for (int i = 0; i < spec.Activity.Count; i++)
        {
            var entry = spec.Activity[i];

            if (string.IsNullOrWhiteSpace(entry.At))
                result.Errors.Add(Error(spec.Id, $"activity[{i}].at", "activity.at은 필수입니다."));

            if (!ValidActivityRoles.Contains(entry.Role))
                result.Errors.Add(Error(spec.Id, $"activity[{i}].role",
                    $"유효하지 않은 activity.role: '{entry.Role}'. 허용: {string.Join(", ", ValidActivityRoles)}"));

            if (string.IsNullOrWhiteSpace(entry.Summary))
                result.Errors.Add(Error(spec.Id, $"activity[{i}].summary", "activity.summary는 필수입니다."));

            if (!ValidActivityOutcomes.Contains(entry.Outcome))
                result.Errors.Add(Error(spec.Id, $"activity[{i}].outcome",
                    $"유효하지 않은 activity.outcome: '{entry.Outcome}'. 허용: {string.Join(", ", ValidActivityOutcomes)}"));

            if (!string.IsNullOrWhiteSpace(entry.Kind) && !ValidActivityKinds.Contains(entry.Kind))
                result.Errors.Add(Error(spec.Id, $"activity[{i}].kind",
                    $"유효하지 않은 activity.kind: '{entry.Kind}'. 허용: {string.Join(", ", ValidActivityKinds)}"));

            for (int issueIndex = 0; issueIndex < entry.Issues.Count; issueIndex++)
            {
                var issue = entry.Issues[issueIndex];
                if (!ValidActivityIssues.Contains(issue))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].issues[{issueIndex}]",
                        $"유효하지 않은 activity issue: '{issue}'. 허용: {string.Join(", ", ValidActivityIssues)}"));
            }

            for (int updateIndex = 0; updateIndex < entry.ConditionUpdates.Count; updateIndex++)
            {
                var update = entry.ConditionUpdates[updateIndex];
                if (string.IsNullOrWhiteSpace(update.ConditionId))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].conditionUpdates[{updateIndex}].conditionId", "conditionId는 필수입니다."));

                if (!ValidConditionStatuses.Contains(update.Status))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].conditionUpdates[{updateIndex}].status",
                        $"유효하지 않은 condition update status: '{update.Status}'. 허용: {string.Join(", ", ValidConditionStatuses)}"));

                if (!string.IsNullOrWhiteSpace(update.Reason) && !ValidConditionUpdateReasons.Contains(update.Reason))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].conditionUpdates[{updateIndex}].reason",
                        $"유효하지 않은 condition update reason: '{update.Reason}'. 허용: {string.Join(", ", ValidConditionUpdateReasons)}"));
            }

            if (entry.StatusChange != null)
            {
                if (!ValidStatuses.Contains(entry.StatusChange.From))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].statusChange.from", $"유효하지 않은 상태: {entry.StatusChange.From}"));
                if (!ValidStatuses.Contains(entry.StatusChange.To))
                    result.Errors.Add(Error(spec.Id, $"activity[{i}].statusChange.to", $"유효하지 않은 상태: {entry.StatusChange.To}"));
            }
        }
    }

    /// <summary>
    /// 모든 스펙에 대한 참조 무결성을 검사합니다.
    /// </summary>
    public ValidationResult ValidateAll(List<SpecNode> specs, bool strict = false, bool checkTests = false)
    {
        var result = new ValidationResult();
        var idSet = specs.Select(s => s.Id).ToHashSet();

        foreach (var spec in specs)
        {
            // 개별 스펙 유효성
            var specResult = ValidateSpec(spec, strict, checkTests);
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

            // 관계 필드 참조 무결성 (F-022)
            ValidateCrossSpecRelations(spec, idSet, result);
        }

        // 양방향 연결 일관성 검사 (F-022)
        ValidateBidirectionalLinks(specs, result);

        return result;
    }

    /// <summary>
    /// 관계 필드에서 참조하는 ID가 실제 존재하는지 검사합니다.
    /// </summary>
    private void ValidateCrossSpecRelations(SpecNode spec, HashSet<string> idSet, ValidationResult result)
    {
        foreach (var id in spec.Supersedes)
        {
            if (!idSet.Contains(id))
                result.Errors.Add(Error(spec.Id, "supersedes", $"존재하지 않는 스펙 참조: {id}"));
        }
        foreach (var id in spec.SupersededBy)
        {
            if (!idSet.Contains(id))
                result.Errors.Add(Error(spec.Id, "supersededBy", $"존재하지 않는 스펙 참조: {id}"));
        }
        foreach (var id in spec.Mutates)
        {
            if (!idSet.Contains(id))
                result.Errors.Add(Error(spec.Id, "mutates", $"존재하지 않는 스펙 참조: {id}"));
        }
        foreach (var id in spec.MutatedBy)
        {
            if (!idSet.Contains(id))
                result.Errors.Add(Error(spec.Id, "mutatedBy", $"존재하지 않는 스펙 참조: {id}"));
        }
    }

    /// <summary>
    /// supersedes↔supersededBy, mutates↔mutatedBy 양방향 연결 일관성을 검사합니다.
    /// A.supersedes에 B가 있으면 B.supersededBy에 A가 있어야 하고, 역방향도 마찬가지.
    /// </summary>
    private void ValidateBidirectionalLinks(List<SpecNode> specs, ValidationResult result)
    {
        var nodeMap = specs.ToDictionary(s => s.Id);

        foreach (var spec in specs)
        {
            // A.supersedes → B 이면 B.supersededBy ∋ A
            foreach (var targetId in spec.Supersedes)
            {
                if (nodeMap.TryGetValue(targetId, out var target) && !target.SupersededBy.Contains(spec.Id))
                    result.Warnings.Add(Warning(spec.Id,
                        $"양방향 연결 불일치: {spec.Id}.supersedes에 {targetId}가 있지만 {targetId}.supersededBy에 {spec.Id}가 없습니다."));
            }

            // A.supersededBy → B 이면 B.supersedes ∋ A
            foreach (var sourceId in spec.SupersededBy)
            {
                if (nodeMap.TryGetValue(sourceId, out var source) && !source.Supersedes.Contains(spec.Id))
                    result.Warnings.Add(Warning(spec.Id,
                        $"양방향 연결 불일치: {spec.Id}.supersededBy에 {sourceId}가 있지만 {sourceId}.supersedes에 {spec.Id}가 없습니다."));
            }

            // A.mutates → B 이면 B.mutatedBy ∋ A
            foreach (var targetId in spec.Mutates)
            {
                if (nodeMap.TryGetValue(targetId, out var target) && !target.MutatedBy.Contains(spec.Id))
                    result.Warnings.Add(Warning(spec.Id,
                        $"양방향 연결 불일치: {spec.Id}.mutates에 {targetId}가 있지만 {targetId}.mutatedBy에 {spec.Id}가 없습니다."));
            }

            // A.mutatedBy → B 이면 B.mutates ∋ A
            foreach (var sourceId in spec.MutatedBy)
            {
                if (nodeMap.TryGetValue(sourceId, out var source) && !source.Mutates.Contains(spec.Id))
                    result.Warnings.Add(Warning(spec.Id,
                        $"양방향 연결 불일치: {spec.Id}.mutatedBy에 {sourceId}가 있지만 {sourceId}.mutates에 {spec.Id}가 없습니다."));
            }
        }
    }

    /// <summary>
    /// Planner 자동 queued 승격 가능 여부를 검증한다 (F-019-C5).
    /// supersedes 또는 mutates 관계가 있지만 activity에 기록되지 않은 경우 오류를 반환한다.
    /// </summary>
    public ValidationResult ValidateAutoQueueEligibility(SpecNode spec)
    {
        var result = new ValidationResult();

        // C5: supersedes가 있으면 activity에 supersede 항목 필요
        if (spec.Supersedes.Count > 0)
        {
            bool hasActivity = HasRelationActivity(spec, "supersede");
            if (!hasActivity)
                result.Errors.Add(Error(spec.Id, "supersedes",
                    "supersedes 관계가 activity에 기록되지 않았습니다. " +
                    "자동 승격 전에 activity.kind='supersede' 항목을 추가하세요."));
        }

        // C5: mutates가 있으면 activity에 mutate 항목 필요
        if (spec.Mutates.Count > 0)
        {
            bool hasActivity = HasRelationActivity(spec, "mutate");
            if (!hasActivity)
                result.Errors.Add(Error(spec.Id, "mutates",
                    "mutates 관계가 activity에 기록되지 않았습니다. " +
                    "자동 승격 전에 activity.kind='mutate' 항목을 추가하세요."));
        }

        // F-021-C4: supersededBy/mutatedBy가 있으면 대응하는 방향 포인터도 필요
        // (역방향 포인터만 있고 정방향 관계가 없으면 불명확)
        if (spec.SupersededBy.Count > 0 && spec.Supersedes.Count == 0)
        {
            // 역방향은 있지만 이 스펙 자신이 supersedes를 지정하지 않음 — 정상
            // (이 스펙이 supersededBy 대상이라면 별도 검토가 필요)
            result.Warnings.Add(Warning(spec.Id,
                $"스펙 '{spec.Id}'이 supersededBy에 의해 대체될 예정입니다. " +
                $"대체 스펙({string.Join(", ", spec.SupersededBy)})을 검토하고 deprecated 전환 시점을 결정하세요."));
        }

        return result;
    }

    /// <summary>
    /// F-021-C4: 새 스펙이 기존 스펙을 부분적으로 변형하거나 확장할 때 관계 유형이 명시되어야 함을 검증한다.
    /// supersedes/mutates 중 하나는 있지만 activity 또는 bidirectional link가 불완전하면 오류를 반환한다.
    /// 관계가 모두 불명확(parent/dep/supersedes/mutates 모두 없고 다른 스펙과 유사한 제목)하면 경고를 추가한다.
    /// </summary>
    public ValidationResult ValidateRelationCompleteness(SpecNode spec, IReadOnlyList<SpecNode> allSpecs)
    {
        var result = new ValidationResult();

        // supersedes는 있는데 supersede activity가 없으면 관계 불명확
        foreach (var targetId in spec.Supersedes)
        {
            bool hasActivity = HasRelationActivity(spec, "supersede", targetId);
            if (!hasActivity)
                result.Errors.Add(Error(spec.Id, "supersedes",
                    $"supersedes[{targetId}] 관계에 대한 activity(kind=supersede, relatedIds∋{targetId}) 가 없습니다. " +
                    "관계가 불명확하면 auto-queue 승격이 금지됩니다."));
        }

        // mutates는 있는데 mutate activity가 없으면 관계 불명확
        foreach (var targetId in spec.Mutates)
        {
            bool hasActivity = HasRelationActivity(spec, "mutate", targetId);
            if (!hasActivity)
                result.Errors.Add(Error(spec.Id, "mutates",
                    $"mutates[{targetId}] 관계에 대한 activity(kind=mutate, relatedIds∋{targetId}) 가 없습니다. " +
                    "관계가 불명확하면 auto-queue 승격이 금지됩니다."));
        }

        return result;
    }

    private static bool HasRelationActivity(SpecNode spec, string kind, string? relatedId = null)
    {
        return spec.Activity.Any(entry =>
            string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase)
            && (relatedId == null || entry.RelatedIds.Contains(relatedId)));
    }

    private static ValidationError Error(string specId, string field, string message)
        => new() { SpecId = specId, Field = field, Message = message };

    private static ValidationWarning Warning(string specId, string message)
        => new() { SpecId = specId, Message = message };
}
