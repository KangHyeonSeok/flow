using System.Text.Json;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// PlannerAutoQueueService 자동 queued 승격 테스트 (F-019)
/// </summary>
public class PlannerAutoQueueServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SpecStore _store;
    private readonly PlannerAutoQueueService _svc;

    public PlannerAutoQueueServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-autoqueue-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SpecStore(_tempDir);
        _store.Initialize();
        _svc = new PlannerAutoQueueService(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── C1: 최소 완성 요건 ─────────────────────────────────────────────────

    [Fact]
    public void EvaluateEligibility_DraftFeatureWithConditionsAndDescription_IsEligible()
    {
        var spec = CreateFeatureSpec("F-001", "기능 설명이 충분히 길게 작성되었습니다 50자 이상");

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
        result.BlockReason.Should().BeNull();
        result.UnresolvedQuestions.Should().Be(0);
        result.PlannerState.Should().Be("standby");
    }

    [Fact]
    public void EvaluateEligibility_NonDraftStatus_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        spec.Status = "queued";

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.BlockReason.Should().Contain("draft");
    }

    [Fact]
    public void EvaluateEligibility_EmptyDescription_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        spec.Description = "";

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.BlockReason.Should().Contain("description");
    }

    [Fact]
    public void EvaluateEligibility_FeatureWithNoConditions_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        spec.Conditions.Clear();

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.BlockReason.Should().Contain("conditions");
    }

    [Fact]
    public void EvaluateEligibility_TaskWithNoConditions_IsEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        spec.NodeType = "task";
        spec.Conditions.Clear();

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
    }

    // ── C2: 금지 조건 ──────────────────────────────────────────────────────

    [Fact]
    public void EvaluateEligibility_PlannerStateWaiting_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        spec.Metadata = new Dictionary<string, object> { ["plannerState"] = "waiting-user-input" };

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.PlannerState.Should().Be("waiting-user-input");
    }

    [Fact]
    public void EvaluateEligibility_OpenQuestionsExist_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        var questionsJson = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "q1", question = "미결 질문", status = "open" }
        });
        spec.Metadata = new Dictionary<string, object> { ["questions"] = questionsJson };

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.UnresolvedQuestions.Should().Be(1);
        result.PlannerState.Should().Be("waiting-user-input");
    }

    [Fact]
    public void EvaluateEligibility_AllQuestionsAnswered_IsEligible()
    {
        var spec = CreateFeatureSpec("F-001");
        var questionsJson = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "q1", question = "질문", status = "answered", answer = "답변" }
        });
        spec.Metadata = new Dictionary<string, object> { ["questions"] = questionsJson };

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
        result.UnresolvedQuestions.Should().Be(0);
    }

    // ── C3: 승격 메타데이터 기록 ─────────────────────────────────────────

    [Fact]
    public void PromoteToQueued_EligibleSpec_SetsStatusAndPromotion()
    {
        var spec = _store.Create(CreateFeatureSpec("F-002"));
        _svc.PromoteToQueued(spec, "구현 준비 완료");

        // 저장 후 재조회하여 JsonElement로 역직렬화된 값 확인
        var promoted = _store.Get("F-002")!;

        promoted.Status.Should().Be("queued");
        promoted.Metadata.Should().ContainKey("promotion");
        promoted.Metadata.Should().ContainKey("plannerState");

        var promotionJson = (JsonElement)promoted.Metadata!["promotion"];
        promotionJson.GetProperty("source").GetString().Should().Be("planner-auto");
        promotionJson.GetProperty("reason").GetString().Should().Be("구현 준비 완료");
        promotionJson.GetProperty("promotedAt").GetString().Should().NotBeNullOrEmpty();
        promotionJson.GetProperty("confidence").GetDouble().Should().BeGreaterThan(0);
        promotionJson.GetProperty("unresolvedQuestions").GetInt32().Should().Be(0);
    }

    [Fact]
    public void PromoteToQueued_WithExplicitConfidence_UsesProvidedValue()
    {
        var spec = _store.Create(CreateFeatureSpec("F-003"));
        _svc.PromoteToQueued(spec, "이유", confidence: 0.95);

        var promoted = _store.Get("F-003")!;
        var promo = (JsonElement)promoted.Metadata!["promotion"];
        promo.GetProperty("confidence").GetDouble().Should().BeApproximately(0.95, 0.001);
    }

    [Fact]
    public void PromoteToQueued_IneligibleSpec_ThrowsException()
    {
        var spec = CreateFeatureSpec("F-004");
        spec.Status = "working";

        var act = () => _svc.PromoteToQueued(spec, "이유");

        act.Should().Throw<InvalidOperationException>().WithMessage("*자동 승격 불가*");
    }

    // ── C4: 승격 복원 ────────────────────────────────────────────────────

    [Fact]
    public void RevertToDraft_QueuedSpec_RestoresDraftAndPreservesHistory()
    {
        var spec = _store.Create(CreateFeatureSpec("F-005"));
        var promoted = _svc.PromoteToQueued(spec, "자동 승격");

        // 재조회해서 저장된 스펙으로 복원
        var refreshed = _store.Get("F-005")!;
        var reverted = _svc.RevertToDraft(refreshed, "추가 요구사항 발생");

        reverted.Status.Should().Be("draft");
        reverted.Metadata!["plannerState"].Should().Be("waiting-user-input");
        reverted.Metadata.Should().NotContainKey("requiresUserInput");

        // 이력 보존 확인
        var promoDict = reverted.Metadata["promotion"] as Dictionary<string, object>;
        promoDict.Should().NotBeNull();
        promoDict!.Should().ContainKey("revertedAt");
        promoDict.Should().ContainKey("revertReason");
        promoDict["revertReason"].Should().Be("추가 요구사항 발생");
        // 원래 promotedAt 등 이력이 남아 있어야 함
        promoDict.Should().ContainKey("promotedAt");
        promoDict.Should().ContainKey("source");
    }

    [Fact]
    public void RevertToDraft_NonQueuedSpec_ThrowsException()
    {
        var spec = CreateFeatureSpec("F-006");
        spec.Status = "draft";

        var act = () => _svc.RevertToDraft(spec, "이유");

        act.Should().Throw<InvalidOperationException>().WithMessage("*queued*");
    }

    // ── C5: 대체/변형 스펙 자동 승격 금지 ──────────────────────────────────

    [Fact]
    public void EvaluateEligibility_SupersedesWithoutActivity_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-007");
        spec.Supersedes.Add("F-003");
        // activity에 supersede 항목 없음

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.PlannerState.Should().Be("waiting-user-input");
        result.BlockReason.Should().Contain("supersedes");
    }

    [Fact]
    public void EvaluateEligibility_SupersedesWithActivity_IsEligible()
    {
        var spec = CreateFeatureSpec("F-008");
        spec.Supersedes.Add("F-003");
        spec.Activity.Add(new SpecActivityEntry
        {
            Kind = "supersede",
            At = DateTime.UtcNow.ToString("o"),
            Role = "planner",
            Actor = "planner",
            Summary = "F-003을 F-008로 대체",
            RelatedIds = ["F-003"],
            Outcome = "done"
        });

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public void EvaluateEligibility_MutatesWithoutActivity_IsNotEligible()
    {
        var spec = CreateFeatureSpec("F-009");
        spec.Mutates.Add("F-005");
        // activity에 mutate 항목 없음

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeFalse();
        result.BlockReason.Should().Contain("mutates");
    }

    [Fact]
    public void EvaluateEligibility_MutatesWithActivity_IsEligible()
    {
        var spec = CreateFeatureSpec("F-010");
        spec.Mutates.Add("F-005");
        spec.Activity.Add(new SpecActivityEntry
        {
            Kind = "mutate",
            At = DateTime.UtcNow.ToString("o"),
            Role = "planner",
            Actor = "planner",
            Summary = "F-005 in-place 수정",
            RelatedIds = ["F-005"],
            Outcome = "done"
        });

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
    }

    // ── 신뢰도 계산 ──────────────────────────────────────────────────────

    [Fact]
    public void EvaluateEligibility_FullyPopulatedSpec_HasHighConfidence()
    {
        var spec = CreateFeatureSpec("F-011", "아주 충분히 긴 설명 50자 이상을 만족하는 설명문 입니다");
        spec.Dependencies.Add("F-001");
        spec.CodeRefs.Add("src/some/file.cs");
        spec.Conditions.Add(new SpecCondition { Id = "F-011-C2", Description = "c2" });
        spec.Conditions.Add(new SpecCondition { Id = "F-011-C3", Description = "c3" });

        var result = _svc.EvaluateEligibility(spec);

        result.IsEligible.Should().BeTrue();
        result.Confidence.Should().BeGreaterThan(0.8);
    }

    // ── SpecValidator C5 검증 ────────────────────────────────────────────

    [Fact]
    public void ValidateAutoQueueEligibility_SupersedesNoLog_ReturnsError()
    {
        var validator = new SpecValidator();
        var spec = CreateFeatureSpec("F-012");
        spec.Supersedes.Add("F-001");

        var result = validator.ValidateAutoQueueEligibility(spec);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "supersedes");
    }

    [Fact]
    public void ValidateAutoQueueEligibility_MutatesNoLog_ReturnsError()
    {
        var validator = new SpecValidator();
        var spec = CreateFeatureSpec("F-013");
        spec.Mutates.Add("F-002");

        var result = validator.ValidateAutoQueueEligibility(spec);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "mutates");
    }

    [Fact]
    public void ValidateAutoQueueEligibility_NoRelations_IsValid()
    {
        var validator = new SpecValidator();
        var spec = CreateFeatureSpec("F-014");

        var result = validator.ValidateAutoQueueEligibility(spec);

        result.IsValid.Should().BeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static SpecNode CreateFeatureSpec(string id, string? description = null) => new()
    {
        Id = id,
        NodeType = "feature",
        Title = $"{id} 테스트 기능",
        Description = description ?? $"{id} 기능 설명",
        Status = "draft",
        Conditions = [new SpecCondition { Id = $"{id}-C1", Description = "기본 조건" }]
    };
}
