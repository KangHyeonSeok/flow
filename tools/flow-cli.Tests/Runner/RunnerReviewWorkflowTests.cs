using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class RunnerReviewWorkflowTests
{
    [Fact]
    public void ParseReviewAnalysis_ParsesMarkdownWrappedJson()
    {
        const string output = """
        검토 결과입니다.
        {
          "summary": "머지 실패로 재시도 필요",
          "failureReasons": ["main 브랜치와 충돌"],
          "alternatives": ["충돌 범위를 줄여 재시도"],
          "suggestedAttempts": ["최근 main 기준으로 다시 구현"],
          "requiresUserInput": true,
          "additionalInformationRequests": ["충돌 시 어떤 구현을 우선할지 결정 필요"],
          "questions": [
            {
              "type": "user-decision",
              "question": "신규 구현과 기존 구현 중 어느 쪽을 우선할까요?",
              "why": "머지 충돌 해소 기준이 필요합니다."
            }
          ]
        }
        """;

        var parsed = RunnerService.TryParseReviewAnalysis(output, out var analysis);

        parsed.Should().BeTrue();
        analysis.Summary.Should().Be("머지 실패로 재시도 필요");
        analysis.VerifiedConditionIds.Should().BeEmpty();
        analysis.RequiresUserInput.Should().BeTrue();
        analysis.Questions.Should().ContainSingle();
    }

    [Fact]
    public void ParseReviewAnalysis_ParsesVerifiedConditionIds()
    {
        const string output = """
        {
          "summary": "조건 확인 완료",
          "failureReasons": [],
          "alternatives": [],
          "suggestedAttempts": [],
          "verifiedConditionIds": ["F-210-C1", "F-210-C2"],
          "requiresUserInput": false,
          "additionalInformationRequests": [],
          "questions": []
        }
        """;

        var parsed = RunnerService.TryParseReviewAnalysis(output, out var analysis);

        parsed.Should().BeTrue();
        analysis.VerifiedConditionIds.Should().Equal("F-210-C1", "F-210-C2");
    }

    [Fact]
    public void TryParseReviewAnalysisJson_InvalidJson_ReturnsDetailedError()
    {
        const string output = """
        {
          "summary": "파싱 실패",
          "failureReasons": ["쉼표 누락"]
        """;

        var parsed = RunnerService.TryParseReviewAnalysisJson(output, out _, out var errorMessage);

        parsed.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("리뷰 JSON 파싱 실패");
    }

        [Fact]
        public void ParseReviewAnalysis_FiltersInternalArtifactRequests()
        {
                const string output = """
                {
                    "summary": "자동 분석에 내부 실행 정보가 더 필요합니다.",
                    "failureReasons": ["현재 컨텍스트만으로 원인 확정이 어렵습니다."],
                    "alternatives": ["검토자가 내부 로그를 확인한 뒤 재시도"],
                    "suggestedAttempts": ["runner 로그와 변경 사항을 내부적으로 점검"],
                    "requiresUserInput": true,
                    "additionalInformationRequests": ["검토 실행의 전체 로그와 변경 파일 목록을 제공해 주시겠습니까?"],
                    "questions": [
                        {
                            "type": "missing-info",
                            "question": "검토 실행의 전체 로그와 변경 파일 목록을 제공해 주시겠습니까?",
                            "why": "자동 원인 분석을 위해 상세 로그와 변경 사항이 필요합니다."
                        }
                    ]
                }
                """;

                var parsed = RunnerService.TryParseReviewAnalysis(output, out var analysis);

                parsed.Should().BeTrue();
                analysis.RequiresUserInput.Should().BeFalse();
                analysis.AdditionalInformationRequests.Should().BeEmpty();
                analysis.Questions.Should().BeEmpty();
        }

    [Fact]
    public void ApplyReviewAnalysis_WhenQuestionsExist_RequeuesAndTracksOpenQuestions()
    {
        var spec = new SpecNode
        {
            Id = "F-200",
            NodeType = "feature",
            Status = "needs-review",
            Metadata = new Dictionary<string, object>()
        };
        var analysis = new SpecReviewAnalysis
        {
            Summary = "사용자 판단 필요",
            FailureReasons = ["정책 선택 필요"],
            SuggestedAttempts = ["답변 후 재시도"],
            RequiresUserInput = true,
            Questions =
            [
                new SpecReviewQuestion
                {
                    Type = "user-decision",
                    Question = "A와 B 중 어떤 동작이 맞나요?",
                    Why = "정책 방향에 따라 구현이 달라집니다."
                }
            ]
        };

        RunnerService.ApplyReviewAnalysis(spec, analysis, "runner-test", DateTime.Parse("2026-03-08T00:00:00Z"));

        // open 질문이 남아 있으면 "needs-review" 유지 (사용자 입력 대기, 자동 재처리 금지)
        spec.Status.Should().Be("needs-review");
        spec.Metadata.Should().NotContainKey("requiresUserInput");
        ((Dictionary<string, object>)spec.Metadata!["review"]).Should().NotContainKey("requiresUserInput");
        spec.Metadata["questionStatus"].Should().Be("waiting-user-input");
        spec.Metadata["reviewDisposition"].Should().Be("open-question");
        spec.Metadata["reviewReason"].Should().Be("open-question");
        spec.Activity.Should().ContainSingle();
        spec.Activity[0].Outcome.Should().Be("needs-review");
        spec.Activity[0].Issues.Should().ContainSingle().Which.Should().Be("user-input-required");
    }

    [Fact]
    public void ApplyReviewAnalysis_WithoutQuestions_RequeuesSpec()
    {
        var spec = new SpecNode
        {
            Id = "F-201",
            NodeType = "feature",
            Status = "needs-review",
            Metadata = new Dictionary<string, object>
            {
                ["questionStatus"] = "waiting-user-input"
            }
        };
        var analysis = new SpecReviewAnalysis
        {
            Summary = "사용자 입력 없이 재시도 가능",
            FailureReasons = ["구현 범위 조정 필요"],
            Alternatives = ["더 작은 변경으로 분리"],
            SuggestedAttempts = ["review 사유를 정리한 뒤 다시 작업"],
            RequiresUserInput = false
        };

        RunnerService.ApplyReviewAnalysis(spec, analysis, "runner-test", DateTime.Parse("2026-03-08T00:00:00Z"));

        spec.Status.Should().Be("queued");
        spec.Metadata.Should().NotContainKey("requiresUserInput");
        spec.Metadata.Should().NotContainKey("questionStatus");
        spec.Metadata["reviewDisposition"].Should().Be("missing-evidence");
        spec.Metadata["reviewReason"].Should().Be("missing-evidence");
        spec.Activity.Should().ContainSingle();
        spec.Activity[0].Outcome.Should().Be("requeue");
    }

    [Fact]
    public void ApplyReviewAnalysis_WhenReviewVerifiesAllConditions_MarksSpecVerified()
    {
        var spec = new SpecNode
        {
            Id = "F-202",
            NodeType = "feature",
            Status = "needs-review",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-202-C1",
                    Status = "needs-review",
                    Metadata = new Dictionary<string, object>
                    {
                        ["requiresManualVerification"] = true,
                        ["manualVerificationItems"] = new object[] { "검토 필요" }
                    }
                }
            ],
            Metadata = new Dictionary<string, object>()
        };
        var analysis = new SpecReviewAnalysis
        {
            Summary = "조건 확인 완료",
            SuggestedAttempts = ["추가 작업 불필요"],
            VerifiedConditionIds = ["F-202-C1"],
            RequiresUserInput = false
        };

        RunnerService.ApplyReviewAnalysis(spec, analysis, "runner-test", DateTime.Parse("2026-03-08T00:00:00Z"));

        spec.Status.Should().Be("verified");
        spec.Conditions[0].Status.Should().Be("verified");
        spec.Conditions[0].Metadata.Should().NotContainKey("requiresManualVerification");
        ((Dictionary<string, object>)spec.Metadata!["review"])["verifiedConditionIds"].Should().NotBeNull();
        spec.Metadata["reviewDisposition"].Should().Be("review-verified");
        spec.Metadata.Should().NotContainKey("reviewReason");
        spec.Metadata["verificationSource"].Should().Be("copilot-cli-review");
        spec.Activity.Should().ContainSingle();
        spec.Activity[0].Outcome.Should().Be("verified");
    }
}