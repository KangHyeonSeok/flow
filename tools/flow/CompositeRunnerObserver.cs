using FlowCore.Runner;

namespace Flow;

/// <summary>여러 IRunnerObserver에 동시에 이벤트를 전달한다.</summary>
internal sealed class CompositeRunnerObserver : IRunnerObserver, IDisposable
{
    private readonly IRunnerObserver[] _observers;

    public CompositeRunnerObserver(params IRunnerObserver[] observers)
        => _observers = observers;

    public void OnCycleStart(string runId, int candidateCount)
    { foreach (var o in _observers) o.OnCycleStart(runId, candidateCount); }

    public void OnCycleEnd(string runId, int processedCount)
    { foreach (var o in _observers) o.OnCycleEnd(runId, processedCount); }

    public void OnSpecDispatched(string specId, string title, string reason)
    { foreach (var o in _observers) o.OnSpecDispatched(specId, title, reason); }

    public void OnAgentStarted(string specId, string agentRole, string assignmentId)
    { foreach (var o in _observers) o.OnAgentStarted(specId, agentRole, assignmentId); }

    public void OnAgentCompleted(string specId, string agentRole, string result, string? summary)
    { foreach (var o in _observers) o.OnAgentCompleted(specId, agentRole, result, summary); }

    public void OnStateTransition(string specId, string fromState, string toState)
    { foreach (var o in _observers) o.OnStateTransition(specId, fromState, toState); }

    public void OnError(string specId, string message)
    { foreach (var o in _observers) o.OnError(specId, message); }

    public void OnDaemonError(Exception ex)
    { foreach (var o in _observers) o.OnDaemonError(ex); }

    public void OnDaemonStopped(int totalCycles, int totalProcessed, int totalErrors)
    { foreach (var o in _observers) o.OnDaemonStopped(totalCycles, totalProcessed, totalErrors); }

    public void Dispose()
    {
        foreach (var o in _observers)
            if (o is IDisposable d) d.Dispose();
    }
}
