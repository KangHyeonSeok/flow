using System.Text.Json;
using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// F-006-C1: Converts legacy positional/option CLI arguments into a FlowRequest JSON model,
/// enabling the compatibility layer to route legacy calls through the new JSON dispatcher.
/// </summary>
public static class LegacyArgsAdapter
{
    /// <summary>
    /// Commands registered in the JSON dispatcher (RouteRequest).
    /// These are the commands intercepted by the legacy compatibility layer.
    /// </summary>
    private static readonly HashSet<string> DispatchableCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "config", "db-add", "db-query", "test",
        "runner-start", "runner-status", "runner-stop", "runner-logs",
        "human-input", "spec-init", "spec-create", "spec-get", "spec-list",
        "spec-delete", "spec-validate", "spec-graph", "spec-impact",
        "spec-propagate", "spec-check-refs", "spec-order", "spec-append-review"
    };

    /// <summary>
    /// Returns true if the first arg is a known legacy command that can be routed through the JSON dispatcher.
    /// </summary>
    public static bool IsLegacyCommand(string firstArg)
        => DispatchableCommands.Contains(firstArg);

    /// <summary>
    /// F-006-C1: Converts a legacy string[] args array into a FlowRequest.
    /// Parsing rules:
    /// - args[0]: command name → FlowRequest.Command
    /// - --flag (no following value): boolean true option
    /// - --option value (value does not start with '--'): typed option
    /// - Non-flag positional args: FlowRequest.Arguments[]
    /// </summary>
    public static FlowRequest ToFlowRequest(string[] args)
    {
        if (args.Length == 0)
            return new FlowRequest { Command = "" };

        var command = args[0];
        var options = new Dictionary<string, JsonElement>();
        var positionalArgs = new List<string>();

        int i = 1;
        while (i < args.Length)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (string.IsNullOrEmpty(key)) { i++; continue; }

                // If next token exists and is not a flag, treat it as the option's value
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    options[key] = ParseElement(args[i + 1]);
                    i += 2;
                }
                else
                {
                    options[key] = TrueElement();
                    i++;
                }
            }
            else if (arg.StartsWith("-") && arg.Length == 2)
            {
                // Short single-char flag (e.g. -h) — skip, let Cocona handle if needed
                i++;
            }
            else
            {
                positionalArgs.Add(arg);
                i++;
            }
        }

        return new FlowRequest
        {
            Command = command,
            Arguments = positionalArgs.Count > 0 ? positionalArgs.ToArray() : null,
            Options = options.Count > 0 ? options : null
        };
    }

    /// <summary>Extracts the presence of --pretty flag from args without full parsing.</summary>
    public static bool ExtractPretty(string[] args)
        => Array.Exists(args, a => a.Equals("--pretty", StringComparison.OrdinalIgnoreCase));

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static JsonElement TrueElement()
        => JsonDocument.Parse("true").RootElement.Clone();

    /// <summary>
    /// Parses a string value into the most specific JSON primitive type:
    /// bool → JSON boolean, integer → JSON number, otherwise → JSON string.
    /// </summary>
    private static JsonElement ParseElement(string value)
    {
        if (bool.TryParse(value, out var b))
            return JsonDocument.Parse(b ? "true" : "false").RootElement.Clone();

        if (int.TryParse(value, out var n))
            return JsonDocument.Parse(n.ToString()).RootElement.Clone();

        var jsonStr = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(jsonStr).RootElement.Clone();
    }
}
