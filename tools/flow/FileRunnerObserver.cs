using FlowCore.Runner;

namespace Flow;

/// <summary>Runner 이벤트를 파일에 기록한다.</summary>
internal sealed class FileRunnerObserver : IRunnerObserver, IDisposable
{
    private readonly StreamWriter _writer;

    public FileRunnerObserver(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public void OnCycleStart(string runId, int candidateCount)
        => Log($"[cycle] {runId} — {candidateCount} candidate(s)");

    public void OnCycleEnd(string runId, int processedCount)
        => Log($"[cycle] {runId} — processed {processedCount} spec(s)");

    public void OnSpecDispatched(string specId, string title, string reason)
        => Log($"[dispatch] {specId} \"{title}\" — {reason}");

    public void OnAgentStarted(string specId, string agentRole, string assignmentId)
        => Log($"[agent] {agentRole} started for {specId} ({assignmentId})");

    public void OnAgentCompleted(string specId, string agentRole, string result, string? summary)
        => Log($"[agent] {agentRole} → {result}{(summary != null ? $": {summary}" : "")}");

    public void OnStateTransition(string specId, string fromState, string toState)
        => Log($"[state] {specId}: {fromState} → {toState}");

    public void OnError(string specId, string message)
        => Log($"[error] {specId}: {message}");

    public void OnDaemonError(Exception ex)
        => Log($"[daemon-error] {ex.GetType().Name}: {ex.Message}");

    public void OnDaemonStopped(int totalCycles, int totalProcessed, int totalErrors)
        => Log($"[daemon] stopped — cycles: {totalCycles}, processed: {totalProcessed}, errors: {totalErrors}");

    private void Log(string message)
    {
        _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
    }

    public void Dispose() => _writer.Dispose();
}
