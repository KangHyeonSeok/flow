using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;
using FlowCore.Storage;

namespace Flow.Commands;

internal static class SpecCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: flow spec <create|list|get> [options]");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var opts = ArgParser.Parse(args[1..]);

        return sub switch
        {
            "create" => await CreateAsync(opts),
            "list" => await ListAsync(opts),
            "get" => await GetAsync(opts),
            _ => Error($"Unknown subcommand: {sub}")
        };
    }

    private static async Task<int> CreateAsync(Dictionary<string, string> opts)
    {
        var projectId = opts.Get("project");
        var title = opts.Get("title");

        if (projectId == null || title == null)
        {
            Console.WriteLine("Required: --project <id> --title <title>");
            return 1;
        }

        var store = new FileFlowStore(projectId);
        var specId = opts.Get("id") ?? await GenerateNextId(store);

        var typeStr = opts.Get("type")?.ToLowerInvariant();
        var specType = typeStr switch
        {
            "feature" => SpecType.Feature,
            "task" => SpecType.Task,
            _ => SpecType.Feature
        };

        var now = DateTimeOffset.UtcNow;
        var spec = new Spec
        {
            Id = specId,
            ProjectId = projectId,
            Title = title,
            Type = specType,
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            Problem = opts.Get("problem"),
            Goal = opts.Get("goal"),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        var acTexts = opts.GetAll("ac");
        if (acTexts.Count > 0)
        {
            spec.AcceptanceCriteria = acTexts.Select((text, i) => new AcceptanceCriterion
            {
                Id = $"AC-{i + 1:D3}",
                Text = text,
                Testable = true
            }).ToList();
        }

        var result = await store.SaveAsync(spec, 0);
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Failed to save spec: {result.Status} - {result.Message}");
            return 1;
        }

        Console.WriteLine($"Created: {specId} \"{title}\" ({specType}, Draft)");
        return 0;
    }

    private static async Task<int> ListAsync(Dictionary<string, string> opts)
    {
        var projectId = opts.Get("project");
        if (projectId == null)
        {
            Console.WriteLine("Required: --project <id>");
            return 1;
        }

        var store = new FileFlowStore(projectId);
        var specs = await store.LoadAllAsync();
        var statusFilter = opts.Get("status")?.ToLowerInvariant();

        foreach (var spec in specs.OrderBy(s => s.Id))
        {
            if (statusFilter != null &&
                !spec.State.ToString().Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine($"  {spec.Id,-12} {spec.State,-18} {spec.Title}");
        }

        if (!specs.Any())
            Console.WriteLine("  (no specs)");

        return 0;
    }

    private static async Task<int> GetAsync(Dictionary<string, string> opts)
    {
        var projectId = opts.Get("project");
        // positional arg: spec id
        var specId = opts.Get("_1");

        if (projectId == null || specId == null)
        {
            Console.WriteLine("Usage: flow spec get --project <id> <spec-id>");
            return 1;
        }

        var store = new FileFlowStore(projectId);
        var spec = await store.LoadAsync(specId);
        if (spec == null)
        {
            Console.WriteLine($"Spec not found: {specId}");
            return 1;
        }

        var json = SpecPruner.Serialize(spec);
        Console.WriteLine(json);
        return 0;
    }

    private static async Task<string> GenerateNextId(FileFlowStore store)
    {
        var specs = await store.LoadAllAsync();
        var maxNum = 0;
        foreach (var s in specs)
        {
            if (s.Id.StartsWith("F-") && int.TryParse(s.Id[2..], out var n) && n > maxNum)
                maxNum = n;
        }
        return $"F-{maxNum + 1:D3}";
    }

    private static int Error(string msg)
    {
        Console.WriteLine(msg);
        return 1;
    }
}
