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
        [Option("spec-repo", Description = "Set specRepository git URL")] string? specRepo = null,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            var configService = new FlowConfigService(PathResolver.ConfigPath);

            bool anyWrite = log != null || specRepo != null;

            if (anyWrite)
            {
                var config = configService.Load();

                // --spec-repo: specRepository URL 설정 (F-080-C2)
                if (specRepo != null)
                {
                    config.SpecRepository = specRepo;
                }

                // --log: 로깅 설정
                if (log != null)
                {
                    var enabled = log.ToLowerInvariant() is "on" or "true" or "1";
                    config.Logging ??= new Models.FlowLoggingConfig();
                    config.Logging.Enabled = enabled;
                }

                configService.Save(config);

                var savedConfig = configService.Load();
                JsonOutput.Write(JsonOutput.Success("config",
                    new
                    {
                        specRepository = savedConfig.SpecRepository,
                        specBranch = savedConfig.SpecBranch,
                        logging = savedConfig.Logging
                    },
                    "설정 저장 완료"), pretty);
            }
            else
            {
                // Read current config from config.json (F-080-C1, C2)
                var config = configService.Load();

                JsonOutput.Write(JsonOutput.Success("config",
                    new
                    {
                        specRepository = config.SpecRepository,
                        specBranch = config.SpecBranch,
                        logging = config.Logging
                    },
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
