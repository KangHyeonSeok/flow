using System.Text.Json;
using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Agents.Dummy;
using FlowCore.Backend;
using FlowCore.Runner;
using FlowCore.Serialization;
using FlowCore.Storage;

namespace FlowConsole.Services;

/// <summary>FileFlowStore + FlowRunner 인스턴스 생성</summary>
public static class StoreFactory
{
    public static (FileFlowStore Store, FlowRunner Runner) Create(string projectId)
    {
        var flowHome = Environment.GetEnvironmentVariable("FLOW_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow");
        var store = new FileFlowStore(projectId, flowHome);
        var backendConfig = LoadBackendConfig(flowHome);

        IAgentAdapter[] agents;

        if (backendConfig != null)
        {
            var registry = new BackendRegistry(backendConfig);
            var promptBuilder = new PromptBuilder();
            var outputParser = new OutputParser();

            agents =
            [
                new CliPlanner(registry, promptBuilder, outputParser),
                new CliArchitect(registry, promptBuilder, outputParser),
                new CliDeveloper(registry, promptBuilder, outputParser),
                new CliTestValidator(registry, promptBuilder, outputParser),
                new CliSpecValidator(registry, promptBuilder, outputParser)
            ];
        }
        else
        {
            agents =
            [
                new DummyPlanner(),
                new DummyArchitect(),
                new DummyDeveloper(),
                new DummyTestValidator(),
                new DummySpecValidator()
            ];
        }

        var config = new RunnerConfig
        {
            PollIntervalSeconds = 9999,
            MaxSpecsPerCycle = 0
        };

        // worktree provisioner: backend config가 있을 때만 (CLI agent 활성 시)
        IWorktreeProvisioner? worktreeProvisioner = null;
        if (backendConfig != null)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            worktreeProvisioner = new GitWorktreeProvisioner(projectRoot, flowHome);
        }

        var runner = new FlowRunner(store, agents, config, worktreeProvisioner: worktreeProvisioner);
        return (store, runner);
    }

    /// <summary>
    /// FlowHome 내의 backend-config.json을 읽는다. 없으면 null.
    /// </summary>
    private static BackendConfig? LoadBackendConfig(string flowHome)
    {
        var configPath = Path.Combine(flowHome, "backend-config.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<BackendConfig>(json, FlowJsonOptions.Default);
        }
        catch
        {
            return null;
        }
    }
}
