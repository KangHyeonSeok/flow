using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

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
                    Status = "working",
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
        ReadCapturedOutput().Should().Contain("spec-record-condition-review");
    }

    [Fact]
    public void SpecRecordConditionReview_Failed_KeepsManualFlagAndNeedsReview()
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
                    Status = "working",
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
        updated!.Status.Should().Be("needs-review");
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("manual-verification-failed");

        var condition = updated.Conditions.Single();
        condition.Status.Should().Be("needs-review");
        condition.Metadata.Should().NotBeNull();
        condition.Metadata!["requiresManualVerification"].Should().Be(true);
        condition.Metadata["manualVerificationStatus"].ToString().Should().Be("failed");
        condition.Tests.Should().ContainSingle(test => test.TestId == "manual-review:F-402-C1" && test.Status == "failed" && test.ErrorMessage == "저장 후 오류 토스트 발생");
    }

    private string ReadCapturedOutput()
    {
        var text = _capturedOut.ToString();
        _capturedOut.GetStringBuilder().Clear();
        return text;
    }
}