using FlowCore.Models;

namespace FlowCore.Storage;

/// <summary>Activity 저장소. append-only.</summary>
public interface IActivityStore
{
    Task AppendAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityEvent>> LoadRecentAsync(string specId, int maxCount, CancellationToken ct = default);
}
