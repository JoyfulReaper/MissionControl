namespace MissionControl.Client.Infrastructure;

public interface IBandwidthUsageClient
{
    Task<BandwidthUsageSnapshot> GetAsync(CancellationToken cancellationToken = default);
}