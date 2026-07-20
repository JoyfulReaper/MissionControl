using MissionControl.Contracts.Agent;

namespace MissionControl.UI.Components.Overview;

public static class NodeResourceCalculations
{
    public const string UnavailableMessage =
        "Host resource metrics are unavailable for this snapshot.";

    public static double? GetCpuPercent(PublicHostMetric? host)
    {
        return host?.CpuPercent;
    }

    public static bool HasMemoryMetrics(PublicHostMetric? host)
    {
        return host?.MemoryTotalBytes is > 0 &&
               host.MemoryAvailableBytes is >= 0;
    }

    public static bool HasLoadAverageMetrics(PublicHostMetric? host)
    {
        return host?.LoadAverage1Minute is >= 0 &&
               host.LoadAverage5Minutes is >= 0 &&
               host.LoadAverage15Minutes is >= 0;
    }

    public static bool HasResourceMetrics(PublicHostMetric? host)
    {
        return GetCpuPercent(host) is not null ||
               HasMemoryMetrics(host) ||
               HasLoadAverageMetrics(host);
    }

    public static long GetMemoryUsedBytes(PublicHostMetric? host)
    {
        return host?.MemoryTotalBytes is not null &&
               host.MemoryAvailableBytes is not null
            ? Math.Max(
                0,
                host.MemoryTotalBytes.Value -
                host.MemoryAvailableBytes.Value)
            : 0;
    }

    public static double GetMemoryPercent(PublicHostMetric? host)
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
