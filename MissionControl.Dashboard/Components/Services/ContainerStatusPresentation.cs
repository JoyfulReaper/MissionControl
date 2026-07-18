namespace MissionControl.Dashboard.Components.Services;

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
            : containerState.ToUpperInvariant();
    }
}
