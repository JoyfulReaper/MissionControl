namespace MissionControl.Dashboard.Events;

public sealed record DashboardLoginFailedEvent(
    string Username,
    DateTimeOffset FailedAtUtc,
    string? Remote)
{
    public const string EventType = "missioncontrol.dashboard.user.login.failed";
}
