using FlowCore.Runner;

namespace Flow;

/// <summary>Runner 이벤트를 콘솔에 실시간 출력한다.</summary>
internal sealed class ConsoleRunnerObserver : IRunnerObserver
{
    public void OnCycleStart(string runId, int candidateCount)
    {
        if (candidateCount > 0)
            Write($"[cycle] {runId} — {candidateCount} candidate(s)", ConsoleColor.DarkGray);
    }

    public void OnCycleEnd(string runId, int processedCount)
    {
        if (processedCount > 0)
            Write($"[cycle] {runId} — processed {processedCount} spec(s)", ConsoleColor.DarkGray);
    }

    public void OnSpecDispatched(string specId, string title, string reason)
    {
        Write($"[dispatch] {specId} \"{title}\" — {reason}", ConsoleColor.Cyan);
    }

    public void OnAgentStarted(string specId, string agentRole, string assignmentId)
    {
        Write($"[agent] {agentRole} started for {specId}", ConsoleColor.Yellow);
    }

    public void OnAgentCompleted(string specId, string agentRole, string result, string? summary)
    {
        var color = result == "Success" ? ConsoleColor.Green : ConsoleColor.Red;
        var msg = $"[agent] {agentRole} → {result}";
        if (summary != null) msg += $": {summary}";
        Write(msg, color);
    }

    public void OnStateTransition(string specId, string fromState, string toState)
    {
        Write($"[state] {specId}: {fromState} → {toState}", ConsoleColor.Magenta);
    }

    public void OnError(string specId, string message)
    {
        Write($"[error] {specId}: {message}", ConsoleColor.Red);
    }

    public void OnDaemonError(Exception ex)
    {
        Write($"[daemon-error] {ex.GetType().Name}: {ex.Message}", ConsoleColor.Red);
    }

    public void OnDaemonStopped(int totalCycles, int totalProcessed, int totalErrors)
    {
        Write($"[daemon] stopped — cycles: {totalCycles}, processed: {totalProcessed}, errors: {totalErrors}",
            totalErrors > 0 ? ConsoleColor.Yellow : ConsoleColor.DarkGray);
    }

    private static void Write(string message, ConsoleColor color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{timestamp} ");
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
        Console.Out.Flush();
    }
}
