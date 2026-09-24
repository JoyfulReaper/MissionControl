namespace MissionControl.Dashboard.GreenCloud;

public interface IGreenCloudBandwidthFleetClient
{
    Task<IReadOnlyList<GreenCloudBandwidthNodeResult>>
        GetAllAsync(CancellationToken cancellationToken = default);
}