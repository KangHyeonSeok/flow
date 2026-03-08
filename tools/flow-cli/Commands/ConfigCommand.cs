using System.Text.Json;
using Cocona;
using FlowCLI.Services;
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
            var configService = new FlowConfigService(PathResolver.ConfigPath);

            if (log != null)
            {
                var config = configService.Load();
                var enabled = log.ToLowerInvariant() is "on" or "true" or "1";
                config.Logging ??= new Models.FlowLoggingConfig();
                config.Logging.Enabled = enabled;
                configService.Save(config);

                var savedConfig = configService.Load();
                JsonOutput.Write(JsonOutput.Success("config",
                    new { logging = savedConfig.Logging },
                    "설정 저장 완료"), pretty);
            }
            else
            {
                var config = configService.Load();
                JsonOutput.Write(JsonOutput.Success("config",
                    new { logging = config.Logging },
                    "현재 설정"), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("config", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
