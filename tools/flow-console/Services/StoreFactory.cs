using FlowCore.Agents;
using FlowCore.Agents.Dummy;
using FlowCore.Runner;
using FlowCore.Storage;

namespace FlowConsole.Services;

/// <summary>FileFlowStore + FlowRunner 인스턴스 생성</summary>
public static class StoreFactory
{
    public static (FileFlowStore Store, FlowRunner Runner) Create(string projectId)
    {
        var store = new FileFlowStore(projectId);

        var agents = new IAgentAdapter[]
        {
            new DummyPlanner(),
            new DummyArchitect(),
            new DummyDeveloper(),
            new DummyTestValidator(),
            new DummySpecValidator()
        };

        var config = new RunnerConfig
        {
            PollIntervalSeconds = 9999,
            MaxSpecsPerCycle = 0
        };

        var runner = new FlowRunner(store, agents, config);
        return (store, runner);
    }
}
