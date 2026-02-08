using System.Text.Json;
using Cocona;
using FlowCLI.Services;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    private PythonBridge? _pythonBridge;
    private PythonBridge PythonBridge => _pythonBridge ??= new PythonBridge(PathResolver);

    [Command("test", Description = "Run E2E tests")]
    public void Test(
        [Argument(Description = "Sub-command (e2e)")] string subCommand,
        [Argument(Description = "Scenario YAML file path")] string? target = null,
        [Option("timeout", Description = "Test timeout in seconds")] int timeout = 300,
        [Option("retry", Description = "Retry count")] int retry = 3,
        [Option("platform", Description = "Target platform (flutter|unity)")] string? platform = null,
        [Option("save-report", Description = "Save report to file")] bool saveReport = false,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            // Validate sub-command
            if (!subCommand.Equals("e2e", StringComparison.OrdinalIgnoreCase))
            {
                JsonOutput.Write(JsonOutput.Error("test",
                    $"Unknown sub-command '{subCommand}'. Supported: e2e"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // Validate scenario file
            if (string.IsNullOrEmpty(target))
            {
                JsonOutput.Write(JsonOutput.Error("test",
                    "Scenario file path is required. Usage: flow test e2e <scenario.yaml>"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // Resolve scenario path
            var scenarioPath = Path.GetFullPath(target);
            if (!File.Exists(scenarioPath))
            {
                JsonOutput.Write(JsonOutput.Error("test",
                    $"Scenario file not found: {target}",
                    new { path = scenarioPath }), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // Check Python availability
            var pythonExe = PythonBridge.FindPython();
            if (pythonExe == null)
            {
                JsonOutput.Write(JsonOutput.Error("test",
                    "Python 3.12 not found. Please install from https://www.python.org/downloads/"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // Check e2e-test directory exists
            if (!Directory.Exists(PythonBridge.E2ETestDir))
            {
                JsonOutput.Write(JsonOutput.Error("test",
                    $"E2E test tool not found at {PythonBridge.E2ETestDir}"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // Build Python arguments
            var args = new List<string> { scenarioPath };

            if (timeout != 300)
            {
                args.Add("--timeout");
                args.Add(timeout.ToString());
            }

            if (retry != 3)
            {
                args.Add("--retry");
                args.Add(retry.ToString());
            }

            if (!string.IsNullOrEmpty(platform))
            {
                args.Add("--platform");
                args.Add(platform);
            }

            if (saveReport)
            {
                args.Add("--save-report");

                // Determine report directory from current feature
                try
                {
                    var (featureName, _) = StateService.GetCurrentState();
                    if (!string.IsNullOrEmpty(featureName))
                    {
                        var reportDir = PathResolver.GetFeatureDir(featureName);
                        args.Add("--report-dir");
                        args.Add(reportDir);
                    }
                }
                catch
                {
                    // No active feature; report will be saved to default location
                }
            }

            // Execute Python E2E tool
            var result = PythonBridge.RunModule("e2e_test.cli", args.ToArray());

            // Try to parse JSON from Python stdout
            var jsonOutput = PythonBridge.TryParseJson(result.Stdout);

            // If Python produced valid flow-format JSON (has "success" property),
            // forward it directly regardless of exit code
            if (jsonOutput.HasValue &&
                jsonOutput.Value.ValueKind == JsonValueKind.Object &&
                jsonOutput.Value.TryGetProperty("success", out _))
            {
                var options = pretty ? JsonOutput.Pretty : JsonOutput.Default;
                Console.WriteLine(JsonSerializer.Serialize(jsonOutput.Value, options));
                if (!result.IsSuccess)
                    Environment.ExitCode = 1;
            }
            else if (result.IsSuccess)
            {
                // Success but no flow-format JSON — wrap raw output
                JsonOutput.Write(JsonOutput.Success("test",
                    new { raw_output = result.Stdout },
                    "Test completed"), pretty);
            }
            else
            {
                // Non-zero exit with no parseable JSON — build error envelope
                var errorData = new Dictionary<string, object?>();

                if (!string.IsNullOrEmpty(result.Stdout))
                    errorData["stdout"] = result.Stdout;

                if (!string.IsNullOrEmpty(result.Stderr))
                    errorData["stderr"] = result.Stderr;

                errorData["exit_code"] = result.ExitCode;

                JsonOutput.Write(JsonOutput.Error("test",
                    !string.IsNullOrEmpty(result.Error) ? result.Error : "E2E test execution failed",
                    errorData), pretty);

                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("test", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
