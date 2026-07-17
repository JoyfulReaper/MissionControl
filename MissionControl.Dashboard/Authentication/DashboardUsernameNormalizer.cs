namespace MissionControl.Dashboard.Authentication;

public static class DashboardUsernameNormalizer
{
    public static string Normalize(
        string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            username);

        return username
            .Trim()
            .ToUpperInvariant();
    }
}