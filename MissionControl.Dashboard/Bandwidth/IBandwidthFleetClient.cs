namespace MissionControl.Dashboard.Bandwidth;

public interface IBandwidthFleetClient
{
    Task<IReadOnlyList<BandwidthNodeResult>> GetAllAsync(CancellationToken cancellationToken = default);
}
