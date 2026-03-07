namespace FlowCLI.Services;

/// <summary>
/// F-006-C3: Manages deprecation warnings for legacy CLI argument-style calls.
/// Opt-in via the FLOW_DEPRECATION_WARNINGS environment variable (set to "1", "true", or "warn").
/// </summary>
public static class DeprecationPolicy
{
    /// <summary>Environment variable that enables deprecation warnings.</summary>
    public const string EnvVar = "FLOW_DEPRECATION_WARNINGS";

    /// <summary>Version in which legacy direct-arg calls will be removed.</summary>
    public const string DeprecationVersion = "2.0.0";

    /// <summary>
    /// Returns true when deprecation warnings are explicitly enabled via the environment variable.
    /// Accepted truthy values: "1", "true", "warn" (case-insensitive).
    /// </summary>
    public static bool IsEnabled()
    {
        var val = Environment.GetEnvironmentVariable(EnvVar);
        return val is not null &&
               (val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("warn", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// F-006-C3: Writes a deprecation warning to stderr when warnings are enabled.
    /// The message includes a transition guide and the planned removal version.
    /// </summary>
    public static void WarnIfEnabled(string command)
    {
        if (!IsEnabled()) return;

        Console.Error.WriteLine(
            $"[DEPRECATION WARNING] Direct CLI argument style for '{command}' is deprecated " +
            $"and will be removed in v{DeprecationVersion}.");
        Console.Error.WriteLine(
            $"  Migrate to JSON request: flow invoke '{{\"command\":\"{command}\",...}}'");
        Console.Error.WriteLine(
            $"  Suppress this warning:   {EnvVar}=none");
    }
}
