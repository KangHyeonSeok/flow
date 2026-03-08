using System.Text.Json;
using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class SpecReviewEvaluatorTests
{
    [Fact]
    public void Evaluate_AllVerifiedConditionsWithoutManualRequirement_CanAutoVerify()
    {
        var spec = CreateSpec(
            status: "needs-review",
            conditions:
            [
                CreateCondition("F-100-C1", "verified"),
                CreateCondition("F-100-C2", "verified")
            ]);

        var evaluation = SpecReviewEvaluator.Evaluate(spec);

        evaluation.TotalConditions.Should().Be(2);
        evaluation.VerifiedConditions.Should().Be(2);
        evaluation.AllConditionsVerified.Should().BeTrue();
        evaluation.RequiresManualVerification.Should().BeFalse();
        evaluation.CanAutoVerify.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SpecLevelManualVerification_BlocksAutoVerify()
    {
        var spec = CreateSpec(
            status: "needs-review",
            conditions:
            [CreateCondition("F-100-C1", "verified")],
            metadata: new Dictionary<string, object>
            {
                ["requiresManualVerification"] = true,
                ["manualVerificationReason"] = "배포 전 실제 장비에서 확인 필요"
            });

        var evaluation = SpecReviewEvaluator.Evaluate(spec);

        evaluation.CanAutoVerify.Should().BeFalse();
        evaluation.RequiresManualVerification.Should().BeTrue();
        evaluation.ManualVerificationItems.Should().ContainSingle(item =>
            item.Source == "spec" &&
            item.Label == "F-100" &&
            item.Reason == "배포 전 실제 장비에서 확인 필요");
    }

    [Fact]
    public void Evaluate_ConditionLevelManualVerificationItems_AreCollectedFromJsonMetadata()
    {
        const string json = """
        {
          "id": "F-100",
          "title": "Manual review test",
          "description": "desc",
          "status": "needs-review",
          "conditions": [
            {
              "id": "F-100-C1",
              "nodeType": "condition",
              "description": "visual confirmation",
              "status": "verified",
              "codeRefs": [],
              "evidence": [],
              "metadata": {
                "requiresManualVerification": true,
                "manualVerificationItems": [
                  {
                    "label": "로그인 화면 육안 확인",
                    "reason": "실제 렌더링 품질은 자동화로 판정 불가"
                  }
                ]
              }
            }
          ],
          "codeRefs": [],
          "evidence": [],
          "tags": [],
          "metadata": {}
        }
        """;

        var spec = JsonSerializer.Deserialize<SpecNode>(json);

        spec.Should().NotBeNull();
        var evaluation = SpecReviewEvaluator.Evaluate(spec!);

        evaluation.RequiresManualVerification.Should().BeTrue();
        evaluation.CanAutoVerify.Should().BeFalse();
        evaluation.ManualVerificationItems.Should().ContainSingle(item =>
            item.Source == "condition" &&
            item.ConditionId == "F-100-C1" &&
            item.Label == "로그인 화면 육안 확인" &&
            item.Reason == "실제 렌더링 품질은 자동화로 판정 불가");
    }

    [Fact]
    public void Evaluate_WithoutConditions_CannotAutoVerify()
    {
        var spec = CreateSpec(status: "needs-review", conditions: []);

        var evaluation = SpecReviewEvaluator.Evaluate(spec);

        evaluation.HasConditions.Should().BeFalse();
        evaluation.CanAutoVerify.Should().BeFalse();
    }

      [Fact]
      public void PromoteVerifiedConditionsFromArtifacts_PromotesConditionWithPassingTestsAndEvidence()
      {
        var spec = CreateSpec(
          status: "needs-review",
          conditions:
          [
            CreateCondition(
              "F-100-C1",
              "needs-review",
              tests:
              [
                new TestLink { TestId = "test-1", Status = "passed" }
              ],
              evidence:
              [
                new SpecEvidence { Type = "test-result", Path = "artifacts/test-results.xml" }
              ])
          ]);

        var promoted = SpecReviewEvaluator.PromoteVerifiedConditionsFromArtifacts(
          spec,
          "runner-01",
          "runner-review-artifacts",
          DateTime.Parse("2026-03-09T00:00:00Z"));

        promoted.Should().Be(1);
        spec.Conditions[0].Status.Should().Be("verified");
        spec.Conditions[0].Metadata.Should().NotBeNull();
        spec.Conditions[0].Metadata!["verificationSource"].Should().Be("runner-review-artifacts");
        spec.Conditions[0].Metadata["lastVerifiedBy"].Should().Be("runner-01");
      }

      [Fact]
      public void PromoteVerifiedConditionsFromArtifacts_DoesNotPromoteWhenManualVerificationIsRequired()
      {
        var spec = CreateSpec(
          status: "needs-review",
          conditions:
          [
            CreateCondition(
              "F-100-C1",
              "needs-review",
              tests:
              [
                new TestLink { TestId = "test-1", Status = "passed" }
              ],
              evidence:
              [
                new SpecEvidence { Type = "test-result", Path = "artifacts/test-results.xml" }
              ],
              metadata: new Dictionary<string, object>
              {
                ["requiresManualVerification"] = true,
                ["manualVerificationItems"] = new object[] { "화면 색상 확인" }
              })
          ]);

        var promoted = SpecReviewEvaluator.PromoteVerifiedConditionsFromArtifacts(
          spec,
          "runner-01",
          "runner-review-artifacts",
          DateTime.Parse("2026-03-09T00:00:00Z"));

        promoted.Should().Be(0);
        spec.Conditions[0].Status.Should().Be("needs-review");
      }

      [Fact]
      public void PromoteVerifiedConditionsFromArtifacts_DoesNotPromoteWhenEvidenceIsMissingOrTestsFailed()
      {
        var spec = CreateSpec(
          status: "needs-review",
          conditions:
          [
            CreateCondition(
              "F-100-C1",
              "needs-review",
              tests:
              [
                new TestLink { TestId = "test-1", Status = "passed" }
              ]),
            CreateCondition(
              "F-100-C2",
              "needs-review",
              tests:
              [
                new TestLink { TestId = "test-2", Status = "failed" }
              ],
              evidence:
              [
                new SpecEvidence { Type = "test-result", Path = "artifacts/test-results.xml" }
              ])
          ]);

        var promoted = SpecReviewEvaluator.PromoteVerifiedConditionsFromArtifacts(
          spec,
          "runner-01",
          "runner-review-artifacts",
          DateTime.Parse("2026-03-09T00:00:00Z"));

        promoted.Should().Be(0);
        spec.Conditions.Should().OnlyContain(condition => condition.Status == "needs-review");
      }

    private static SpecNode CreateSpec(
        string status,
        List<SpecCondition> conditions,
        Dictionary<string, object>? metadata = null,
        string? description = null,
        List<string>? codeRefs = null) => new()
    {
        Id = "F-100",
        Title = "Spec review evaluator",
        Description = description ?? "desc",
        Status = status,
        Conditions = conditions,
        Metadata = metadata ?? new Dictionary<string, object>(),
        CodeRefs = codeRefs ?? new List<string>()
    };

      private static SpecCondition CreateCondition(
        string id,
        string status,
        List<string>? codeRefs = null,
        List<TestLink>? tests = null,
        List<SpecEvidence>? evidence = null,
        Dictionary<string, object>? metadata = null) => new()
    {
        Id = id,
        Description = id,
        Status = status,
        CodeRefs = codeRefs ?? new List<string>(),
        Tests = tests ?? new List<TestLink>(),
        Evidence = evidence ?? new List<SpecEvidence>(),
        Metadata = metadata
    };
}