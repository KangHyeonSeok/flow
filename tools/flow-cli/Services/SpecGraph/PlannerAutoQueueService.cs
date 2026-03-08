using System.Text.Json;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// Spec Planner 자동 queued 승격 서비스 (F-019).
/// draft 스펙이 자동 승격 조건을 충족하는지 평가하고, 승격·복원을 수행한다.
/// </summary>
public class PlannerAutoQueueService
{
    private readonly SpecStore _store;

    public PlannerAutoQueueService(SpecStore store)
    {
        _store = store;
    }

    // ── 평가 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// draft 스펙의 자동 queued 승격 가능 여부를 평가한다 (F-019-C1, C2, C5).
    /// </summary>
    /// <param name="spec">평가 대상 스펙</param>
    /// <param name="allSpecs">양방향 supersedes/mutates 확인용 전체 스펙 목록 (선택)</param>
    public PlannerAutoQueueEligibility EvaluateEligibility(SpecNode spec, List<SpecNode>? allSpecs = null)
    {
        // C1: draft 상태여야 함
        if (!string.Equals(spec.Status, "draft", StringComparison.OrdinalIgnoreCase))
            return Ineligible("스펙 상태가 draft가 아닙니다.", 0, "standby");

        // C5: supersedes 또는 mutates 관계가 changeLog로 확정되지 않았으면 차단
        if (HasUnconfirmedRelationReview(spec))
            return Ineligible(
                "기존 스펙 대체(supersedes) 또는 변형(mutates) 관계가 확정되지 않았습니다. " +
                "변경 관리 규칙에 따라 관계를 먼저 changeLog에 기록하세요.",
                0, "waiting-user-input");

        // C2: plannerState='waiting-user-input' 이면 차단
        if (HasPlannerStateWaiting(spec))
            return Ineligible("metadata.plannerState='waiting-user-input' — 사용자 판단 대기 중", 0, "waiting-user-input");

        // C1: open 상태 질문이 0개여야 함
        int unresolvedCount = CountUnresolvedQuestions(spec);
        if (unresolvedCount > 0)
            return Ineligible($"미해결 질문 {unresolvedCount}개 있음 — 모든 질문이 해결된 후에 승격 가능합니다.", unresolvedCount, "waiting-user-input");

        // C1: description은 필수
        if (string.IsNullOrWhiteSpace(spec.Description))
            return Ineligible("description이 비어 있습니다.", 0, "standby");

        // C1: feature 타입은 수락 조건(conditions)이 최소 1개 이상 필요
        if (string.Equals(spec.NodeType, "feature", StringComparison.OrdinalIgnoreCase) &&
            spec.Conditions.Count == 0)
            return Ineligible("feature 타입은 수락 조건(conditions)이 최소 1개 이상 필요합니다.", 0, "standby");

        double confidence = CalculateConfidence(spec);

        return new PlannerAutoQueueEligibility
        {
            IsEligible = true,
            BlockReason = null,
            Confidence = confidence,
            UnresolvedQuestions = 0,
            PlannerState = "standby"
        };
    }

    // ── 승격 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// draft 스펙을 queued로 자동 승격한다 (F-019-C3).
    /// metadata.promotion 에 source, reason, promotedAt, confidence, unresolvedQuestions 를 기록한다.
    /// </summary>
    /// <param name="spec">승격 대상 스펙</param>
    /// <param name="reason">승격 근거 설명</param>
    /// <param name="confidence">신뢰도 (null이면 자동 계산)</param>
    /// <param name="allSpecs">C5 검사용 전체 스펙 목록 (선택)</param>
    public SpecNode PromoteToQueued(SpecNode spec, string reason, double? confidence = null, List<SpecNode>? allSpecs = null)
    {
        var eligibility = EvaluateEligibility(spec, allSpecs);
        if (!eligibility.IsEligible)
            throw new InvalidOperationException($"자동 승격 불가: {eligibility.BlockReason}");

        var promotedAt = DateTime.UtcNow.ToString("o");
        spec.Status = "queued";
        spec.Metadata ??= new Dictionary<string, object>();

        // C3: 승격 근거 기록
        spec.Metadata["promotion"] = new Dictionary<string, object>
        {
            ["source"] = "planner-auto",
            ["reason"] = reason,
            ["promotedAt"] = promotedAt,
            ["confidence"] = confidence ?? eligibility.Confidence,
            ["unresolvedQuestions"] = 0
        };
        spec.Metadata["plannerState"] = eligibility.PlannerState;

        return _store.Update(spec);
    }

    // ── 복원 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 자동 승격된 queued 스펙을 draft로 복원한다 (F-019-C4).
    /// 승격 이력(metadata.promotion)은 삭제하지 않고 revertedAt/revertReason을 추가하여 추적 가능하게 한다.
    /// </summary>
    /// <param name="spec">복원 대상 스펙</param>
    /// <param name="revertReason">복원 사유 (사용자 요구 또는 반대 의견)</param>
    public SpecNode RevertToDraft(SpecNode spec, string revertReason)
    {
        if (!string.Equals(spec.Status, "queued", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"queued 상태가 아닌 스펙은 복원할 수 없습니다. 현재 상태: {spec.Status}");

        spec.Status = "draft";
        spec.Metadata ??= new Dictionary<string, object>();

        // C4: 자동 승격 이력은 삭제하지 않고 revert 정보만 추가 (이력 보존)
        if (spec.Metadata.TryGetValue("promotion", out var existingPromotion))
            spec.Metadata["promotion"] = BuildRevertedPromotion(existingPromotion, revertReason);

        spec.Metadata["plannerState"] = "waiting-user-input";

        return _store.Update(spec);
    }

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// C5: supersedes 또는 mutates에 대한 changeLog 확정 여부를 검사한다.
    /// supersedes가 있으면 changeLog에 "supersede" 타입 항목이, mutates가 있으면 "mutate" 항목이 있어야 한다.
    /// </summary>
    private static bool HasUnconfirmedRelationReview(SpecNode spec)
    {
        if (spec.Supersedes.Count > 0)
        {
            bool hasLog = spec.ChangeLog.Any(e =>
                string.Equals(e.Type, "supersede", StringComparison.OrdinalIgnoreCase));
            if (!hasLog) return true;
        }

        if (spec.Mutates.Count > 0)
        {
            bool hasLog = spec.ChangeLog.Any(e =>
                string.Equals(e.Type, "mutate", StringComparison.OrdinalIgnoreCase));
            if (!hasLog) return true;
        }

        return false;
    }

    private static bool HasPlannerStateWaiting(SpecNode spec)
    {
        if (spec.Metadata == null) return false;
        if (!spec.Metadata.TryGetValue("plannerState", out var val)) return false;
        var state = val is JsonElement je ? je.GetString() : val?.ToString();
        return string.Equals(state, "waiting-user-input", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountUnresolvedQuestions(SpecNode spec)
    {
        if (spec.Metadata == null) return 0;
        if (!spec.Metadata.TryGetValue("questions", out var qVal)) return 0;

        if (qVal is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
        {
            return arr.EnumerateArray()
                .Count(q => q.ValueKind == JsonValueKind.Object &&
                            q.TryGetProperty("status", out var statusProp) &&
                            string.Equals(statusProp.GetString(), "open", StringComparison.OrdinalIgnoreCase));
        }

        return 0;
    }

    /// <summary>
    /// 스펙 완성도를 기반으로 승격 신뢰도(0.0~1.0)를 계산한다 (C3: confidence).
    /// </summary>
    private static double CalculateConfidence(SpecNode spec)
    {
        double score = 0.5;

        if (!string.IsNullOrWhiteSpace(spec.Description) && spec.Description.Length > 50)
            score += 0.1;

        if (spec.Conditions.Count >= 3)
            score += 0.2;
        else if (spec.Conditions.Count >= 1)
            score += 0.1;

        if (spec.Dependencies.Count > 0)
            score += 0.1;

        if (spec.CodeRefs.Count > 0)
            score += 0.1;

        return Math.Min(1.0, Math.Round(score, 2));
    }

    /// <summary>
    /// 기존 promotion 객체에 revert 정보를 추가한 새 객체를 반환한다 (C4: 이력 보존).
    /// </summary>
    private static Dictionary<string, object> BuildRevertedPromotion(object existingPromotion, string revertReason)
    {
        var result = new Dictionary<string, object>
        {
            ["source"] = "planner-auto",
            ["reason"] = "",
            ["promotedAt"] = "",
            ["confidence"] = 0.0,
            ["unresolvedQuestions"] = 0
        };

        if (existingPromotion is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("source", out var p)) result["source"] = p.GetString() ?? "planner-auto";
            if (je.TryGetProperty("reason", out var r)) result["reason"] = r.GetString() ?? "";
            if (je.TryGetProperty("promotedAt", out var at)) result["promotedAt"] = at.GetString() ?? "";
            if (je.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var cd)) result["confidence"] = cd;
            if (je.TryGetProperty("unresolvedQuestions", out var u) && u.TryGetInt32(out var ui)) result["unresolvedQuestions"] = ui;
        }
        else if (existingPromotion is Dictionary<string, object> dict)
        {
            foreach (var kv in dict)
                result[kv.Key] = kv.Value;
        }

        result["revertedAt"] = DateTime.UtcNow.ToString("o");
        result["revertReason"] = revertReason;
        return result;
    }

    private static PlannerAutoQueueEligibility Ineligible(string reason, int unresolvedQuestions, string plannerState)
        => new()
        {
            IsEligible = false,
            BlockReason = reason,
            Confidence = 0,
            UnresolvedQuestions = unresolvedQuestions,
            PlannerState = plannerState
        };
}
