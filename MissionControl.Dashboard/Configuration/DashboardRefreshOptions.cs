namespace MissionControl.Dashboard.Configuration;

public sealed class DashboardRefreshOptions
{
    public const string SectionName = "Dashboard:Refresh";

    public int AgentSnapshotRefreshSeconds { get; init; } = 30;

    public int EventRefreshSeconds { get; init; } = 30;

    public int SnapshotStaleAfterSeconds { get; init; } = 120;
}
