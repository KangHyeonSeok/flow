using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

[Collection("CommandGlobalState")]
public class SpecRecordConditionReviewCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalCwd;
    private readonly TextWriter _originalOut;
    private readonly StringWriter _capturedOut;

    public SpecRecordConditionReviewCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-spec-record-condition-review-{Guid.NewGuid():N}");
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
            catch (IOException) { }
        }
    }

    [Fact]
    public void SpecRecordConditionReview_Passed_ClearsManualFlagAndVerifiesSpec()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-401",
            Title = "수동 검증 통과",
            Description = "manual pass",
            Status = "needs-review",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-401-C1",
                    Description = "사용자 확인 필요",
                    Status = "needs-review",
                    Metadata = new Dictionary<string, object>
                    {
                        ["requiresManualVerification"] = true,
                        ["manualVerificationReason"] = "UI 확인 필요",
                        ["manualVerificationItems"] = new object[] { "버튼 클릭 후 성공 메시지 확인" }
                    }
                }
            ]
        });

        var app = new FlowApp();

        app.SpecRecordConditionReview("F-401", conditionId: "F-401-C1", result: "passed", comment: "정상 동작 확인", reviewer: "qa-user");

        Environment.ExitCode.Should().Be(0);
        var updated = store.Get("F-401");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("verified");
        updated.Metadata!["verificationSource"].ToString().Should().Be("manual-review");
        updated.Metadata.Should().NotContainKey("reviewReason");

        var condition = updated.Conditions.Single();
        condition.Status.Should().Be("verified");
        condition.Metadata.Should().NotBeNull();
        condition.Metadata.Should().NotContainKey("requiresManualVerification");
        condition.Metadata.Should().NotContainKey("manualVerificationReason");
        condition.Metadata.Should().NotContainKey("manualVerificationItems");
        condition.Metadata!["manualVerificationStatus"].ToString().Should().Be("passed");
        condition.Tests.Should().ContainSingle(test => test.TestId == "manual-review:F-401-C1" && test.Status == "passed");
        condition.Evidence.Should().ContainSingle(ev => ev.Path == "manual-review://F-401/F-401-C1");
        updated.Evidence.Should().ContainSingle(ev => ev.Path == "manual-review://F-401/F-401-C1");
        updated.Activity.Should().HaveCount(1);
        var activity = updated.Activity.Last();
        activity.Outcome.Should().Be("verified");
        activity.Issues.Should().BeEmpty();
        activity.ConditionUpdates.Should().ContainSingle(update =>
            update.ConditionId == "F-401-C1" &&
            update.Status == "verified" &&
            update.Reason == "manual-tests-passed");
        ReadCapturedOutput().Should().Contain("spec-record-condition-review");
    }

    [Fact]
    public void SpecRecordConditionReview_Failed_RequeuesSpecAndResetsConditions()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-402",
            Title = "수동 검증 실패",
            Description = "manual fail",
            Status = "needs-review",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-402-C1",
                    Description = "사용자 확인 필요",
                    Status = "needs-review",
                    Metadata = new Dictionary<string, object>
                    {
                        ["requiresManualVerification"] = true,
                        ["manualVerificationItems"] = new object[] { "실패 케이스 확인" }
                    }
                }
            ]
        });

        var app = new FlowApp();

        app.SpecRecordConditionReview("F-402", conditionId: "F-402-C1", result: "failed", comment: "저장 후 오류 토스트 발생", reviewer: "qa-user");

        Environment.ExitCode.Should().Be(0);
        var updated = store.Get("F-402");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("queued");
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("test-failed");
        updated.Metadata["reviewReason"].ToString().Should().Be("test-failed");

        var condition = updated.Conditions.Single();
        condition.Status.Should().Be("draft");
        condition.Metadata.Should().NotBeNull();
        condition.Metadata.Should().NotContainKey("requiresManualVerification");
        condition.Metadata.Should().NotContainKey("manualVerificationStatus");
        condition.Tests.Should().ContainSingle(test => test.TestId == "manual-review:F-402-C1" && test.Status == "failed" && test.ErrorMessage == "저장 후 오류 토스트 발생");
        updated.Activity.Should().HaveCount(1);
        var activity = updated.Activity.Last();
        activity.Outcome.Should().Be("requeue");
        activity.Issues.Should().ContainSingle().Which.Should().Be("test-failed");
        activity.ConditionUpdates.Should().ContainSingle(update =>
            update.ConditionId == "F-402-C1" &&
            update.Status == "draft" &&
            update.Reason == "reset-for-requeue");
    }

    private string ReadCapturedOutput()
    {
        var text = _capturedOut.ToString();
        _capturedOut.GetStringBuilder().Clear();
        return text;
    }
}
