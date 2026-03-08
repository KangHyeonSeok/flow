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
        var baseJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "selectionReason": {
              "rank": 1
            },
            "userPriorityHint": "medium"
          }
        }
        """;

        var localJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "selectionReason": {
              "rank": 1,
              "selectedAt": "2026-03-08T00:00:00Z"
            },
            "userPriorityHint": "high"
          }
        }
        """;

        var currentJson = """
        {
          "id": "F-081",
          "status": "queued",
          "metadata": {
            "selectionReason": {
              "rank": 1
            },
            "userPriorityHint": "low",
            "runnerStartedAt": "2026-03-08T01:00:00Z"
          }
        }
        """;

        var result = SpecRepoService.MergeSpecJson(baseJson, localJson, currentJson);

        result.Changed.Should().BeTrue();
        result.RestoredPathCount.Should().Be(1);
        var metadata = JsonNode.Parse(result.MergedJson)!["metadata"]!.AsObject();
        metadata["selectionReason"]!["selectedAt"]!.GetValue<string>().Should().Be("2026-03-08T00:00:00Z");
        metadata["userPriorityHint"]!.GetValue<string>().Should().Be("low");
        metadata["runnerStartedAt"]!.GetValue<string>().Should().Be("2026-03-08T01:00:00Z");
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
}