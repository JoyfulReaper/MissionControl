namespace MissionControl.UI.Components.Services;

internal static class ContainerStatusPresentation
{
    public static string GetState(
        bool isSnapshotAvailable,
        bool? dockerAvailable,
        string? containerState)
    {
        if (!isSnapshotAvailable)
        {
            return "UNKNOWN";
        }

        if (dockerAvailable == false)
        {
            return "UNAVAILABLE";
        }

        if (dockerAvailable is null)
        {
            return "UNKNOWN";
        }

        return string.IsNullOrWhiteSpace(containerState)
            ? "MISSING"
            : containerState.Trim().ToUpperInvariant();
    }

    public static string GetCssClass(string state)
    {
        return state switch
        {
            "RUNNING" => "status-running",
            "MISSING" => "status-missing",
            "EXITED" or "STOPPED" or "DEAD" or "REMOVING" =>
                "status-stopped",
            "CREATED" or "RESTARTING" or "PAUSED" =>
                "status-warning",
            "UNAVAILABLE" or "UNKNOWN" =>
                "status-unknown",
            _ => "status-warning"
        };
    }
}