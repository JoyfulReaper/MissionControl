using MissionControl.Dashboard.Agent;

namespace MissionControl.Dashboard.Refresh;

internal sealed class AgentSnapshotRefreshController(
    IAgentSnapshotClient client,
    TimeProvider timeProvider,
    TimeSpan staleAfter)
{
    private readonly RefreshGate refreshGate = new();
    private AgentSnapshotItem? snapshot;

    public AgentSnapshotItem? CurrentSnapshot =>
        snapshot is null
            ? null
            : SnapshotFreshness.Apply(
                snapshot,
                timeProvider.GetUtcNow(),
                staleAfter);

    public bool IsInitialLoading { get; private set; } = true;

    public bool IsRefreshing => refreshGate.IsRunning;

    public string? RefreshWarning { get; private set; }

    public async Task<bool> RefreshAsync(
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                try
                {
                    AgentSnapshotItem refreshed =
                        await client.GetSnapshotAsync(token);

                    snapshot = refreshed;
                    RefreshWarning = null;
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                    when (exception is
                        HttpRequestException or
                        TaskCanceledException or
                        InvalidOperationException)
                {
                    RefreshWarning =
                        $"Latest Agent refresh failed: {exception.Message}";
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }
}
