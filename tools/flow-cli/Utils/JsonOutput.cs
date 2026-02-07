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

    public static CommandResult Success(string command, object? data = null, string? message = null)
        => new() { Success = true, Command = command, Data = data, Message = message };

    public static CommandResult Error(string command, string error, object? data = null)
        => new() { Success = false, Command = command, Error = error, Data = data };
}
