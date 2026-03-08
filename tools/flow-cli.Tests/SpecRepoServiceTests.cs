using System.Text.Json.Nodes;
using FlowCLI.Services.Runner;
using FluentAssertions;

namespace FlowCLI.Tests;

public class SpecRepoServiceTests
{
    [Fact]
    public void MergeSpecJson_RestoresLocalStatus_WhenRemoteDidNotChangeStatus()
    {
        var baseJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {}
        }
        """;

        var localJson = """
        {
          "id": "F-081",
          "status": "working",
          "metadata": {}
        }
        """;

        var currentJson = """
        {
          "id": "F-081",
          "status": "queued",
          "title": "remote update",
          "metadata": {}
        }
        """;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        result.Changed.Should().BeTrue();
        result.RestoredPathCount.Should().Be(1);
        var merged = JsonNode.Parse(result.MergedJson)!.AsObject();
        merged["status"]!.GetValue<string>().Should().Be("working");
        merged["title"]!.GetValue<string>().Should().Be("remote update");
    }

    [Fact]
    public void MergeSpecJson_KeepsRemoteStatus_WhenBothSidesChangedStatus()
    {
        var baseJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {}
        }
        """;

        var localJson = """
        {
          "id": "F-081",
          "status": "working",
          "metadata": {}
        }
        """;

        var currentJson = """
        {
          "id": "F-081",
          "status": "needs-review",
          "metadata": {}
        }
        """;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        result.Changed.Should().BeFalse();
        result.RestoredPathCount.Should().Be(0);
        JsonNode.Parse(result.MergedJson)!["status"]!.GetValue<string>().Should().Be("needs-review");
    }

    [Fact]
    public void MergeSpecJson_RestoresSafeMetadataFields_WithoutOverwritingRemoteChanges()
    {
        // userPriorityHint: 로컬이 변경(medium→high), 원격이 변경하지 않음 → 로컬 값 복구
        // lastError: 로컬이 변경(없음→some-error), 원격이 변경하지 않음 → 로컬 값 복구
        var baseJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "userPriorityHint": "medium"
          }
        }
        """;

        var localJson = """
        {
          "id": "F-081",
          "status": "needs-review",
          "metadata": {
            "userPriorityHint": "high",
            "lastError": "some-error"
          }
        }
        """;

        var currentJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "userPriorityHint": "medium",
            "description": "remote update"
          }
        }
        """;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        // status와 userPriorityHint, lastError 총 3개 복구
        result.Changed.Should().BeTrue();
        result.RestoredPathCount.Should().Be(3);
        var merged = JsonNode.Parse(result.MergedJson)!;
        merged["status"]!.GetValue<string>().Should().Be("needs-review");
        var metadata = merged["metadata"]!.AsObject();
        metadata["userPriorityHint"]!.GetValue<string>().Should().Be("high");
        metadata["lastError"]!.GetValue<string>().Should().Be("some-error");
        // 원격 업데이트는 유지
        metadata["description"]!.GetValue<string>().Should().Be("remote update");
    }

    [Fact]
    public void MergeSpecJson_RemovesSafeMetadataField_WhenLocalDeletedItAndRemoteDidNotTouchIt()
    {
        var baseJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "userPriorityHint": "high"
          }
        }
        """;

        var localJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {}
        }
        """;

        var currentJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "userPriorityHint": "high"
          }
        }
        """;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        result.Changed.Should().BeTrue();
        var metadata = JsonNode.Parse(result.MergedJson)!["metadata"]!.AsObject();
        metadata.ContainsKey("userPriorityHint").Should().BeFalse();
    }

    /// <summary>
    /// 사용자가 질문에 답변 후 git pull 시 답변이 보존되는지 검증.
    /// feedbackStore가 로컬에서 questions 배열을 수정했는데 원격이 변경하지 않은 경우
    /// 로컬 답변이 복구되어야 한다.
    /// </summary>
    [Fact]
    public void MergeSpecJson_PreservesUserAnswers_WhenRemoteDidNotChangeQuestions()
    {
        var baseJson = """
        {
          "id": "F-003",
          "status": "needs-review",
          "metadata": {
            "reviewDisposition": "needs-user-decision",
            "plannerState": "waiting-user-input",
            "questionStatus": "waiting-user-input",
            "questions": [
              { "id": "q1", "type": "user-decision", "question": "A와 B 중 어느 쪽?", "status": "open" }
            ]
          }
        }
        """;

        // 사용자가 답변 저장: questions 배열 answered로 변경, 관련 플래그 리셋
        var localJson = """
        {
          "id": "F-003",
          "status": "needs-review",
          "metadata": {
            "reviewDisposition": "retry-queued",
            "plannerState": "standby",
            "questions": [
              { "id": "q1", "type": "user-decision", "question": "A와 B 중 어느 쪽?", "status": "answered", "answer": "A를 우선합니다" }
            ],
            "lastAnsweredAt": "2026-03-08T10:00:00Z"
          }
        }
        """;

        // 원격은 base와 동일 (runner가 아직 pull하지 않음)
        var currentJson = baseJson;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        result.Changed.Should().BeTrue();
        var merged = JsonNode.Parse(result.MergedJson)!;
        var metadata = merged["metadata"]!.AsObject();

        // 답변 보존
        var questions = metadata["questions"]!.AsArray();
        questions.Should().HaveCount(1);
        questions[0]!["status"]!.GetValue<string>().Should().Be("answered");
        questions[0]!["answer"]!.GetValue<string>().Should().Be("A를 우선합니다");

        // 관련 상태 보존
        metadata["reviewDisposition"]!.GetValue<string>().Should().Be("retry-queued");
        metadata["plannerState"]!.GetValue<string>().Should().Be("standby");
        metadata["lastAnsweredAt"]!.GetValue<string>().Should().Be("2026-03-08T10:00:00Z");

        // questionStatus는 로컬에서 제거됨 → 복구 시 제거 유지
        metadata.ContainsKey("questionStatus").Should().BeFalse();
    }
}
