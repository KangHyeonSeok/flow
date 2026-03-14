using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCore.Backend;

/// <summary>ACP JSON-RPC 2.0 요청</summary>
internal sealed class AcpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }
}

/// <summary>ACP JSON-RPC 2.0 응답 (제네릭)</summary>
internal sealed class AcpResponse<T>
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error")]
    public AcpError? Error { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    public bool IsNotification => Id == null && Method != null;
}

/// <summary>ACP JSON-RPC 원시 메시지 (JsonElement 기반, 메시지 루프용)</summary>
internal sealed class AcpRawMessage
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public AcpError? Error { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    public bool IsNotification => Id == null && Method != null;
}

/// <summary>ACP 오류</summary>
internal sealed class AcpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// ── initialize ──

internal sealed class InitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion => "0.1";

    [JsonPropertyName("capabilities")]
    public object Capabilities => new { };

    [JsonPropertyName("clientInfo")]
    public ClientInfo ClientInfo => new();
}

internal sealed class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name => "flow-core";

    [JsonPropertyName("version")]
    public string Version => "1.0.0";
}

internal sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string? ProtocolVersion { get; set; }

    [JsonPropertyName("capabilities")]
    public JsonElement? Capabilities { get; set; }

    [JsonPropertyName("serverInfo")]
    public JsonElement? ServerInfo { get; set; }
}

// ── session/new ──

internal sealed class SessionNewParams
{
    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }
}

internal sealed class SessionNewResult
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
}

// ── session/set_mode ──

internal sealed class SessionSetModeParams
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
}

// ── session/prompt ──

internal sealed class SessionPromptParams
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed class SessionPromptResult
{
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("responseText")]
    public string? ResponseText { get; set; }
}

// ── session/update (notification) ──

internal sealed class SessionUpdateParams
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stopReason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("responseText")]
    public string? ResponseText { get; set; }
}

// ── request_permission ──

internal sealed class RequestPermissionParams
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class PermissionResponse
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

// ── session/cancel ──

internal sealed class SessionCancelParams
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }
}

/// <summary>ACP JSON 직렬화 옵션</summary>
internal static class AcpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
