using System.Text.Json;
using FlowCore.Backend;
using FluentAssertions;

namespace FlowCore.Tests;

/// <summary>
/// CopilotAcpBackend 테스트.
/// 실제 copilot 프로세스 없이 ACP 프로토콜 계약을 검증한다.
/// </summary>
public class CopilotAcpBackendTests
{
    [Fact]
    public void BackendId_IsCopilotAcp()
    {
        var backend = new CopilotAcpBackend();
        backend.BackendId.Should().Be("copilot-acp");
    }

    [Fact]
    public async Task RunPromptAsync_WhenProcessNotAvailable_ReturnsError()
    {
        // copilot 바이너리가 없으면 프로세스 시작 실패 → 에러 응답
        var backend = new CopilotAcpBackend(command: "nonexistent-copilot-binary-12345");
        var options = new CliBackendOptions
        {
            HardTimeout = TimeSpan.FromSeconds(5)
        };

        var response = await backend.RunPromptAsync("test prompt", options);

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Error);
        response.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunPromptAsync_CancellationToken_ReturnsCancelled()
    {
        var backend = new CopilotAcpBackend(command: "nonexistent-copilot-binary-12345");
        var options = new CliBackendOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await backend.RunPromptAsync("test", options, cts.Token);

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Cancelled);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var backend = new CopilotAcpBackend();
        await backend.DisposeAsync();
        // 두번째 호출도 예외 없이 완료
        await backend.DisposeAsync();
    }
}

/// <summary>ACP 프로토콜 메시지 직렬화 테스트</summary>
public class AcpProtocolSerializationTests
{
    [Fact]
    public void AcpRequest_SerializesCorrectly()
    {
        var request = new AcpRequest
        {
            Id = 1,
            Method = "initialize",
            Params = new InitializeParams()
        };

        var json = JsonSerializer.Serialize(request, AcpJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt32().Should().Be(1);
        root.GetProperty("method").GetString().Should().Be("initialize");
        root.GetProperty("params").GetProperty("protocolVersion").GetString().Should().Be("0.1");
        root.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString().Should().Be("flow-core");
    }

    [Fact]
    public void SessionNewParams_SerializesWithCwd()
    {
        var param = new SessionNewParams { Cwd = "/tmp/worktree" };
        var json = JsonSerializer.Serialize(param, AcpJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("cwd").GetString().Should().Be("/tmp/worktree");
    }

    [Fact]
    public void SessionPromptParams_SerializesCorrectly()
    {
        var param = new SessionPromptParams
        {
            SessionId = "sess-123",
            Prompt = "Implement feature X"
        };
        var json = JsonSerializer.Serialize(param, AcpJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("sessionId").GetString().Should().Be("sess-123");
        doc.RootElement.GetProperty("prompt").GetString().Should().Be("Implement feature X");
    }

    [Fact]
    public void SessionSetModeParams_SerializesCorrectly()
    {
        var param = new SessionSetModeParams { SessionId = "sess-123", Mode = "code" };
        var json = JsonSerializer.Serialize(param, AcpJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("mode").GetString().Should().Be("code");
    }

    [Fact]
    public void PermissionResponse_AllowAlways()
    {
        var resp = new PermissionResponse { Action = "allow_always" };
        var json = JsonSerializer.Serialize(resp, AcpJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("action").GetString().Should().Be("allow_always");
    }

    [Fact]
    public void AcpResponse_DeserializesResult()
    {
        var json = """{"jsonrpc":"2.0","id":1,"result":{"sessionId":"sess-abc"}}""";
        var response = JsonSerializer.Deserialize<AcpResponse<SessionNewResult>>(json, AcpJsonOptions.Default);

        response.Should().NotBeNull();
        response!.Id.Should().Be(1);
        response.Result.Should().NotBeNull();
        response.Result!.SessionId.Should().Be("sess-abc");
    }

    [Fact]
    public void AcpResponse_DeserializesError()
    {
        var json = """{"jsonrpc":"2.0","id":2,"error":{"code":-32600,"message":"Invalid Request"}}""";
        var response = JsonSerializer.Deserialize<AcpResponse<object>>(json, AcpJsonOptions.Default);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32600);
        response.Error.Message.Should().Be("Invalid Request");
    }

    [Fact]
    public void AcpResponse_Notification_HasMethodButNoId()
    {
        var json = """{"jsonrpc":"2.0","method":"session/update","params":{"message":"working..."}}""";
        var response = JsonSerializer.Deserialize<AcpResponse<object>>(json, AcpJsonOptions.Default);

        response.Should().NotBeNull();
        response!.IsNotification.Should().BeTrue();
        response.Method.Should().Be("session/update");
    }

    [Fact]
    public void AcpResponse_RequestPermission_HasIdAndMethod()
    {
        var json = """{"jsonrpc":"2.0","id":5,"method":"request_permission","params":{"tool":"write_file","description":"Write to main.cs"}}""";
        var response = JsonSerializer.Deserialize<AcpResponse<object>>(json, AcpJsonOptions.Default);

        response.Should().NotBeNull();
        response!.Id.Should().Be(5);
        response.Method.Should().Be("request_permission");
        response.IsNotification.Should().BeFalse();
    }

    [Fact]
    public void SessionUpdateParams_DeserializesStopReason()
    {
        var json = """{"sessionId":"sess-1","stopReason":"end_turn","responseText":"Done!"}""";
        var update = JsonSerializer.Deserialize<SessionUpdateParams>(json, AcpJsonOptions.Default);

        update.Should().NotBeNull();
        update!.StopReason.Should().Be("end_turn");
        update.ResponseText.Should().Be("Done!");
    }
}
