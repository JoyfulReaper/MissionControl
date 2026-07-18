namespace MissionControl.Dashboard.Components.Services;

internal static class ContainerResourcePresentation
{
    public const string UnavailableMarker = "—";

    public static string FormatCpu(double? cpuPercent)
    {
        return cpuPercent is null
            ? UnavailableMarker
            : $"{cpuPercent.Value:0.##}%";
    }

    public static string FormatMemory(
        long? usageBytes,
        long? limitBytes,
        double? percent)
    {
        if (usageBytes is null &&
            limitBytes is null &&
            percent is null)
        {
            return "Unavailable";
        }

        string usage = usageBytes is null
            ? UnavailableMarker
            : FormatBytes(usageBytes.Value);

        string limit = limitBytes switch
        {
            null => UnavailableMarker,
            > 0 => FormatBytes(limitBytes.Value),
            _ => "No limit"
        };

        string percentage = percent is null
            ? UnavailableMarker
            : $"{percent.Value:0.#}%";

        return $"{usage} / {limit} ({percentage})";
    }

    public static string FormatRestartCount(int? restartCount)
    {
        return restartCount?.ToString("N0") ??
               UnavailableMarker;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
