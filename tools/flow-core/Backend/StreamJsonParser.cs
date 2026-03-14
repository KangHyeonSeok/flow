using System.Text;
using System.Text.Json;

namespace FlowCore.Backend;

/// <summary>Claude CLI stream-json 출력을 CliResponse로 파싱</summary>
public static class StreamJsonParser
{
    /// <summary>
    /// stream-json 원시 출력을 파싱하여 CliResponse를 반환한다.
    /// type:"result" 이벤트의 result 필드를 ResponseText로 사용.
    /// result 이벤트가 없으면 content_block_delta 텍스트를 fallback으로 누적.
    /// </summary>
    public static CliResponse Parse(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "empty output from backend",
                StopReason = CliStopReason.Error
            };
        }

        string? resultText = null;
        var deltaBuilder = new StringBuilder();

        foreach (var line in rawOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '{')
                continue;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp))
                    continue;

                var type = typeProp.GetString();

                if (type == "result")
                {
                    if (root.TryGetProperty("result", out var resultProp))
                        resultText = resultProp.GetString();
                }
                else if (type == "content_block_delta")
                {
                    if (root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var text))
                    {
                        deltaBuilder.Append(text.GetString());
                    }
                }
            }
            catch (JsonException)
            {
                // malformed line — skip
            }
        }

        if (resultText != null)
        {
            return new CliResponse
            {
                ResponseText = resultText,
                Success = true,
                StopReason = CliStopReason.Completed
            };
        }

        var fallback = deltaBuilder.ToString();
        if (fallback.Length > 0)
        {
            return new CliResponse
            {
                ResponseText = fallback,
                Success = true,
                StopReason = CliStopReason.Completed
            };
        }

        return new CliResponse
        {
            ResponseText = string.Empty,
            Success = false,
            ErrorMessage = "no result or content found in stream-json output",
            StopReason = CliStopReason.Error
        };
    }
}
