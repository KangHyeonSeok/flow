using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Backend;
using FlowCore.Models;
using FluentAssertions;

namespace FlowCore.Tests;

public class OutputParserTests
{
    private static readonly OutputParser Parser = new();

    private static AgentInput CreateInput(int version = 5) => new()
    {
        Spec = new Spec
        {
            Id = "spec-001",
            ProjectId = "proj-001",
            Title = "Test Spec",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview
        },
        Assignment = new Assignment
        {
            Id = "asg-001",
            SpecId = "spec-001",
            AgentRole = AgentRole.SpecValidator,
            Type = AssignmentType.SpecValidation
        },
        ProjectId = "proj-001",
        RunId = "run-001",
        CurrentVersion = version
    };

    [Fact]
    public void Parse_ValidJsonBlock_ReturnsSuccess()
    {
        var response = new CliResponse
        {
            ResponseText = """
                분석 결과입니다.

                ```json
                {
                  "proposedEvent": "specValidationPassed",
                  "summary": "모든 AC 충족",
                  "proposedReviewRequest": null
                }
                ```

                이상입니다.
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.SpecValidationPassed);
        output.Summary.Should().Be("모든 AC 충족");
        output.BaseVersion.Should().Be(5);
    }

    [Fact]
    public void Parse_JsonWithoutCodeFence_ExtractsFirstBraceBlock()
    {
        var response = new CliResponse
        {
            ResponseText = """
                결과: {"proposedEvent":"acPrecheckPassed","summary":"AC 적절"}
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.ProposedEvent.Should().Be(FlowEvent.AcPrecheckPassed);
    }

    [Fact]
    public void Parse_FailedResponse_ReturnsTerminalFailure()
    {
        var response = new CliResponse
        {
            ResponseText = string.Empty,
            Success = false,
            ErrorMessage = "exit code 1",
            StopReason = CliStopReason.Error
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.Result.Should().Be(AgentResult.TerminalFailure);
        output.Message.Should().Contain("exit code 1");
    }

    [Fact]
    public void Parse_TimeoutResponse_ReturnsRetryableFailure()
    {
        var response = new CliResponse
        {
            ResponseText = string.Empty,
            Success = false,
            ErrorMessage = "hard timeout",
            StopReason = CliStopReason.Timeout
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.Result.Should().Be(AgentResult.RetryableFailure);
    }

    [Fact]
    public void Parse_NoJsonInResponse_ReturnsNull()
    {
        var response = new CliResponse
        {
            ResponseText = "이것은 JSON이 없는 일반 텍스트입니다.",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().BeNull();
    }

    [Fact]
    public void Parse_BaseVersion_AlwaysFromInput()
    {
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput(version: 42));

        output.Should().NotBeNull();
        output!.BaseVersion.Should().Be(42);
    }

    [Fact]
    public void Parse_WithProposedReviewRequest_ParsesCorrectly()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "specValidationUserReviewRequested",
                  "summary": "사용자 확인 필요",
                  "proposedReviewRequest": {
                    "summary": "구현 방향 확인",
                    "questions": ["이 방향이 맞습니까?", "다른 접근이 필요합니까?"],
                    "options": [
                      { "id": "approve", "label": "승인", "description": "진행" },
                      { "id": "reject", "label": "반려", "description": "재작업" }
                    ]
                  }
                }
                ```
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.ProposedEvent.Should().Be(FlowEvent.SpecValidationUserReviewRequested);
        output.ProposedReviewRequest.Should().NotBeNull();
        output.ProposedReviewRequest!.Summary.Should().Be("구현 방향 확인");
        output.ProposedReviewRequest.Questions.Should().HaveCount(2);
        output.ProposedReviewRequest.Options.Should().HaveCount(2);
        output.ProposedReviewRequest.Options![0].Id.Should().Be("approve");
    }

    [Fact]
    public void Parse_InvalidEventName_ReturnsNull()
    {
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"nonExistentEvent","summary":"bad"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonBlock_CodeFence_ExtractsContent()
    {
        var text = "before\n```json\n{\"key\":\"val\"}\n```\nafter";

        var json = OutputParser.ExtractJsonBlock(text);

        json.Should().Be("{\"key\":\"val\"}");
    }

    [Fact]
    public void ExtractJsonBlock_NoBraces_ReturnsNull()
    {
        var json = OutputParser.ExtractJsonBlock("no json here");

        json.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEvidenceRefs_ParsesCorrectly()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "specValidationPassed",
                  "summary": "모든 AC 충족",
                  "evidenceRefs": [
                    { "kind": "test-result", "relativePath": "test-output.xml", "summary": "42 tests passed" },
                    { "kind": "coverage", "relativePath": "coverage.html" }
                  ]
                }
                ```
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.EvidenceRefs.Should().NotBeNull();
        output.EvidenceRefs.Should().HaveCount(2);
        output.EvidenceRefs![0].Kind.Should().Be("test-result");
        output.EvidenceRefs[0].RelativePath.Should().Be("test-output.xml");
        output.EvidenceRefs[0].Summary.Should().Be("42 tests passed");
        output.EvidenceRefs[1].Kind.Should().Be("coverage");
        output.EvidenceRefs[1].Summary.Should().BeNull();
    }

    [Fact]
    public void Parse_UserReviewRequested_WithoutProposedReviewRequest_ReturnsNull()
    {
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationUserReviewRequested","summary":"확인 필요"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        // proposedReviewRequest 없이 specValidationUserReviewRequested → null (RetryableFailure)
        output.Should().BeNull();
    }

    [Fact]
    public void Parse_WithoutEvidenceRefs_LeavesNull()
    {
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"acPrecheckPassed","summary":"ok"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.EvidenceRefs.Should().BeNull();
    }

    [Theory]
    [InlineData("", "빈 경로")]
    [InlineData("  ", "공백만")]
    [InlineData("/etc/passwd", "Unix 절대 경로")]
    [InlineData("C:/Windows/system32", "Windows 절대 경로 (C:)")]
    [InlineData("../../../secret.txt", "path traversal")]
    [InlineData("logs/../../../secret.txt", "중간 path traversal")]
    public void Parse_InvalidEvidencePaths_Filtered(string badPath, string description)
    {
        // JSON에 안전하게 삽입하기 위해 backslash를 escape
        var jsonPath = badPath.Replace("\\", "\\\\");
        var json = $@"{{""proposedEvent"":""specValidationPassed"",""summary"":""ok"",""evidenceRefs"":[{{""kind"":""log"",""relativePath"":""{jsonPath}"",""summary"":""{description}""}}]}}";
        var response = new CliResponse
        {
            ResponseText = json,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        // 유효하지 않은 경로의 ref는 필터링되어 null
        output!.EvidenceRefs.Should().BeNull($"'{badPath}' ({description}) should be filtered out");
    }

    [Fact]
    public void Parse_WindowsBackslashAbsolutePath_Filtered()
    {
        // Windows 절대 경로 (backslash)와 UNC 경로 테스트
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok","evidenceRefs":[{"kind":"log","relativePath":"C:\\Windows\\system32"}]}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());
        output.Should().NotBeNull();
        output!.EvidenceRefs.Should().BeNull();
    }

    [Fact]
    public void Parse_UncPath_Filtered()
    {
        var response = new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok","evidenceRefs":[{"kind":"log","relativePath":"\\\\server\\share"}]}""",
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());
        output.Should().NotBeNull();
        output!.EvidenceRefs.Should().BeNull();
    }

    [Fact]
    public void Parse_MixedValidAndInvalidPaths_KeepsOnlyValid()
    {
        var response = new CliResponse
        {
            ResponseText = """
                {"proposedEvent":"specValidationPassed","summary":"ok","evidenceRefs":[
                  {"kind":"test","relativePath":"test-output.xml","summary":"valid"},
                  {"kind":"hack","relativePath":"../../../etc/passwd"},
                  {"kind":"log","relativePath":"logs/build.log","summary":"also valid"},
                  {"kind":"bad","relativePath":""}
                ]}
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        };

        var output = Parser.Parse(response, CreateInput());

        output.Should().NotBeNull();
        output!.EvidenceRefs.Should().HaveCount(2);
        output.EvidenceRefs![0].RelativePath.Should().Be("test-output.xml");
        output.EvidenceRefs[1].RelativePath.Should().Be("logs/build.log");
    }
}
