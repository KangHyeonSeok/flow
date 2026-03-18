namespace FlowCore.Runner;

/// <summary>
/// Runner 실행 중 주요 이벤트를 외부에 알린다.
/// CLI는 콘솔 출력으로, 웹서비스는 로그/SSE 등으로 구현할 수 있다.
/// </summary>
public interface IRunnerObserver
{
    void OnCycleStart(string runId, int candidateCount);
    void OnCycleEnd(string runId, int processedCount);
    void OnSpecDispatched(string specId, string title, string reason);
    void OnAgentStarted(string specId, string agentRole, string assignmentId);
    void OnAgentCompleted(string specId, string agentRole, string result, string? summary);
    void OnStateTransition(string specId, string fromState, string toState);
    void OnError(string specId, string message);
    void OnDaemonError(Exception ex) { }
    void OnDaemonStopped(int totalCycles, int totalProcessed, int totalErrors) { }
}

/// <summary>아무것도 출력하지 않는 기본 구현.</summary>
public sealed class NullRunnerObserver : IRunnerObserver
{
    public static readonly NullRunnerObserver Instance = new();
    public void OnCycleStart(string runId, int candidateCount) { }
    public void OnCycleEnd(string runId, int processedCount) { }
    public void OnSpecDispatched(string specId, string title, string reason) { }
    public void OnAgentStarted(string specId, string agentRole, string assignmentId) { }
    public void OnAgentCompleted(string specId, string agentRole, string result, string? summary) { }
    public void OnStateTransition(string specId, string fromState, string toState) { }
    public void OnError(string specId, string message) { }
}
