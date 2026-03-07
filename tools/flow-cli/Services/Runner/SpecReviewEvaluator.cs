using System.Text.Json;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

internal sealed class ManualVerificationItem
{
    public string Source { get; init; } = "";
    public string Label { get; init; } = "";
    public string? ConditionId { get; init; }
    public string? Reason { get; init; }
}

internal sealed class SpecReviewEvaluation
{
    public int TotalConditions { get; init; }
    public int VerifiedConditions { get; init; }
    public IReadOnlyList<ManualVerificationItem> ManualVerificationItems { get; init; } = Array.Empty<ManualVerificationItem>();

    public bool HasConditions => TotalConditions > 0;
    public bool AllConditionsVerified => HasConditions && VerifiedConditions == TotalConditions;
    public bool RequiresManualVerification => ManualVerificationItems.Count > 0;
    public bool CanAutoVerify => AllConditionsVerified && !RequiresManualVerification;
}

internal static class SpecReviewEvaluator
{
    public static SpecReviewEvaluation Evaluate(SpecNode spec)
    {
        var items = new List<ManualVerificationItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AppendManualVerificationItems(spec.Metadata, items, seen, "spec", spec.Id, null);

        foreach (var condition in spec.Conditions)
        {
            AppendManualVerificationItems(
                condition.Metadata,
                items,
                seen,
                "condition",
                condition.Id,
                condition.Id);
        }

        return new SpecReviewEvaluation
        {
            TotalConditions = spec.Conditions.Count,
            VerifiedConditions = spec.Conditions.Count(c => string.Equals(c.Status, "verified", StringComparison.OrdinalIgnoreCase)),
            ManualVerificationItems = items
        };
    }

    private static void AppendManualVerificationItems(
        Dictionary<string, object>? metadata,
        List<ManualVerificationItem> destination,
        HashSet<string> seen,
        string source,
        string defaultLabel,
        string? conditionId)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return;
        }

        var requiresManualVerification = TryGetBoolean(metadata, "requiresManualVerification", out var required) && required;
        var reason = TryGetString(metadata, "manualVerificationReason", out var text) ? text : null;
        var explicitItems = TryGetManualVerificationItems(metadata, source, conditionId);

        if (explicitItems.Count > 0)
        {
            requiresManualVerification = true;
        }

        if (!requiresManualVerification)
        {
            return;
        }

        if (explicitItems.Count == 0)
        {
            AddUnique(destination, seen, new ManualVerificationItem
            {
                Source = source,
                Label = defaultLabel,
                ConditionId = conditionId,
                Reason = reason
            });
            return;
        }

        foreach (var item in explicitItems)
        {
            AddUnique(destination, seen, new ManualVerificationItem
            {
                Source = item.Source,
                Label = item.Label,
                ConditionId = item.ConditionId,
                Reason = item.Reason ?? reason
            });
        }
    }

    private static void AddUnique(
        List<ManualVerificationItem> destination,
        HashSet<string> seen,
        ManualVerificationItem item)
    {
        var key = $"{item.Source}|{item.ConditionId}|{item.Label}|{item.Reason}";
        if (seen.Add(key))
        {
            destination.Add(item);
        }
    }

    private static List<ManualVerificationItem> TryGetManualVerificationItems(
        Dictionary<string, object> metadata,
        string source,
        string? conditionId)
    {
        if (!TryGetValue(metadata, "manualVerificationItems", out var rawItems) || rawItems == null)
        {
            return new List<ManualVerificationItem>();
        }

        return ParseManualVerificationItems(rawItems, source, conditionId);
    }

    private static List<ManualVerificationItem> ParseManualVerificationItems(object rawItems, string source, string? conditionId)
    {
        var result = new List<ManualVerificationItem>();

        if (rawItems is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in element.EnumerateArray())
            {
                var parsed = ParseManualVerificationItem(item, source, conditionId);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }

            return result;
        }

        if (rawItems is IEnumerable<object> enumerable)
        {
            foreach (var item in enumerable)
            {
                var parsed = ParseManualVerificationItem(item, source, conditionId);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }
        }

        return result;
    }

    private static ManualVerificationItem? ParseManualVerificationItem(object rawItem, string source, string? conditionId)
    {
        if (TryConvertToString(rawItem, out var label))
        {
            return new ManualVerificationItem
            {
                Source = source,
                Label = label,
                ConditionId = conditionId
            };
        }

        if (!TryGetString(rawItem, "label", out label) &&
            !TryGetString(rawItem, "title", out label))
        {
            return null;
        }

        TryGetString(rawItem, "reason", out var reason);

        return new ManualVerificationItem
        {
            Source = source,
            Label = label,
            ConditionId = conditionId,
            Reason = reason
        };
    }

    private static bool TryGetBoolean(Dictionary<string, object> metadata, string key, out bool value)
    {
        value = false;
        return TryGetValue(metadata, key, out var rawValue) && rawValue != null && TryConvertToBoolean(rawValue, out value);
    }

    private static bool TryGetString(Dictionary<string, object> metadata, string key, out string value)
    {
        value = "";
        return TryGetValue(metadata, key, out var rawValue) && rawValue != null && TryConvertToString(rawValue, out value);
    }

    private static bool TryGetString(object source, string key, out string value)
    {
        value = "";
        return TryGetProperty(source, key, out var rawValue) && rawValue != null && TryConvertToString(rawValue, out value);
    }

    private static bool TryGetValue(Dictionary<string, object> metadata, string key, out object? value)
    {
        foreach (var entry in metadata)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetProperty(object source, string key, out object? value)
    {
        switch (source)
        {
            case Dictionary<string, object> dictionary:
                return TryGetValue(dictionary, key, out value);
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
                break;
        }

        value = null;
        return false;
    }

    private static bool TryConvertToBoolean(object rawValue, out bool value)
    {
        switch (rawValue)
        {
            case bool boolean:
                value = boolean;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return bool.TryParse(element.GetString(), out value);
            case string text:
                return bool.TryParse(text, out value);
            default:
                value = false;
                return false;
        }
    }

    private static bool TryConvertToString(object rawValue, out string value)
    {
        switch (rawValue)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                value = element.GetString() ?? "";
                return !string.IsNullOrWhiteSpace(value);
            case JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } element:
                value = element.ToString();
                return !string.IsNullOrWhiteSpace(value);
            case JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array }:
                value = "";
                return false;
            default:
                value = rawValue.ToString() ?? "";
                return !string.IsNullOrWhiteSpace(value);
        }
    }
}