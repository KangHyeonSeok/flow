using System.Diagnostics;
using System.Text.Json;

namespace FlowCLI.Services;

/// <summary>
/// Bridge to Python E2E test tool.
/// Executes Python scripts as subprocesses and captures JSON output.
/// </summary>
public class PythonBridge
{
    private readonly PathResolver _paths;

    public PythonBridge(PathResolver paths) => _paths = paths;

    /// <summary>
    /// Path to the e2e-test Python package directory.
    /// </summary>
    public string E2ETestDir => Path.Combine(_paths.ProjectRoot, "tools", "e2e-test");

    /// <summary>
    /// Find a working Python 3.12.x executable on the system.
    /// </summary>
    public string? FindPython()
    {
        string[] candidates = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? ["python", "python3", "py"]
            : ["python3.12", "python3", "python"];

        foreach (var cmd in candidates)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                var version = !string.IsNullOrEmpty(output) ? output.Trim() : stderr.Trim();
                if (version.Contains("3.12."))
                    return cmd;
            }
            catch
            {
                // Command not found, try next
            }
        }

        return null;
    }

    /// <summary>
    /// Run a Python module with arguments, capturing stdout as JSON.
    /// </summary>
    public PythonResult RunModule(string module, params string[] arguments)
    {
        var python = FindPython();
        if (python == null)
        {
            return new PythonResult
            {
                ExitCode = -1,
                Error = "Python 3.12 not found. Please install from https://www.python.org/downloads/"
            };
        }

        var args = $"-m {module}";
        if (arguments.Length > 0)
            args += " " + string.Join(" ", arguments.Select(QuoteArg));

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = args,
                    WorkingDirectory = E2ETestDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new PythonResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim(),
                Error = process.ExitCode != 0 ? stderr.Trim() : null
            };
        }
        catch (Exception ex)
        {
            return new PythonResult
            {
                ExitCode = -1,
                Error = $"Failed to execute Python: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Run a Python script file with arguments.
    /// </summary>
    public PythonResult RunScript(string scriptPath, params string[] arguments)
    {
        var python = FindPython();
        if (python == null)
        {
            return new PythonResult
            {
                ExitCode = -1,
                Error = "Python 3.12 not found. Please install from https://www.python.org/downloads/"
            };
        }

        var args = $"\"{scriptPath}\"";
        if (arguments.Length > 0)
            args += " " + string.Join(" ", arguments.Select(QuoteArg));

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = args,
                    WorkingDirectory = E2ETestDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new PythonResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim(),
                Error = process.ExitCode != 0 ? stderr.Trim() : null
            };
        }
        catch (Exception ex)
        {
            return new PythonResult
            {
                ExitCode = -1,
                Error = $"Failed to execute Python: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Try to parse JSON from Python stdout output.
    /// Searches backwards for the outermost '{' that forms valid JSON.
    /// </summary>
    public static JsonElement? TryParseJson(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        // Search backwards for each '{' and try to parse from there
        var pos = output.Length;
        while (pos > 0)
        {
            pos = output.LastIndexOf('{', pos - 1);
            if (pos < 0) break;

            try
            {
                var jsonStr = output[pos..].TrimEnd();
                var element = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                // Verify it's a JSON object (not a fragment)
                if (element.ValueKind == JsonValueKind.Object)
                    return element;
            }
            catch
            {
                // Not valid JSON from this position, try previous '{'
            }
        }

        return null;
    }

    private static string QuoteArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"'))
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        return arg;
    }
}

/// <summary>
/// Result of a Python subprocess execution.
/// </summary>
public class PythonResult
{
    public int ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public string? Error { get; set; }

    public bool IsSuccess => ExitCode == 0;
}
