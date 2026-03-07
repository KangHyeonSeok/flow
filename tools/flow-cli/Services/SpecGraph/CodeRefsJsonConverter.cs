using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// codeRefs 필드의 하위 호환을 위해 문자열 배열과 객체 배열을 모두 허용한다.
/// 객체 형식은 최소 { "path": "..." } 를 지원하며 description 등 추가 필드는 무시한다.
/// </summary>
public class CodeRefsJsonConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new List<string>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("codeRefs는 배열이어야 합니다.");

        var result = new List<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return result;

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
                continue;
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var document = JsonDocument.ParseValue(ref reader);
                if (!document.RootElement.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                    throw new JsonException("codeRefs 객체는 문자열 path 필드를 포함해야 합니다.");

                var path = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                    result.Add(path!);
                continue;
            }

            throw new JsonException($"지원하지 않는 codeRefs 항목 타입: {reader.TokenType}");
        }

        throw new JsonException("codeRefs 배열이 올바르게 종료되지 않았습니다.");
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
            writer.WriteStringValue(item);

        writer.WriteEndArray();
    }
}