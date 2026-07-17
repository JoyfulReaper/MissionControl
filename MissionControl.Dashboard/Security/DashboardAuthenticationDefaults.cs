namespace MissionControl.Dashboard.Security;

public static class DashboardAuthenticationDefaults
{
    public const string Scheme = "Dashboard";

    public const string UsernameClaim =
        "dashboard-username";

    public const string SecurityStampClaim =
        "dashboard-security-stamp";

    public const string PermissionClaim =
        "permission";

    public const string RawIpPermission =
        "events.raw-ip";
}