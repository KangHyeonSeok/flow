using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCore.Serialization;

/// <summary>flow-core 공통 JSON 직렬화 옵션</summary>
public static class FlowJsonOptions
{
    public static readonly JsonSerializerOptions Default = CreateOptions(writeIndented: true);
    public static readonly JsonSerializerOptions Compact = CreateOptions(writeIndented: false);

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
