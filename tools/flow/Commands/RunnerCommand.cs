using System.Diagnostics;
using System.Text.Json;
using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Agents.Dummy;
using FlowCore.Backend;
using FlowCore.Runner;
using FlowCore.Serialization;
using FlowCore.Storage;

namespace Flow.Commands;

internal static class RunnerCommand
{
    private static string PidFile =>
        Path.Combine(
            Environment.GetEnvironmentVariable("FLOW_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow"),
            "runner.pid");

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: flow runner <start|stop|status> [options]");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var opts = ArgParser.Parse(args[1..]);

        return sub switch
        {
            "start" => await StartAsync(opts),
            "stop" => Stop(),
            "status" => Status(),
            _ => Error($"Unknown subcommand: {sub}")
        };
    }

    private static async Task<int> StartAsync(Dictionary<string, string> opts)
    {
        var projectId = opts.Get("project");
        if (projectId == null)
        {
            Console.WriteLine("Required: --project <id>");
            return 1;
        }

        var once = opts.Flag("once");
        var flowHome = Environment.GetEnvironmentVariable("FLOW_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow");

        var store = new FileFlowStore(projectId, flowHome);
        var agents = BuildAgents(flowHome);
        var config = new RunnerConfig();

        IWorktreeProvisioner? worktree = null;
        var projectRoot = Directory.GetCurrentDirectory();
        if (HasBackendConfig(flowHome))
            worktree = new GitWorktreeProvisioner(projectRoot, flowHome);

        var observer = new ConsoleRunnerObserver();
        var runner = new FlowRunner(store, agents, config, worktreeProvisioner: worktree, observer: observer);

        if (once)
        {
            Console.WriteLine($"Running single cycle for project '{projectId}'...");
            var count = await runner.RunOnceAsync();
            Console.WriteLine($"Processed {count} spec(s).");
            return 0;
        }

        // Daemon mode
        WritePidFile();
        Console.WriteLine($"Runner started (PID {Environment.ProcessId}) for project '{projectId}'");
        Console.WriteLine($"Poll interval: {config.PollIntervalSeconds}s | Press Ctrl+C to stop");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Shutting down...");
        };

        try
        {
            await runner.RunDaemonAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            CleanupPidFile();
            Console.WriteLine("Runner stopped.");
        }

        return 0;
    }

    private static int Stop()
    {
        if (!File.Exists(PidFile))
        {
            Console.WriteLine("No runner PID file found. Runner may not be running.");
            return 1;
        }

        var pidText = File.ReadAllText(PidFile).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            Console.WriteLine($"Invalid PID file content: {pidText}");
            File.Delete(PidFile);
            return 1;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            Console.WriteLine($"Runner (PID {pid}) stopped.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Process {pid} not found. Cleaning up PID file.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to stop process {pid}: {ex.Message}");
        }

        CleanupPidFile();
        return 0;
    }

    private static int Status()
    {
        if (!File.Exists(PidFile))
        {
            Console.WriteLine("Runner is not running.");
            return 0;
        }

        var pidText = File.ReadAllText(PidFile).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            Console.WriteLine("Invalid PID file.");
            return 1;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            Console.WriteLine($"Runner is running (PID {pid})");
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Runner PID {pid} found but process is not running. Stale PID file.");
            CleanupPidFile();
        }

        return 0;
    }

    private static IAgentAdapter[] BuildAgents(string flowHome)
    {
        var backendConfig = LoadBackendConfig(flowHome);
        if (backendConfig != null)
        {
            var registry = new BackendRegistry(backendConfig);
            var promptBuilder = new PromptBuilder();
            var outputParser = new OutputParser();
            return
            [
                new CliPlanner(registry, promptBuilder, outputParser),
                new CliArchitect(registry, promptBuilder, outputParser),
                new CliDeveloper(registry, promptBuilder, outputParser),
                new CliTestGenerator(registry, promptBuilder, outputParser),
                new CliSpecValidator(registry, promptBuilder, outputParser)
            ];
        }

        return
        [
            new DummyPlanner(),
            new DummyArchitect(),
            new DummyDeveloper(),
            new DummyTestGenerator(),
            new DummySpecValidator()
        ];
    }

    private static bool HasBackendConfig(string flowHome)
        => File.Exists(Path.Combine(flowHome, "backend-config.json"));

    private static BackendConfig? LoadBackendConfig(string flowHome)
    {
        var path = Path.Combine(flowHome, "backend-config.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackendConfig>(json, FlowJsonOptions.Default);
        }
        catch { return null; }
    }

    private static void WritePidFile()
    {
        var dir = Path.GetDirectoryName(PidFile);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(PidFile, Environment.ProcessId.ToString());
    }

    private static void CleanupPidFile()
    {
        try { File.Delete(PidFile); }
        catch { /* best-effort */ }
    }

    private static int Error(string msg)
    {
        Console.WriteLine(msg);
        return 1;
    }
}
