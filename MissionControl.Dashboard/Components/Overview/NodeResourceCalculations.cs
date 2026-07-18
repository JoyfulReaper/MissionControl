using MissionControl.Dashboard.Agent;

namespace MissionControl.Dashboard.Components.Overview;

internal static class NodeResourceCalculations
{
    public const string UnavailableMessage =
        "Host resource metrics are unavailable for this snapshot.";

    public static double? GetCpuPercent(HostMetricItem? host)
    {
        return host?.CpuPercent;
    }

    public static bool HasMemoryMetrics(HostMetricItem? host)
    {
        return host?.MemoryTotalBytes is > 0 &&
               host.MemoryAvailableBytes is >= 0;
    }

    public static bool HasResourceMetrics(HostMetricItem? host)
    {
        return GetCpuPercent(host) is not null ||
               HasMemoryMetrics(host);
    }

    public static long GetMemoryUsedBytes(HostMetricItem? host)
    {
        return host?.MemoryTotalBytes is not null &&
               host.MemoryAvailableBytes is not null
            ? Math.Max(
                0,
                host.MemoryTotalBytes.Value -
                host.MemoryAvailableBytes.Value)
            : 0;
    }

    public static double GetMemoryPercent(HostMetricItem? host)
    {
        return host?.MemoryTotalBytes is > 0
            ? (double)GetMemoryUsedBytes(host) /
              host.MemoryTotalBytes.Value * 100.0
            : 0;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
