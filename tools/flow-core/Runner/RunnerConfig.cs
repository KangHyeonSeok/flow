namespace FlowCore.Runner;

public sealed class RunnerConfig
{
    public int PollIntervalSeconds { get; init; } = 30;
    public int MaxSpecsPerCycle { get; init; } = 10;
    public int DefaultTimeoutSeconds { get; init; } = 3600;
    public int DefaultReviewDeadlineSeconds { get; init; } = 86400;
    public int RetryBackoffBaseSeconds { get; init; } = 60;
    public int MaxRetries { get; init; } = 3;
}
