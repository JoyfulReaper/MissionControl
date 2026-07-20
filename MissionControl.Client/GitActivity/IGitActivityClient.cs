using MissionControl.Contracts.GitActivity;

namespace MissionControl.Client.GitActivity;

public interface IGitActivityClient
{
    Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
        int? limit = null,
        CancellationToken cancellationToken = default);
}
