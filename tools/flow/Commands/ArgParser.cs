namespace Flow.Commands;

/// <summary>Simple named argument parser: --key value (supports multi-value keys)</summary>
internal static class ArgParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var multiCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var positionalIndex = 0;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    var value = args[++i];
                    if (result.ContainsKey(key))
                    {
                        // multi-value: store as key__0, key__1, ...
                        multiCounts.TryGetValue(key, out var n);
                        result[$"{key}__{n}"] = value;
                        multiCounts[key] = n + 1;
                    }
                    else
                    {
                        result[key] = value;
                    }
                }
                else
                {
                    result[key] = "true"; // flag
                }
            }
            else
            {
                result[$"_{++positionalIndex}"] = args[i];
            }
        }
        return result;
    }

    public static string? Get(this Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : null;

    public static bool Flag(this Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) && v == "true";

    public static List<string> GetAll(this Dictionary<string, string> d, string key)
    {
        var result = new List<string>();
        if (d.TryGetValue(key, out var first))
            result.Add(first);
        for (var i = 0; d.TryGetValue($"{key}__{i}", out var v); i++)
            result.Add(v);
        return result;
    }
}
