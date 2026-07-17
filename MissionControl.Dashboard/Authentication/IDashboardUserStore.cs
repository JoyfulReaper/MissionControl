namespace MissionControl.Dashboard.Authentication;

public interface IDashboardUserStore
{
    Task<DashboardUser?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);

    Task<DashboardUser> CreateAsync(
        NewDashboardUser user,
        CancellationToken cancellationToken = default);
}