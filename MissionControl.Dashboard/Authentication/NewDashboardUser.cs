namespace MissionControl.Dashboard.Authentication;

public sealed record NewDashboardUser(
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string PasswordHash,
    string SecurityStamp,
    DateTimeOffset CreatedAtUtc);