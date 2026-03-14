using FlowCore.Models;
using FlowCore.Runner;
using FlowCore.Storage;
using Spectre.Console;

namespace FlowConsole.Screens;

/// <summary>오픈 ReviewRequest 목록 + 응답 제출 플로우</summary>
public sealed class ReviewScreen
{
    private readonly FileFlowStore _store;
    private readonly FlowRunner _runner;

    public ReviewScreen(FileFlowStore store, FlowRunner runner)
    {
        _store = store;
        _runner = runner;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Review Requests[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Collect all open review requests
        var openRRs = await CollectOpenReviewRequestsAsync(ct);
        if (openRRs.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No open review requests.[/]");
            AnsiConsole.MarkupLine("[dim]Press any key to return...[/]");
            Console.ReadKey(intercept: true);
            return;
        }

        // Let user select a review request
        var choices = openRRs.Select(rr =>
            $"[{rr.SpecId}] {rr.Summary ?? rr.Reason ?? rr.Id}").ToList();
        choices.Add("[Back]");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a review request:")
                .PageSize(15)
                .AddChoices(choices));

        if (selected == "[Back]")
            return;

        var selectedIndex = choices.IndexOf(selected);
        var rr = openRRs[selectedIndex];

        // Show details
        ShowReviewRequestDetails(rr);

        // Collect response
        var response = CollectResponse(rr);
        if (response == null)
            return;

        // Submit
        await SubmitResponseAsync(rr, response, ct);

        AnsiConsole.MarkupLine("[dim]Press any key to return...[/]");
        Console.ReadKey(intercept: true);
    }

    private async Task<List<ReviewRequest>> CollectOpenReviewRequestsAsync(CancellationToken ct)
    {
        var result = new List<ReviewRequest>();
        try
        {
            var specs = await _store.LoadAllAsync(ct);
            var rrStore = (IReviewRequestStore)_store;

            foreach (var spec in specs)
            {
                if (spec.ReviewRequestIds is not { Count: > 0 })
                    continue;

                var requests = await rrStore.LoadBySpecAsync(spec.Id, ct);
                result.AddRange(requests.Where(rr => rr.Status == ReviewRequestStatus.Open));
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error loading review requests: {Markup.Escape(ex.Message)}[/]");
        }
        return result;
    }

    private static void ShowReviewRequestDetails(ReviewRequest rr)
    {
        AnsiConsole.WriteLine();

        var panel = new Panel(BuildDetailsMarkup(rr))
            .Header($"[bold]{Markup.Escape(rr.Id)}[/] — [dim]{Markup.Escape(rr.SpecId)}[/]")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string BuildDetailsMarkup(ReviewRequest rr)
    {
        var lines = new List<string>();

        if (rr.Summary != null)
            lines.Add($"[bold]Summary:[/] {Markup.Escape(rr.Summary)}");
        if (rr.Reason != null)
            lines.Add($"[bold]Reason:[/] {Markup.Escape(rr.Reason)}");

        if (rr.Questions is { Count: > 0 })
        {
            lines.Add("");
            lines.Add("[bold]Questions:[/]");
            foreach (var q in rr.Questions)
                lines.Add($"  • {Markup.Escape(q)}");
        }

        if (rr.Options is { Count: > 0 })
        {
            lines.Add("");
            lines.Add("[bold]Options:[/]");
            foreach (var opt in rr.Options)
            {
                var desc = opt.Description != null ? $" — {Markup.Escape(opt.Description)}" : "";
                lines.Add($"  [{Markup.Escape(opt.Id)}] {Markup.Escape(opt.Label)}{desc}");
            }
        }

        return string.Join("\n", lines);
    }

    private static ReviewResponse? CollectResponse(ReviewRequest rr)
    {
        if (rr.Options is { Count: > 0 })
        {
            // Option selection mode
            var optChoices = rr.Options.Select(o => $"{o.Label} ({o.Id})").ToList();
            optChoices.Add("[Cancel]");

            var selectedOpt = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select an option:")
                    .AddChoices(optChoices));

            if (selectedOpt == "[Cancel]")
                return null;

            var optIndex = optChoices.IndexOf(selectedOpt);
            var optionId = rr.Options[optIndex].Id;

            var comment = AnsiConsole.Prompt(
                new TextPrompt<string>("[dim]Comment (optional):[/]")
                    .AllowEmpty());

            return new ReviewResponse
            {
                RespondedBy = "console-user",
                RespondedAt = DateTimeOffset.UtcNow,
                Type = ReviewResponseType.ApproveOption,
                SelectedOptionId = optionId,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment
            };
        }
        else
        {
            // Free-text comment mode
            var comment = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold]Enter your comment:[/]"));

            if (string.IsNullOrWhiteSpace(comment))
            {
                AnsiConsole.MarkupLine("[dim]Empty comment, cancelled.[/]");
                return null;
            }

            return new ReviewResponse
            {
                RespondedBy = "console-user",
                RespondedAt = DateTimeOffset.UtcNow,
                Type = ReviewResponseType.RejectWithComment,
                Comment = comment
            };
        }
    }

    private async Task SubmitResponseAsync(ReviewRequest rr, ReviewResponse response, CancellationToken ct)
    {
        try
        {
            var success = await _runner.SubmitReviewResponseAsync(
                rr.SpecId, rr.Id, response, ct);

            if (success)
                AnsiConsole.MarkupLine("[green]✓ Response submitted successfully.[/]");
            else
                AnsiConsole.MarkupLine("[red]✗ Failed to submit response (state transition rejected).[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Error: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
