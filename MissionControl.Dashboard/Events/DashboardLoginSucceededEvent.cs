namespace MissionControl.Dashboard.Events;

public sealed record DashboardLoginSucceededEvent(
    long UserId,
    string Username,
    string DisplayName,
    DateTimeOffset AuthenticatedAtUtc,
    string? Remote)
{
    public const string EventType = "missioncontrol.dashboard.user.login.succeeded";
}