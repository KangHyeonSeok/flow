using System.Text.Json;
using System.Text.Json.Nodes;
using FlowCore.Models;

namespace FlowCore.Serialization;

/// <summary>Spec 저장 시 빈 필드를 제거하는 pruning 로직</summary>
public static class SpecPruner
{
    /// <summary>Spec을 JSON 문자열로 직렬화하고 빈 필드를 제거한다.</summary>
    public static string Serialize(Spec spec)
    {
        var json = JsonSerializer.Serialize(spec, FlowJsonOptions.Default);
        var node = JsonNode.Parse(json)!.AsObject();

        PruneEmptyArray(node, "assignments");
        PruneEmptyArray(node, "reviewRequestIds");
        PruneEmptyArray(node, "testIds");

        // dependencies: dependsOn, blocks 모두 빈 배열이면 dependencies 자체 제거
        if (node["dependencies"] is JsonObject deps)
        {
            PruneEmptyArray(deps, "dependsOn");
            PruneEmptyArray(deps, "blocks");
            if (deps.Count == 0)
                node.Remove("dependencies");
        }

        // retryCounters: 모든 int 값이 0이고 다른 필드가 없으면 제거
        if (node["retryCounters"] is JsonObject counters)
        {
            bool allDefault = true;
            foreach (var kvp in counters)
            {
                var val = kvp.Value;
                if (val is null) continue;
                if (val.GetValueKind() == JsonValueKind.Number)
                {
                    if (val.GetValue<int>() != 0) { allDefault = false; break; }
                }
                else
                {
                    // retryNotBefore 등 non-null 값이 있으면 유지
                    allDefault = false; break;
                }
            }
            if (allDefault)
                node.Remove("retryCounters");
        }

        return node.ToJsonString(FlowJsonOptions.Default);
    }

    /// <summary>JSON 문자열을 Spec으로 역직렬화하고 빈 필드에 기본값을 채운다.</summary>
    public static Spec Deserialize(string json)
    {
        var spec = JsonSerializer.Deserialize<Spec>(json, FlowJsonOptions.Default)
            ?? throw new JsonException("Failed to deserialize Spec");
        return spec;
    }

    private static void PruneEmptyArray(JsonObject obj, string key)
    {
        if (obj[key] is JsonArray arr && arr.Count == 0)
            obj.Remove(key);
    }
}
