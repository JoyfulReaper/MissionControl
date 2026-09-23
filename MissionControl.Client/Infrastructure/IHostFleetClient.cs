namespace MissionControl.Client.Infrastructure;

public interface IHostFleetClient
{
    Task<IReadOnlyList<HostNodeSnapshot>> GetAsync(
        CancellationToken cancellationToken = default);
}
