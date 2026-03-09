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
    public int TotalCodeRefs { get; init; }
    public int DescriptionLength { get; init; }
    public IReadOnlyList<ManualVerificationItem> ManualVerificationItems { get; init; } = Array.Empty<ManualVerificationItem>();

    public bool HasConditions => TotalConditions > 0;
    public bool AllConditionsVerified => HasConditions && VerifiedConditions == TotalConditions;
    public bool RequiresManualVerification => ManualVerificationItems.Count > 0;
    public bool CanAutoVerify => AllConditionsVerified && !RequiresManualVerification;
}

internal sealed class SpecReviewDecision
{
    public string SpecStatus { get; init; } = "needs-review";
    public string ReviewDisposition { get; init; } = "missing-evidence";
    public string? ReviewReason { get; init; }
    public bool HasOpenQuestions { get; init; }
    public bool HasFailedChecks { get; init; }
    public bool RequiresManualVerification { get; init; }
    public bool IsFinal => string.Equals(SpecStatus, "verified", StringComparison.OrdinalIgnoreCase)
        || string.Equals(SpecStatus, "done", StringComparison.OrdinalIgnoreCase);
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
            TotalCodeRefs = CountCodeRefs(spec),
            DescriptionLength = spec.Description?.Length ?? 0,
            ManualVerificationItems = items
        };
    }

    public static SpecReviewDecision ResolveDecision(SpecNode spec, bool hasOpenQuestions)
    {
        var evaluation = Evaluate(spec);

        if (hasOpenQuestions)
        {
            return new SpecReviewDecision
            {
                SpecStatus = "needs-review",
                ReviewDisposition = "open-question",
                ReviewReason = "open-question",
                HasOpenQuestions = true,
                HasFailedChecks = HasFailedChecks(spec),
                RequiresManualVerification = evaluation.RequiresManualVerification
            };
        }

        var hasFailedManualVerification = HasFailedManualVerification(spec);
        var hasFailedChecks = hasFailedManualVerification || HasFailedChecks(spec);
        if (hasFailedChecks)
        {
            return new SpecReviewDecision
            {
                SpecStatus = "queued",
                ReviewDisposition = "test-failed",
                ReviewReason = "test-failed",
                HasFailedChecks = true,
                RequiresManualVerification = evaluation.RequiresManualVerification
            };
        }

        if (evaluation.RequiresManualVerification)
        {
            return new SpecReviewDecision
            {
                SpecStatus = "needs-review",
                ReviewDisposition = "user-test-required",
                ReviewReason = "user-test-required",
                RequiresManualVerification = true
            };
        }

        if (string.Equals(spec.NodeType, "task", StringComparison.OrdinalIgnoreCase))
        {
            if (!evaluation.HasConditions || evaluation.AllConditionsVerified)
            {
                return new SpecReviewDecision
                {
                    SpecStatus = "done",
                    ReviewDisposition = "review-done"
                };
            }

            return new SpecReviewDecision
            {
                SpecStatus = "queued",
                ReviewDisposition = "missing-evidence",
                ReviewReason = "missing-evidence"
            };
        }

        if (evaluation.AllConditionsVerified)
        {
            return new SpecReviewDecision
            {
                SpecStatus = "verified",
                ReviewDisposition = "review-verified"
            };
        }

        return new SpecReviewDecision
        {
            SpecStatus = "queued",
            ReviewDisposition = "missing-evidence",
            ReviewReason = "missing-evidence"
        };
    }

    public static void NormalizeConditionReviewStates(SpecNode spec, bool hasOpenQuestions)
    {
        foreach (var condition in spec.Conditions)
        {
            if (string.Equals(condition.Status, "verified", StringComparison.OrdinalIgnoreCase))
            {
                condition.Metadata?.Remove("reviewReason");
                continue;
            }

            condition.Metadata ??= new Dictionary<string, object>();

            var hasFailedManualVerification = TryGetValue(condition.Metadata, "manualVerificationStatus", out var manualStatus)
                && string.Equals(manualStatus?.ToString(), "failed", StringComparison.OrdinalIgnoreCase);

            if (hasFailedManualVerification || ConditionHasFailedChecks(condition))
            {
                condition.Status = "needs-review";
                condition.Metadata["reviewReason"] = "test-failed";
                if (hasFailedManualVerification)
                {
                    condition.Metadata.Remove("requiresManualVerification");
                    condition.Metadata.Remove("manualVerificationReason");
                    condition.Metadata.Remove("manualVerificationItems");
                }
                continue;
            }

            if (HasManualVerificationRequirement(condition.Metadata))
            {
                condition.Status = "needs-review";
                condition.Metadata["reviewReason"] = "user-test-required";
                continue;
            }

            condition.Status = "needs-review";
            condition.Metadata["reviewReason"] = hasOpenQuestions ? "open-question" : "missing-evidence";
        }
    }

    public static bool HasOpenQuestions(SpecNode spec)
        => CountOpenQuestions(spec) > 0;

    public static int CountOpenQuestions(SpecNode spec)
    {
        if (spec.Metadata == null || !TryGetValue(spec.Metadata, "questions", out var rawQuestions) || rawQuestions == null)
        {
            return 0;
        }

        if (rawQuestions is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            return element.EnumerateArray().Count(question =>
                question.ValueKind == JsonValueKind.Object
                && question.TryGetProperty("status", out var status)
                && string.Equals(status.GetString(), "open", StringComparison.OrdinalIgnoreCase));
        }

        if (rawQuestions is IEnumerable<object> questions)
        {
            return questions.Count(question =>
            {
                if (question is Dictionary<string, object> dict
                    && TryGetValue(dict, "status", out var statusValue))
                {
                    return string.Equals(statusValue?.ToString(), "open", StringComparison.OrdinalIgnoreCase);
                }

                return question is JsonElement questionElement
                    && questionElement.ValueKind == JsonValueKind.Object
                    && questionElement.TryGetProperty("status", out var status)
                    && string.Equals(status.GetString(), "open", StringComparison.OrdinalIgnoreCase);
            });
        }

        return 0;
    }

    public static int PromoteVerifiedConditionsFromArtifacts(
        SpecNode spec,
        string verifier,
        string verificationSource,
        DateTime verifiedAtUtc)
    {
        var promoted = 0;

        foreach (var condition in spec.Conditions)
        {
            if (!CanPromoteConditionFromArtifacts(condition))
            {
                continue;
            }

            condition.Status = "verified";
            condition.Metadata ??= new Dictionary<string, object>();
            condition.Metadata["lastVerifiedAt"] = verifiedAtUtc.ToString("o");
            condition.Metadata["lastVerifiedBy"] = verifier;
            condition.Metadata["verificationSource"] = verificationSource;
            condition.Metadata.Remove("reviewReason");
            promoted++;
        }

        return promoted;
    }

    private static int CountCodeRefs(SpecNode spec)
        => (spec.CodeRefs?.Count ?? 0) + spec.Conditions.Sum(condition => condition.CodeRefs?.Count ?? 0);

    private static bool CanPromoteConditionFromArtifacts(SpecCondition condition)
    {
        if (string.Equals(condition.Status, "verified", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(condition.Status, "done", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasManualVerificationRequirement(condition.Metadata))
        {
            return false;
        }

        if (!HasSupportingEvidence(condition))
        {
            return false;
        }

        return HasHealthyAutomatedTests(condition);
    }

    private static bool HasManualVerificationRequirement(Dictionary<string, object>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return false;
        }

        var requiresManualVerification = TryGetBoolean(metadata, "requiresManualVerification", out var required) && required;
        var explicitItems = TryGetManualVerificationItems(metadata, "condition", null);
        return requiresManualVerification || explicitItems.Count > 0;
    }

    private static bool HasSupportingEvidence(SpecCondition condition)
        => condition.Evidence.Any(evidence =>
            !string.IsNullOrWhiteSpace(evidence.Path)
            || !string.IsNullOrWhiteSpace(evidence.Summary));

    private static bool HasHealthyAutomatedTests(SpecCondition condition)
    {
        if (condition.Tests.Count == 0)
        {
            return false;
        }

        if (condition.Tests.Any(test => test.Quarantined))
        {
            return false;
        }

        var statuses = condition.Tests
            .Select(test => test.Status?.Trim().ToLowerInvariant() ?? string.Empty)
            .ToList();

        if (statuses.Any(status => status is "failed" or "flaky" or "quarantined"))
        {
            return false;
        }

        return statuses.Any(status => status == "passed");
    }

    private static bool HasFailedChecks(SpecNode spec)
        => spec.Conditions.Any(ConditionHasFailedChecks);

    private static bool HasFailedManualVerification(SpecNode spec)
        => spec.Conditions.Any(condition => condition.Metadata != null
            && TryGetValue(condition.Metadata, "manualVerificationStatus", out var manualStatus)
            && string.Equals(manualStatus?.ToString(), "failed", StringComparison.OrdinalIgnoreCase));

    private static bool ConditionHasFailedChecks(SpecCondition condition)
    {
        if (condition.Tests.Any(test =>
                string.Equals(test.Status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "flaky", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "quarantined", StringComparison.OrdinalIgnoreCase)
                || test.Quarantined))
        {
            return true;
        }

        return false;
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