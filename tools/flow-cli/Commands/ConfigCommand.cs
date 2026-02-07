using System.Text.Json;
using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("config", Description = "View or update configuration")]
    public void Config(
        [Option("log", Description = "Set logging on/off")] string? log = null,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var settingsPath = PathResolver.SettingsPath;

            if (log != null)
            {
                // Update logging setting
                var enabled = log.ToLowerInvariant() is "on" or "true" or "1";
                var settings = new Dictionary<string, object>
                {
                    ["logging"] = new Dictionary<string, object> { ["enabled"] = enabled }
                };

                var json = JsonSerializer.Serialize(settings, JsonOutput.Pretty);
                File.WriteAllText(settingsPath, json);

                JsonOutput.Write(JsonOutput.Success("config",
                    new { logging = new { enabled } },
                    $"로깅 {(enabled ? "활성화" : "비활성화")}"), pretty);
            }
            else
            {
                // Read current config
                object? settings = null;
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize<JsonElement>(json);
                }

                JsonOutput.Write(JsonOutput.Success("config", settings, "현재 설정"), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("config", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
