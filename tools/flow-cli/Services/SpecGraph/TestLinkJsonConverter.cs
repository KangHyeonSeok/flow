using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// tests 필드 역호환 컨버터.
/// 구형: 문자열 배열 ["path/to/test"] → TestId에 경로를 넣은 TestLink 목록으로 변환.
/// 신형: 객체 배열 [{ "testId": "...", ... }] → 그대로 역직렬화.
/// </summary>
public class TestLinkJsonConverter : JsonConverter<List<TestLink>>
{
    public override List<TestLink> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new List<TestLink>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("tests는 배열이어야 합니다.");

        var result = new List<TestLink>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return result;

            // 구형: 문자열 → TestId에 경로 저장
            if (reader.TokenType == JsonTokenType.String)
            {
                var path = reader.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                    result.Add(new TestLink { TestId = path!, Name = path! });
                continue;
            }

            // 신형: 객체 → TestLink 역직렬화
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var innerOptions = new JsonSerializerOptions(options);
                innerOptions.Converters.Remove(this); // 재귀 방지
                var link = JsonSerializer.Deserialize<TestLink>(ref reader, innerOptions);
                if (link != null)
                    result.Add(link);
                continue;
            }

            throw new JsonException($"지원하지 않는 tests 항목 타입: {reader.TokenType}");
        }

        throw new JsonException("tests 배열이 올바르게 종료되지 않았습니다.");
    }

    public override void Write(Utf8JsonWriter writer, List<TestLink> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var link in value)
            JsonSerializer.Serialize(writer, link, options);
        writer.WriteEndArray();
    }
}
