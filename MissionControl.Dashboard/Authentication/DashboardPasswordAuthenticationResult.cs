namespace MissionControl.Dashboard.Authentication;

public sealed record DashboardPasswordAuthenticationResult(
    bool Succeeded,
    DashboardUser? User)
{
    public static DashboardPasswordAuthenticationResult Failed()
    {
        return new DashboardPasswordAuthenticationResult(
            Succeeded: false,
            User: null);
    }

    public static DashboardPasswordAuthenticationResult Success(
        DashboardUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new DashboardPasswordAuthenticationResult(
            Succeeded: true,
            User: user);
    }
}