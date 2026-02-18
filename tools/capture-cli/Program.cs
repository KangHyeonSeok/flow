using System.CommandLine;
using CaptureCli.Services;

namespace CaptureCli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Flow Capture CLI - Windows screen capture tool");

        // ─── window 커맨드 ──────────────────────────────────

        var nameOption = new Option<string?>("--name", "Window title to search for");
        var processOption = new Option<string?>("--process", "Process name to search for");
        var delayOption = new Option<int>("--delay", () => 0, "Delay in seconds before capture");
        var outputOption = new Option<string>("--output", () => "capture.png", "Output file path");
        var formatOption = new Option<string>("--format", () => "png", "Image format (png/jpg)");
        var cropClientOption = new Option<bool>("--crop-client", () => false, "Exclude title bar");

        var windowCommand = new Command("window", "Capture a specific window")
        {
            nameOption, processOption, delayOption, outputOption, formatOption, cropClientOption
        };

        windowCommand.SetHandler(async (name, process, delay, output, format, cropClient) =>
        {
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(process))
            {
                Console.Error.WriteLine("Error: --name or --process is required.");
                Environment.ExitCode = 4;
                return;
            }

            var windows = !string.IsNullOrEmpty(name)
                ? WindowFinder.FindByName(name)
                : WindowFinder.FindByProcess(process!);

            if (windows.Count == 0)
            {
                Console.Error.WriteLine(
                    $"Error: No window found matching '{name ?? process}'.");
                Environment.ExitCode = 1;
                return;
            }

            if (windows.Count > 1)
            {
                Console.WriteLine($"Found {windows.Count} windows. Using first match:");
                foreach (var w in windows.Take(5))
                    Console.WriteLine($"  [{w.ProcessName}] {w.Title}");
            }

            var target = windows[0];
            Console.WriteLine($"Capturing: [{target.ProcessName}] {target.Title}");

            using var service = new CaptureService();
            Environment.ExitCode = await service.CaptureWindowAsync(
                target.Handle, output, format, delay, cropClient);

        }, nameOption, processOption, delayOption, outputOption, formatOption, cropClientOption);

        // ─── monitor 커맨드 ─────────────────────────────────

        var indexOption = new Option<int>("--index", () => 0, "Monitor index (0-based)");
        var monitorOutputOption = new Option<string>("--output", () => "monitor.png", "Output file path");
        var monitorFormatOption = new Option<string>("--format", () => "png", "Image format (png/jpg)");
        var monitorDelayOption = new Option<int>("--delay", () => 0, "Delay in seconds before capture");

        var monitorCommand = new Command("monitor", "Capture a monitor")
        {
            indexOption, monitorOutputOption, monitorFormatOption, monitorDelayOption
        };

        monitorCommand.SetHandler(async (index, output, format, delay) =>
        {
            var monitors = WindowFinder.GetMonitors();

            if (index < 0 || index >= monitors.Count)
            {
                Console.Error.WriteLine(
                    $"Error: Monitor index {index} out of range (0-{monitors.Count - 1}).");
                Environment.ExitCode = 4;
                return;
            }

            var (handle, bounds, name) = monitors[index];
            Console.WriteLine($"Capturing monitor {index}: {name} ({bounds.Width}x{bounds.Height})");

            using var service = new CaptureService();
            Environment.ExitCode = await service.CaptureMonitorAsync(
                handle, output, format, delay);

        }, indexOption, monitorOutputOption, monitorFormatOption, monitorDelayOption);

        // ─── list-windows 커맨드 ────────────────────────────

        var listCommand = new Command("list-windows", "List all visible windows");
        listCommand.SetHandler(() =>
        {
            var windows = WindowFinder.GetAllWindows();
            Console.WriteLine($"{"PID",-8} {"Process",-20} {"Title"}");
            Console.WriteLine(new string('─', 70));

            foreach (var w in windows.OrderBy(w => w.ProcessName))
            {
                Console.WriteLine($"{w.ProcessId,-8} {w.ProcessName,-20} {w.Title}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {windows.Count} windows");
        });

        // ─── 루트 커맨드에 등록 ─────────────────────────────

        rootCommand.AddCommand(windowCommand);
        rootCommand.AddCommand(monitorCommand);
        rootCommand.AddCommand(listCommand);

        return await rootCommand.InvokeAsync(args);
    }
}
