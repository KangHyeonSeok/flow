using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

[Collection("CommandGlobalState")]
public class SpecAppendReviewCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalCwd;
    private readonly TextWriter _originalOut;
    private readonly StringWriter _capturedOut;

    public SpecAppendReviewCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-spec-append-review-{Guid.NewGuid():N}");
        _originalCwd = Directory.GetCurrentDirectory();
        _originalOut = Console.Out;
        _capturedOut = new StringWriter();

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, ".flow"));
        Directory.SetCurrentDirectory(_tempDir);
        Console.SetOut(_capturedOut);
        Environment.ExitCode = 0;
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Directory.SetCurrentDirectory(_originalCwd);
        Environment.ExitCode = 0;

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch (IOException) { /* 다른 프로세스 잠금 시 정리 건너뜀 */ }
        }
    }

    [Fact]
    public void SpecAppendReview_AppliesReviewMetadataToSpec()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-301",
            Title = "리뷰 반영 테스트",
            Description = "review append command",
            Status = "needs-review",
            NodeType = "feature"
        });

        var inputFile = Path.Combine(_tempDir, "review.json");
        File.WriteAllText(inputFile, """
        {
          "summary": "사용자 판단 필요",
          "failureReasons": ["정책 선택 필요"],
          "suggestedAttempts": ["답변 후 재시도"],
          "requiresUserInput": true,
          "questions": [
            {
              "type": "user-decision",
              "question": "A와 B 중 어떤 동작이 맞나요?",
              "why": "정책 방향이 필요합니다."
            }
          ]
        }
        """);

        var app = new FlowApp();

        app.SpecAppendReview("F-301", inputFile: inputFile, reviewer: "runner-test");

        Environment.ExitCode.Should().Be(0);
        var updated = store.Get("F-301");
        updated.Should().NotBeNull();
        // open 질문이 남아 있으므로 "needs-review" 유지 (사용자 입력 대기)
        updated!.Status.Should().Be("needs-review");
        updated.Metadata.Should().NotBeNull();
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("open-question");
        updated.Metadata["reviewReason"].ToString().Should().Be("open-question");
        updated.Metadata.Should().NotContainKey("requiresUserInput");
        updated.Metadata["questionStatus"].ToString().Should().Be("waiting-user-input");
        updated.Activity.Should().HaveCount(2);
        var activity = updated.Activity.Last();
        activity.Role.Should().Be("tester");
        activity.Outcome.Should().Be("needs-review");
        activity.StatusChange!.From.Should().Be("needs-review");
        activity.StatusChange.To.Should().Be("needs-review");
        activity.Issues.Should().ContainSingle().Which.Should().Be("user-input-required");
        ReadCapturedOutput().Should().Contain("spec-append-review");
    }

    [Fact]
    public void SpecAppendReview_InvalidJson_ReportsErrorAndKeepsSpecUntouched()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-302",
            Title = "리뷰 파싱 실패 테스트",
            Description = "invalid review json",
            Status = "needs-review",
            NodeType = "feature"
        });

        var inputFile = Path.Combine(_tempDir, "invalid-review.json");
        File.WriteAllText(inputFile, """
        {
          "summary": "파싱 실패",
          "failureReasons": ["쉼표 누락"]
        """);

        var app = new FlowApp();

        app.SpecAppendReview("F-302", inputFile: inputFile, reviewer: "runner-test");

        Environment.ExitCode.Should().Be(1);
        var updated = store.Get("F-302");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("needs-review");
        updated.Metadata.Should().BeNull();
        ReadCapturedOutput().Should().Contain("리뷰 JSON 파싱 실패");
    }

    [Fact]
    public void SpecAppendReview_WhenReviewFindsMissingEvidence_RequeuesSpecAndResetsConditions()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-303",
            Title = "condition review requeue test",
            Description = "review append requeue",
            Status = "needs-review",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-303-C1",
                    Status = "draft"
                }
            ]
        });

        var inputFile = Path.Combine(_tempDir, "review-requeue.json");
        File.WriteAllText(inputFile, """
        {
          "summary": "증거가 부족해 자동 재시도가 필요합니다.",
          "failureReasons": ["최종 증거가 충분하지 않습니다."],
          "alternatives": ["테스트를 보강합니다."],
          "suggestedAttempts": ["추가 증거 수집 후 다시 실행"],
          "requiresUserInput": false,
          "questions": []
        }
        """);

        var app = new FlowApp();

        app.SpecAppendReview("F-303", inputFile: inputFile, reviewer: "runner-test");

        Environment.ExitCode.Should().Be(0);
        var updated = store.Get("F-303");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("queued");
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("missing-evidence");
        updated.Metadata["reviewReason"].ToString().Should().Be("missing-evidence");
        updated.Conditions[0].Status.Should().Be("draft");
        updated.Activity.Should().HaveCount(2);
        var activity = updated.Activity.Last();
        activity.Outcome.Should().Be("requeue");
        activity.ConditionUpdates.Should().ContainSingle(update =>
            update.ConditionId == "F-303-C1" &&
            update.Status == "draft" &&
            update.Reason == "reset-for-requeue");
    }

    [Fact]
    public void SpecAppendReview_WhenReviewVerifiesCondition_UpdatesConditionAndSpecStatus()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-304",
            Title = "condition review verify test",
            Description = "review append verify condition",
            Status = "needs-review",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-304-C1",
                    Status = "needs-review",
                    Metadata = new Dictionary<string, object>
                    {
                        ["requiresManualVerification"] = true,
                        ["manualVerificationItems"] = new object[] { "검토 필요" }
                    }
                }
            ]
        });

        var inputFile = Path.Combine(_tempDir, "review-verified.json");
        File.WriteAllText(inputFile, """
        {
          "summary": "조건 확인 완료",
          "failureReasons": [],
          "alternatives": [],
          "suggestedAttempts": ["추가 작업 불필요"],
                    "verifiedConditionIds": ["F-304-C1"],
          "requiresUserInput": false,
          "questions": []
        }
        """);

        var app = new FlowApp();

                app.SpecAppendReview("F-304", inputFile: inputFile, reviewer: "runner-test");

        Environment.ExitCode.Should().Be(0);
                var updated = store.Get("F-304");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("verified");
        updated.Conditions[0].Status.Should().Be("verified");
        updated.Conditions[0].Metadata.Should().NotContainKey("requiresManualVerification");
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("review-verified");
        updated.Metadata.Should().NotContainKey("reviewReason");
                updated.Activity.Should().HaveCount(2);
                var activity = updated.Activity.Last();
                activity.Outcome.Should().Be("verified");
                activity.Issues.Should().BeEmpty();
                activity.ConditionUpdates.Should().ContainSingle(update =>
                        update.ConditionId == "F-304-C1" &&
                        update.Status == "verified" &&
                        update.Reason == "automated-tests-passed");
    }

    private string ReadCapturedOutput()
    {
        var text = _capturedOut.ToString();
        _capturedOut.GetStringBuilder().Clear();
        return text;
    }
}