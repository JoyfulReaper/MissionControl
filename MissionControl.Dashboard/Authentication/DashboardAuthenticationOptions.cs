namespace MissionControl.Dashboard.Authentication;

public sealed class DashboardAuthenticationOptions
{
    public const string SectionName =
        "Dashboard:Authentication";

    public string DatabaseFileName { get; set; } =
        "dashboard-auth.db";

    public string BasePath { get; set; } =
        "data";

    public string DataProtectionKeysPath { get; set; } =
        "data/data-protection";

    public int CookieLifetimeHours { get; set; } = 8;

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}