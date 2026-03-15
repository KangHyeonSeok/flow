namespace Flow.Commands;

/// <summary>Simple named argument parser: --key value</summary>
internal static class ArgParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[key] = args[++i];
                }
                else
                {
                    result[key] = "true"; // flag
                }
            }
            else
            {
                // positional — store under empty key with index suffix
                result[$"_{result.Count}"] = args[i];
            }
        }
        return result;
    }

    public static string? Get(this Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : null;

    public static bool Flag(this Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) && v == "true";
}
