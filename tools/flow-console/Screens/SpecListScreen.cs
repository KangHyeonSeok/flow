using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using Spectre.Console;

namespace FlowConsole.Screens;

/// <summary>Live 자동 새로고침 스펙 목록 화면</summary>
public sealed class SpecListScreen
{
    private readonly FileFlowStore _store;
    private readonly FlowRunner _runner;

    public SpecListScreen(FileFlowStore store, FlowRunner runner)
    {
        _store = store;
        _runner = runner;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var lastRefresh = DateTime.MinValue;
        IReadOnlyList<Spec> specs = [];

        await AnsiConsole.Live(BuildTable(specs, "loading..."))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!ct.IsCancellationRequested)
                {
                    // Key handling (only when interactive console is available)
                    try
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            switch (key.Key)
                            {
                                case ConsoleKey.Q:
                                    return;
                                case ConsoleKey.R:
                                    ctx.UpdateTarget(BuildTable(specs, "opening reviews..."));
                                    ctx.Refresh();
                                    await RunReviewScreenAsync(ct);
                                    lastRefresh = DateTime.MinValue; // force refresh after review
                                    break;
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Non-interactive console — skip key handling
                    }

                    // Refresh data every 3 seconds
                    if ((DateTime.UtcNow - lastRefresh).TotalSeconds >= 3)
                    {
                        try
                        {
                            specs = await _store.LoadAllAsync(ct);
                            lastRefresh = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Error loading specs: {Markup.Escape(ex.Message)}[/]");
                        }
                    }

                    ctx.UpdateTarget(BuildTable(specs, DateTime.Now.ToString("HH:mm:ss")));
                    ctx.Refresh();
                    await Task.Delay(100, ct);
                }
            });
    }

    private async Task RunReviewScreenAsync(CancellationToken ct)
    {
        // Temporarily exit live display for interactive prompts
        AnsiConsole.Clear();
        var reviewScreen = new ReviewScreen(_store, _runner);
        await reviewScreen.RunAsync(ct);
        AnsiConsole.Clear();
    }

    private Table BuildTable(IReadOnlyList<Spec> specs, string timestamp)
    {
        var sorted = specs.OrderBy(s => GetSortPriority(s)).ThenBy(s => s.Id).ToList();

        var table = new Table()
            .Title($"[bold]Flow Console[/] — [dim]{sorted.Count} specs — {timestamp}[/]")
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]ID[/]").Width(16))
            .AddColumn(new TableColumn("[bold]Title[/]").Width(36))
            .AddColumn(new TableColumn("[bold]State[/]").Width(20))
            .AddColumn(new TableColumn("[bold]Processing[/]").Width(14))
            .AddColumn(new TableColumn("[bold]Ver[/]").Width(5));

        foreach (var spec in sorted)
        {
            table.AddRow(
                Markup.Escape(spec.Id),
                Markup.Escape(Truncate(spec.Title, 34)),
                ColorizeState(spec.State),
                ColorizeProcessing(spec.ProcessingStatus),
                spec.Version.ToString());
        }

        table.Caption("[dim][[R]] Review requests  [[Q]] Quit[/]");
        return table;
    }

    private static int GetSortPriority(Spec spec) => (spec.State, spec.ProcessingStatus) switch
    {
        (FlowState.Review, ProcessingStatus.UserReview) => 0,
        (FlowState.Failed, _) => 1,
        (FlowState.Review, _) => 2,
        (FlowState.TestGeneration, _) => 3,
        (FlowState.Implementation, _) => 4,
        (FlowState.ArchitectureReview, _) => 5,
        (FlowState.Queued, _) => 6,
        (FlowState.Draft, _) => 7,
        (FlowState.Active, _) => 8,
        (FlowState.Completed, _) => 9,
        (FlowState.Archived, _) => 10,
        _ => 99
    };

    private static string ColorizeState(FlowState state) => state switch
    {
        FlowState.Completed or FlowState.Active => $"[green]{state}[/]",
        FlowState.Review or FlowState.TestGeneration or FlowState.ArchitectureReview => $"[yellow]{state}[/]",
        FlowState.Failed => $"[red]{state}[/]",
        FlowState.Implementation => $"[blue]{state}[/]",
        FlowState.Draft or FlowState.Archived => $"[dim]{state}[/]",
        _ => state.ToString()
    };

    private static string ColorizeProcessing(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Done => $"[green]{status}[/]",
        ProcessingStatus.UserReview or ProcessingStatus.InReview => $"[yellow]{status}[/]",
        ProcessingStatus.Error => $"[red]{status}[/]",
        ProcessingStatus.InProgress => $"[blue]{status}[/]",
        ProcessingStatus.OnHold => $"[dim]{status}[/]",
        _ => status.ToString()
    };

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
