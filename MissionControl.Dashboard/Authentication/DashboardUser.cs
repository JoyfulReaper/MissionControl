namespace MissionControl.Dashboard.Authentication;

public sealed record DashboardUser(
    long Id,
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string PasswordHash,
    bool IsEnabled,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    string SecurityStamp,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);