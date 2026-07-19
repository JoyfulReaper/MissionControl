namespace MissionControl.UI.Components.Services;

public static class AgentDiagnosticPresentation
{
    public const string UnavailableMarker = "—";
    public const string ConfiguredEndpointLabel = "Configured endpoint";
    public const string ObservedEndpointLabel = "Observed endpoint";
    public const string ConfiguredImageLabel = "Configured image";
    public const string ObservedImageLabel = "Observed image";
    public const string FailureReasonLabel = "Probe failure reason";
    public const string MissingFailureReason =
        "No diagnostic information is available.";

    public static string FormatOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? UnavailableMarker
            : value;
    }

    public static string FormatDuration(long durationMilliseconds)
    {
        return $"{durationMilliseconds:N0} ms";
    }

    public static string? GetFailureReason(
        bool succeeded,
        string? error)
    {
        if (succeeded)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(error)
            ? MissingFailureReason
            : error;
    }
}
