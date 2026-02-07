using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Services;
using FlowCLI.Utils;

namespace FlowCLI.Core;

/// <summary>State definition as read from states.json.</summary>
public class StateDefinition
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("transitions")]
    public List<string> Transitions { get; set; } = [];

    [JsonPropertyName("allowed_commands")]
    public List<string> AllowedCommands { get; set; } = [];

    [JsonPropertyName("agent_instruction")]
    public string AgentInstruction { get; set; } = "";
}

/// <summary>Root object for states.json.</summary>
public class StatesConfig
{
    [JsonPropertyName("states")]
    public Dictionary<string, StateDefinition> States { get; set; } = new();
}

/// <summary>
/// Loads and caches state definitions from .flow/states.json.
/// </summary>
public class StateDefinitionLoader
{
    private readonly PathResolver _paths;
    private StatesConfig? _config;

    public StateDefinitionLoader(PathResolver paths) => _paths = paths;

    public StatesConfig Load()
    {
        if (_config != null) return _config;

        var path = _paths.StatesPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"states.json not found at {path}");

        var json = File.ReadAllText(path);
        _config = JsonSerializer.Deserialize<StatesConfig>(json, JsonOutput.Read)
            ?? throw new InvalidOperationException("Failed to parse states.json");
        return _config;
    }

    public StateDefinition? GetState(string stateName)
    {
        var config = Load();
        return config.States.TryGetValue(stateName, out var def) ? def : null;
    }
}
