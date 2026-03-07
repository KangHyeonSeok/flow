using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlowCLI.Services.TestSync;

/// <summary>
/// xUnit TRX(XML) / xUnit JSON / pytest JSON / Jest JSON / 정규화 JSON 파서 (F-014-C1).
/// 각 형식에서 [Trait("Spec","F-014-C1")] / @pytest.mark.spec / [spec:...] 어노테이션을 추출한다.
/// </summary>
public static class TestResultParser
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex SpecAnnotationInName = new(
        @"\[spec:([A-Z]-\d{3}(?:-\d{2})?(?:-C\d+)?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkerSpecPattern = new(
        @"^spec:(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 파일 확장자와 내용에 따라 자동으로 파서를 선택하여 파싱한다.
    /// </summary>
    public static TestRunResult Parse(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".xml" || ext == ".trx")
            return ParseXUnitTrx(File.ReadAllText(filePath));

        var json = File.ReadAllText(filePath);
        return ParseJson(json);
    }

    /// <summary>
    /// JSON 문자열을 파싱한다. 형식을 자동 감지한다.
    /// </summary>
    public static TestRunResult ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Jest format: has "testResults" array at root with nested "testResults"
        if (root.TryGetProperty("testResults", out var jestResults) &&
            jestResults.ValueKind == JsonValueKind.Array)
        {
            return ParseJest(root);
        }

        // pytest-json-report format: has "tests" array with "nodeid" and optional "markers"
        if (root.TryGetProperty("tests", out var pytestTests) &&
            pytestTests.ValueKind == JsonValueKind.Array &&
            TryGetProperty(pytestTests, "nodeid") != null)
        {
            return ParsePytest(root);
        }

        // xUnit JSON format: has "assembly" or "collection" array
        if (root.TryGetProperty("collection", out _) || root.TryGetProperty("assembly", out _))
            return ParseXUnitJson(root);

        // Normalized/generic format: has "tests" array with "traits" or "markers"
        if (root.TryGetProperty("tests", out _))
            return ParseNormalized(root);

        // Fallback: treat as normalized
        return ParseNormalized(root);
    }

    // ─── xUnit TRX (XML) ────────────────────────────────────────────────────

    public static TestRunResult ParseXUnitTrx(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var result = new TestRunResult { Framework = "xunit" };

        // Collect properties/traits from UnitTest definitions
        var propsById = new Dictionary<string, Dictionary<string, string>>();
        foreach (var unitTest in doc.Descendants(ns + "UnitTest"))
        {
            var id = unitTest.Attribute("id")?.Value ?? "";
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in unitTest.Descendants(ns + "Property"))
            {
                var key = prop.Element(ns + "Key")?.Value ?? prop.Attribute("key")?.Value ?? "";
                var val = prop.Element(ns + "Value")?.Value ?? prop.Attribute("value")?.Value ?? "";
                if (!string.IsNullOrEmpty(key))
                    props[key] = val;
            }
            if (!string.IsNullOrEmpty(id))
                propsById[id] = props;
        }

        foreach (var testResult in doc.Descendants(ns + "UnitTestResult"))
        {
            var testId = testResult.Attribute("testId")?.Value ?? testResult.Attribute("executionId")?.Value ?? Guid.NewGuid().ToString();
            var name = testResult.Attribute("testName")?.Value ?? "";
            var outcome = testResult.Attribute("outcome")?.Value ?? "";
            var duration = ParseDuration(testResult.Attribute("duration")?.Value);

            var traits = propsById.TryGetValue(testId, out var p) ? p : new Dictionary<string, string>();

            // Also check inline properties
            foreach (var prop in testResult.Descendants(ns + "Property"))
            {
                var key = prop.Element(ns + "Key")?.Value ?? prop.Attribute("key")?.Value ?? "";
                var val = prop.Element(ns + "Value")?.Value ?? prop.Attribute("value")?.Value ?? "";
                if (!string.IsNullOrEmpty(key))
                    traits[key] = val;
            }

            var errorMsg = testResult.Descendants(ns + "Message").FirstOrDefault()?.Value;

            result.Tests.Add(new TestCaseResult
            {
                Id = testId,
                Name = name,
                Status = MapTrxOutcome(outcome),
                DurationMs = duration,
                ErrorMessage = errorMsg,
                Traits = traits
            });
        }

        return result;
    }

    // ─── Jest JSON ───────────────────────────────────────────────────────────

    private static TestRunResult ParseJest(JsonElement root)
    {
        var result = new TestRunResult { Framework = "jest" };

        foreach (var suiteResult in root.GetProperty("testResults").EnumerateArray())
        {
            var suiteName = suiteResult.TryGetProperty("testFilePath", out var fp) ? fp.GetString() : null;

            if (!suiteResult.TryGetProperty("testResults", out var cases))
                continue;

            foreach (var tc in cases.EnumerateArray())
            {
                var name = tc.TryGetProperty("fullName", out var fn) ? fn.GetString() ?? "" :
                           tc.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var status = tc.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                var duration = tc.TryGetProperty("duration", out var dur) ? (double?)dur.GetDouble() : null;
                var id = tc.TryGetProperty("ancestorTitles", out _) ? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

                var markers = ExtractMarkersFromName(name);
                var traits = ExtractTraitsFromName(name);

                // Jest markers in test props
                if (tc.TryGetProperty("markers", out var mArr) && mArr.ValueKind == JsonValueKind.Array)
                    foreach (var m in mArr.EnumerateArray())
                        if (m.ValueKind == JsonValueKind.String)
                            markers.Add(m.GetString() ?? "");

                result.Tests.Add(new TestCaseResult
                {
                    Id = id,
                    Name = name,
                    Suite = suiteName,
                    Status = MapJestStatus(status),
                    DurationMs = duration,
                    Markers = markers,
                    Traits = traits
                });
            }
        }

        return result;
    }

    // ─── pytest JSON (pytest-json-report) ───────────────────────────────────

    private static TestRunResult ParsePytest(JsonElement root)
    {
        var result = new TestRunResult { Framework = "pytest" };

        foreach (var tc in root.GetProperty("tests").EnumerateArray())
        {
            var nodeId = tc.TryGetProperty("nodeid", out var nid) ? nid.GetString() ?? "" : "";
            var outcome = tc.TryGetProperty("outcome", out var out_) ? out_.GetString() ?? "" : "";
            var duration = tc.TryGetProperty("duration", out var dur) ? (double?)(dur.GetDouble() * 1000) : null;
            var name = nodeId.Contains("::") ? nodeId.Split("::").Last() : nodeId;
            var suite = nodeId.Contains("::") ? string.Join("::", nodeId.Split("::").SkipLast(1)) : null;

            var markers = new List<string>();

            // pytest-json-report stores markers as array of objects with "name" and optional "args"
            if (tc.TryGetProperty("markers", out var mArr) && mArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mArr.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.String)
                    {
                        markers.Add(m.GetString() ?? "");
                    }
                    else if (m.ValueKind == JsonValueKind.Object)
                    {
                        var markerName = m.TryGetProperty("name", out var mn) ? mn.GetString() ?? "" : "";
                        if (markerName.Equals("spec", StringComparison.OrdinalIgnoreCase))
                        {
                            // @pytest.mark.spec("F-014-C1")
                            if (m.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var arg in args.EnumerateArray())
                                    if (arg.ValueKind == JsonValueKind.String)
                                        markers.Add($"spec:{arg.GetString()}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(markerName))
                        {
                            markers.Add(markerName);
                        }
                    }
                }
            }

            // Also check keywords
            if (tc.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in kw.EnumerateObject())
                    if (prop.Name.StartsWith("spec:", StringComparison.OrdinalIgnoreCase))
                        markers.Add(prop.Name);
            }

            var errorMsg = (string?)null;
            if (tc.TryGetProperty("call", out var call) && call.ValueKind == JsonValueKind.Object)
            {
                if (call.TryGetProperty("longrepr", out var lr))
                    errorMsg = lr.GetString();
            }

            result.Tests.Add(new TestCaseResult
            {
                Id = nodeId,
                Name = name,
                Suite = suite,
                Status = MapPytestOutcome(outcome),
                DurationMs = duration,
                ErrorMessage = errorMsg,
                Markers = markers
            });
        }

        return result;
    }

    // ─── xUnit JSON ─────────────────────────────────────────────────────────

    private static TestRunResult ParseXUnitJson(JsonElement root)
    {
        var result = new TestRunResult { Framework = "xunit" };

        // Support both top-level "collection" array and nested structure
        var collections = root.TryGetProperty("collection", out var col)
            ? col.EnumerateArray()
            : root.TryGetProperty("assemblies", out var asm)
                ? asm.EnumerateArray().SelectMany(a =>
                    a.TryGetProperty("collection", out var c) ? c.EnumerateArray() : [])
                : [];

        foreach (var collection in collections)
        {
            var tests = collection.TryGetProperty("test", out var t)
                ? t.EnumerateArray()
                : collection.TryGetProperty("tests", out var ts)
                    ? ts.EnumerateArray()
                    : [];

            foreach (var tc in tests)
            {
                var name = tc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var resultStr = tc.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
                var time = tc.TryGetProperty("time", out var tm)
                    ? tm.ValueKind == JsonValueKind.String
                        ? ParseDurationString(tm.GetString())
                        : tm.GetDouble() * 1000
                    : (double?)null;

                var traits = new Dictionary<string, string>();
                if (tc.TryGetProperty("traits", out var traitElem) && traitElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in traitElem.EnumerateObject())
                    {
                        var val = prop.Value.ValueKind == JsonValueKind.Array
                            ? prop.Value.EnumerateArray().FirstOrDefault().GetString() ?? ""
                            : prop.Value.GetString() ?? "";
                        traits[prop.Name] = val;
                    }
                }

                var errorMsg = (string?)null;
                if (tc.TryGetProperty("failure", out var fail) && fail.ValueKind == JsonValueKind.Object)
                {
                    errorMsg = fail.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                }

                result.Tests.Add(new TestCaseResult
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Status = MapXUnitResult(resultStr),
                    DurationMs = time,
                    ErrorMessage = errorMsg,
                    Traits = traits,
                    Markers = ExtractMarkersFromName(name)
                });
            }
        }

        return result;
    }

    // ─── Normalized/Generic JSON ─────────────────────────────────────────────

    private static TestRunResult ParseNormalized(JsonElement root)
    {
        var framework = root.TryGetProperty("framework", out var fw) ? fw.GetString() ?? "generic" : "generic";
        var runAt = root.TryGetProperty("runAt", out var ra) ? ra.GetString() : null;
        var result = new TestRunResult { Framework = framework, RunAt = runAt };

        if (!root.TryGetProperty("tests", out var tests) || tests.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var tc in tests.EnumerateArray())
        {
            var id = tc.TryGetProperty("id", out var tid) ? tid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
            var name = tc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var suite = tc.TryGetProperty("suite", out var s) ? s.GetString() : null;
            var status = tc.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var duration = tc.TryGetProperty("durationMs", out var d) ? (double?)d.GetDouble() : null;
            var error = tc.TryGetProperty("errorMessage", out var e) ? e.GetString() : null;

            var traits = new Dictionary<string, string>();
            if (tc.TryGetProperty("traits", out var traitEl) && traitEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in traitEl.EnumerateObject())
                    traits[prop.Name] = prop.Value.GetString() ?? "";
            }

            var markers = new List<string>();
            if (tc.TryGetProperty("markers", out var mArr) && mArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mArr.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.String)
                        markers.Add(m.GetString() ?? "");
            }

            result.Tests.Add(new TestCaseResult
            {
                Id = id,
                Name = name,
                Suite = suite,
                Status = status,
                DurationMs = duration,
                ErrorMessage = error,
                Traits = traits,
                Markers = markers
            });
        }

        return result;
    }

    // ─── Annotation extraction ───────────────────────────────────────────────

    /// <summary>
    /// 테스트 케이스에서 스펙 조건 ID를 추출한다.
    /// 우선순위: traits["Spec"] → markers["spec:..."] → test name [spec:...]
    /// </summary>
    public static string? ExtractSpecConditionId(TestCaseResult tc)
    {
        // 1. xUnit Trait: [Trait("Spec", "F-014-C1")]
        if (tc.Traits.TryGetValue("Spec", out var traitVal) && !string.IsNullOrEmpty(traitVal))
            return traitVal.Trim();

        // 2. Markers: "spec:F-014-C1" (pytest @pytest.mark.spec / Jest)
        foreach (var marker in tc.Markers)
        {
            var m = MarkerSpecPattern.Match(marker);
            if (m.Success)
                return m.Groups[1].Value.Trim();
        }

        // 3. Annotation in test name: [spec:F-014-C1]
        var nameMatch = SpecAnnotationInName.Match(tc.Name);
        if (nameMatch.Success)
            return nameMatch.Groups[1].Value.Trim();

        return null;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static List<string> ExtractMarkersFromName(string name)
    {
        var markers = new List<string>();
        var matches = SpecAnnotationInName.Matches(name);
        foreach (Match m in matches)
            markers.Add($"spec:{m.Groups[1].Value}");
        return markers;
    }

    private static Dictionary<string, string> ExtractTraitsFromName(string name)
        => new(); // Jest doesn't use Trait pattern

    private static string? TryGetProperty(JsonElement arr, string propName)
    {
        if (arr.ValueKind != JsonValueKind.Array) return null;
        var first = arr.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return null;
        return first.TryGetProperty(propName, out var p) ? p.GetString() : null;
    }

    private static string MapTrxOutcome(string outcome) => outcome.ToLowerInvariant() switch
    {
        "passed" => "passed",
        "failed" => "failed",
        "notexecuted" or "ignored" or "aborted" => "skipped",
        _ => outcome.ToLowerInvariant()
    };

    private static string MapJestStatus(string status) => status.ToLowerInvariant() switch
    {
        "passed" => "passed",
        "failed" => "failed",
        "pending" or "todo" or "skipped" => "skipped",
        _ => status.ToLowerInvariant()
    };

    private static string MapPytestOutcome(string outcome) => outcome.ToLowerInvariant() switch
    {
        "passed" => "passed",
        "failed" or "error" => "failed",
        "skipped" or "xfailed" or "xpassed" => "skipped",
        _ => outcome.ToLowerInvariant()
    };

    private static string MapXUnitResult(string result) => result.ToLowerInvariant() switch
    {
        "pass" => "passed",
        "fail" => "failed",
        "skip" => "skipped",
        _ => result.ToLowerInvariant()
    };

    private static double? ParseDuration(string? dur)
    {
        if (string.IsNullOrEmpty(dur)) return null;
        if (TimeSpan.TryParse(dur, out var ts)) return ts.TotalMilliseconds;
        if (double.TryParse(dur, out var d)) return d * 1000;
        return null;
    }

    private static double ParseDurationString(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (double.TryParse(s, out var d)) return d * 1000;
        return 0;
    }
}
