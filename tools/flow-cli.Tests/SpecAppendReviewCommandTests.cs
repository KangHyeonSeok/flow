using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

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
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("needs-user-decision");
        updated.Metadata.Should().NotContainKey("requiresUserInput");
        updated.Metadata["questionStatus"].ToString().Should().Be("waiting-user-input");
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
    public void SpecAppendReview_WhenReviewVerifiesCondition_UpdatesConditionAndSpecStatus()
    {
        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = "F-303",
            Title = "condition review verify test",
            Description = "review append verify condition",
            Status = "needs-review",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-303-C1",
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
          "verifiedConditionIds": ["F-303-C1"],
          "requiresUserInput": false,
          "questions": []
        }
        """);

        var app = new FlowApp();

        app.SpecAppendReview("F-303", inputFile: inputFile, reviewer: "runner-test");

        Environment.ExitCode.Should().Be(0);
        var updated = store.Get("F-303");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("verified");
        updated.Conditions[0].Status.Should().Be("verified");
        updated.Conditions[0].Metadata.Should().NotContainKey("requiresManualVerification");
        updated.Metadata!["reviewDisposition"].ToString().Should().Be("review-verified");
    }

    private string ReadCapturedOutput()
    {
        var text = _capturedOut.ToString();
        _capturedOut.GetStringBuilder().Clear();
        return text;
    }
}