namespace MissionControl.Dashboard.Authentication;

public interface IDashboardUserStore
{
    Task<DashboardUser?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);

    Task<DashboardUser> CreateAsync(
        NewDashboardUser user,
        CancellationToken cancellationToken = default);

    Task RecordFailedLoginAsync(
        long userId,
        int maxFailedAttempts,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset newLockoutEndUtc,
        CancellationToken cancellationToken = default);

    Task RecordSuccessfulLoginAsync(
        long userId,
        string? replacementPasswordHash,
        DateTimeOffset authenticatedAtUtc,
        CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(
        long userId,
        string passwordHash,
        string securityStamp,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);
}