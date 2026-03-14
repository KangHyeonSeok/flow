using FlowCore.Backend;
using FlowCore.Models;
using FluentAssertions;

namespace FlowCore.Tests;

public class BackendRegistryTests
{
    private static BackendConfig CreateConfig() => new()
    {
        AgentBackends = new()
        {
            ["specValidator"] = new AgentBackendMapping { Backend = "claude-cli" },
            ["developer"] = new AgentBackendMapping { Backend = "copilot-acp" }
        },
        Backends = new()
        {
            ["claude-cli"] = new BackendDefinition
            {
                Command = "claude",
                IdleTimeoutSeconds = 300,
                HardTimeoutSeconds = 1800,
                MaxRetries = 2
            },
            ["copilot-acp"] = new BackendDefinition
            {
                Command = "copilot",
                IdleTimeoutSeconds = 600,
                HardTimeoutSeconds = 3600,
                MaxRetries = 1
            }
        }
    };

    private static IReadOnlyDictionary<string, ICliBackend> CreateBackends() =>
        new Dictionary<string, ICliBackend>
        {
            ["claude-cli"] = new ClaudeCliBackend(),
            ["copilot-acp"] = new CopilotAcpBackend()
        };

    [Fact]
    public void GetBackend_MappedRole_ReturnsCorrectBackend()
    {
        var registry = new BackendRegistry(CreateConfig(), CreateBackends());

        var backend = registry.GetBackend(AgentRole.SpecValidator);

        backend.Should().NotBeNull();
        backend!.BackendId.Should().Be("claude-cli");
    }

    [Fact]
    public void GetBackend_DeveloperRole_ReturnsCopilotBackend()
    {
        var registry = new BackendRegistry(CreateConfig(), CreateBackends());

        var backend = registry.GetBackend(AgentRole.Developer);

        backend.Should().NotBeNull();
        backend!.BackendId.Should().Be("copilot-acp");
    }

    [Fact]
    public void GetBackend_UnmappedRole_ReturnsNull()
    {
        var registry = new BackendRegistry(CreateConfig(), CreateBackends());

        var backend = registry.GetBackend(AgentRole.Planner);

        backend.Should().BeNull();
    }

    [Fact]
    public void GetBackend_MappedButMissingBackendInstance_ReturnsNull()
    {
        var config = new BackendConfig
        {
            AgentBackends = new()
            {
                ["specValidator"] = new AgentBackendMapping { Backend = "nonexistent" }
            }
        };
        var registry = new BackendRegistry(config, new Dictionary<string, ICliBackend>());

        var backend = registry.GetBackend(AgentRole.SpecValidator);

        backend.Should().BeNull();
    }

    [Fact]
    public void GetDefinition_MappedRole_ReturnsDefinition()
    {
        var registry = new BackendRegistry(CreateConfig(), CreateBackends());

        var def = registry.GetDefinition(AgentRole.SpecValidator);

        def.Should().NotBeNull();
        def!.Command.Should().Be("claude");
        def.MaxRetries.Should().Be(2);
        def.HardTimeoutSeconds.Should().Be(1800);
    }

    [Fact]
    public void GetDefinition_UnmappedRole_ReturnsNull()
    {
        var registry = new BackendRegistry(CreateConfig(), CreateBackends());

        var def = registry.GetDefinition(AgentRole.Architect);

        def.Should().BeNull();
    }

    // ── Factory constructor tests ──

    [Fact]
    public void FactoryConstructor_CreatesClaudeCliBackend()
    {
        var registry = new BackendRegistry(CreateConfig());

        var backend = registry.GetBackend(AgentRole.SpecValidator);

        backend.Should().NotBeNull();
        backend!.BackendId.Should().Be("claude-cli");
    }

    [Fact]
    public void FactoryConstructor_CreatesCopilotAcpBackend()
    {
        var registry = new BackendRegistry(CreateConfig());

        var backend = registry.GetBackend(AgentRole.Developer);

        backend.Should().NotBeNull();
        backend!.BackendId.Should().Be("copilot-acp");
    }

    [Fact]
    public void FactoryConstructor_UnknownBackendId_SkipsGracefully()
    {
        var config = new BackendConfig
        {
            AgentBackends = new()
            {
                ["specValidator"] = new AgentBackendMapping { Backend = "unknown-backend" }
            },
            Backends = new()
            {
                ["unknown-backend"] = new BackendDefinition { Command = "mystery" }
            }
        };
        var registry = new BackendRegistry(config);

        var backend = registry.GetBackend(AgentRole.SpecValidator);

        backend.Should().BeNull();
    }

    [Fact]
    public async Task CopilotAcpBackend_ReturnsFailure_WhenProcessUnavailable()
    {
        var backend = new CopilotAcpBackend(command: "nonexistent-copilot-binary-99999");
        var options = new CliBackendOptions { HardTimeout = TimeSpan.FromSeconds(5) };

        var response = await backend.RunPromptAsync("test", options);

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Error);
        response.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
