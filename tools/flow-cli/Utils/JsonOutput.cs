using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Models;

namespace FlowCLI.Utils;

public static class JsonOutput
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Pretty = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Read = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(CommandResult result, bool pretty = false)
    {
        var options = pretty ? Pretty : Default;
        Console.WriteLine(JsonSerializer.Serialize(result, options));
    }

    /// <summary>F-005-C1: 성공 응답에 공통 envelope (success, command, data, message, metadata) 포함.</summary>
    public static CommandResult Success(string command, object? data = null, string? message = null)
        => new()
        {
            Success = true,
            Command = command,
            Data = data,
            Message = message,
            Metadata = new { timestamp = DateTime.UtcNow.ToString("o") }
        };

    /// <summary>F-005-C2: 실패 응답에 error.code, error.message, error.details, exitCode 일관 적용.</summary>
    public static CommandResult Error(string command, string error, object? details = null, string code = ErrorCodes.ExecutionError)
        => new()
        {
            Success = false,
            Command = command,
            Error = new ErrorInfo { Code = code, Message = error, Details = details },
            ExitCode = 1
        };

    /// <summary>F-005-C2: validation 오류 전용 헬퍼 (VALIDATION_ERROR 코드 사용).</summary>
    public static CommandResult ValidationError(string command, string error, object? details = null)
        => Error(command, error, details, ErrorCodes.ValidationError);
}

/// <summary>F-005-C2: 표준 오류 코드 상수 정의.</summary>
public static class ErrorCodes
{
    public const string ExecutionError = "EXECUTION_ERROR";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string SchemaError = "SCHEMA_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
}
