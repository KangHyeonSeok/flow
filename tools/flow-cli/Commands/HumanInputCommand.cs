using Cocona;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    [Command("human-input", Description = "Request input from human operator")]
    public void HumanInput(
        [Option("type", Description = "Input type: confirm, select, text")] string type = "text",
        [Option("prompt", Description = "Prompt message")] string prompt = "",
        [Option("options", Description = "Options for select type")] string[]? options = null,
        [Option("timeout", Description = "Timeout in seconds")] int? timeout = null,
        [Option("default", Description = "Default value")] string? defaultValue = null,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            string? result = type.ToLowerInvariant() switch
            {
                "confirm" => ConsoleHelper.Confirm(prompt, timeout, defaultValue),
                "select" => ConsoleHelper.Select(prompt, options ?? []),
                "text" => ConsoleHelper.Text(prompt),
                _ => throw new ArgumentException(
                    $"Unknown input type: {type}. Use confirm, select, or text.")
            };

            JsonOutput.Write(JsonOutput.Success("human-input", new
            {
                type,
                prompt,
                result,
                timed_out = false
            }), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("human-input", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
