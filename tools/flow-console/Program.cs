using FlowConsole.Screens;
using FlowConsole.Services;
using Spectre.Console;

var projectId = "flow"; // default

// Simple --project arg parsing
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--project" or "-p")
    {
        projectId = args[i + 1];
        break;
    }
}

try
{
    var (store, runner) = StoreFactory.Create(projectId);

    AnsiConsole.MarkupLine($"[bold]flow-console[/] — project: [cyan]{Markup.Escape(projectId)}[/]");
    AnsiConsole.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var screen = new SpecListScreen(store, runner);
    await screen.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Normal exit via Ctrl+C
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Fatal error: {Markup.Escape(ex.Message)}[/]");
    return 1;
}

return 0;
