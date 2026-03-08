using System.Text.Json;
using FluentAssertions;
using FlowCLI.Models;
using FlowCLI.Utils;

namespace FlowCLI.Tests;

/// <summary>
/// F-003 conditions: 공통 JSON 요청 envelope 역직렬화 및 필드 검증 테스트.
/// </summary>
public class FlowRequestTests
{
    // ─── F-003-C1: Envelope 역직렬화 ─────────────────────────────────────

    [Fact]
    public void C1_Deserialize_AllFields_MapsCorrectly()
    {
        var json = """
            {
                "command": "spec-create",
                "subcommand": "feature",
                "arguments": ["arg1"],
                "payload": { "title": "Test Feature", "status": "draft" },
                "options": { "pretty": true },
                "metadata": { "requestId": "req-001" }
            }
            """;

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        request.Should().NotBeNull();
        request!.Command.Should().Be("spec-create");
        request.Subcommand.Should().Be("feature");
        request.Arguments.Should().ContainSingle("arg1");
        request.Payload.Should().NotBeNull();
        request.Payload!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        request.Payload.Value.GetProperty("title").GetString().Should().Be("Test Feature");
        request.Options.Should().ContainKey("pretty");
        request.Metadata.Should().ContainKey("requestId");
    }

    [Fact]
    public void C1_Deserialize_MinimalRequest_OnlyCommandRequired()
    {
        var json = """{ "command": "build" }""";

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        request.Should().NotBeNull();
        request!.Command.Should().Be("build");
        request.Subcommand.Should().BeNull();
        request.Arguments.Should().BeNull();
        request.Payload.Should().BeNull();
        request.Options.Should().BeNull();
        request.Metadata.Should().BeNull();
    }

    [Fact]
    public void C1_Deserialize_PayloadWithArrayValue_ParsesCorrectly()
    {
        var json = """
            {
                "command": "db-add",
                "payload": { "tags": ["cli", "json"], "count": 2 }
            }
            """;

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        request!.Payload.Should().NotBeNull();
        var tags = request.Payload!.Value.GetProperty("tags");
        tags.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ─── F-003-C3: 필수 필드 누락 및 스키마 오류 검증 ────────────────────

    [Fact]
    public void C3_Deserialize_EmptyCommandField_CommandIsEmpty()
    {
        var json = """{ "command": "" }""";

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        // FlowRequest allows empty command; validation is in the dispatcher
        request!.Command.Should().BeEmpty();
    }

    [Fact]
    public void C3_Deserialize_MissingCommandField_CommandDefaultsToEmpty()
    {
        var json = """{ "subcommand": "e2e" }""";

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        request!.Command.Should().BeEmpty();
    }

    [Fact]
    public void C3_Deserialize_InvalidJson_ThrowsJsonException()
    {
        var invalidJson = "{ command: build }"; // missing quotes

        var act = () => JsonSerializer.Deserialize<FlowRequest>(invalidJson, JsonOutput.Read);

        act.Should().Throw<JsonException>();
    }

    // ─── payload/metadata 직렬화 라운드트립 ──────────────────────────────

    [Fact]
    public void RoundTrip_RequestWithPayloadAndMetadata_Symmetric()
    {
        var original = new FlowRequest
        {
            Command = "spec-create",
            Subcommand = "feature",
            Payload = JsonDocument.Parse("""{"title":"Feature X"}""").RootElement,
            Options = new Dictionary<string, JsonElement>
            {
                ["pretty"] = JsonDocument.Parse("true").RootElement
            },
            Metadata = new Dictionary<string, JsonElement>
            {
                ["requestId"] = JsonDocument.Parse("\"req-42\"").RootElement
            }
        };

        var serialized = JsonSerializer.Serialize(original, JsonOutput.Default);
        var deserialized = JsonSerializer.Deserialize<FlowRequest>(serialized, JsonOutput.Read);

        deserialized!.Command.Should().Be("spec-create");
        deserialized.Subcommand.Should().Be("feature");
        deserialized.Payload!.Value.GetProperty("title").GetString().Should().Be("Feature X");
        deserialized.Metadata.Should().ContainKey("requestId");
    }

    // ─── F-003-C2/C3: 디스패처 검증 (ValidateRequiredFields / ValidateOptionTypes) ──

    [Fact]
    public void C2_C3_ValidateRequiredFields_MissingKey_ReturnsError()
    {
        var opts = new Dictionary<string, JsonElement>
        {
            ["title"] = JsonDocument.Parse("\"Test\"").RootElement
        };

        var errors = FlowApp.ValidateRequiredFields(opts, "title", "content");

        errors.Should().ContainSingle(e => e.Contains("content"));
    }

    [Fact]
    public void C3_ValidateRequiredFields_AllPresent_ReturnsEmpty()
    {
        var opts = new Dictionary<string, JsonElement>
        {
            ["input-file"] = JsonDocument.Parse("\"/tmp/review.json\"").RootElement,
            ["id"] = JsonDocument.Parse("\"F-001\"").RootElement
        };

        var errors = FlowApp.ValidateRequiredFields(opts, "input-file", "id");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void C3_ValidateOptionTypes_TypeMismatch_ReturnsError()
    {
        var opts = new Dictionary<string, JsonElement>
        {
            ["timeout"] = JsonDocument.Parse("\"not-a-number\"").RootElement
        };

        var errors = FlowApp.ValidateOptionTypes(opts, new Dictionary<string, Type>
        {
            ["timeout"] = typeof(int)
        });

        errors.Should().ContainSingle(e => e.Contains("timeout"));
    }

    [Fact]
    public void C1_Deserialize_SampleRunnerPayload_ParsesCorrectly()
    {
        // 사용자 제공 샘플 JSON 페이로드
        var json = """
            {
                "command": "spec",
                "subcommand": "list",
                "arguments": [],
                "options": { "status": "queued", "json": true },
                "payload": {},
                "metadata": { "source": "runner" }
            }
            """;

        var request = JsonSerializer.Deserialize<FlowRequest>(json, JsonOutput.Read);

        request.Should().NotBeNull();
        request!.Command.Should().Be("spec");
        request.Subcommand.Should().Be("list");
        request.Arguments.Should().BeEmpty();
        request.Options.Should().ContainKey("status");
        request.Metadata.Should().ContainKey("source");
    }
}
