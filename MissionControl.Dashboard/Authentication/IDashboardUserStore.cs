namespace MissionControl.Dashboard.Authentication;

public interface IDashboardUserStore
{
    Task<DashboardUser?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);
}