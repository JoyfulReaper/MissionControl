using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.Configuration;

internal sealed class DashboardRefreshOptionsValidator :
    IValidateOptions<DashboardRefreshOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DashboardRefreshOptions options)
    {
        List<string> failures = [];

        if (options.AgentSnapshotRefreshSeconds is < 5 or > 3600)
        {
            failures.Add(
                "Dashboard Agent snapshot refresh interval must be between 5 and 3600 seconds.");
        }

        if (options.EventRefreshSeconds is < 5 or > 3600)
        {
            failures.Add(
                "Dashboard event refresh interval must be between 5 and 3600 seconds.");
        }

        if (options.SnapshotStaleAfterSeconds is < 5 or > 86400)
        {
            failures.Add(
                "Dashboard snapshot stale threshold must be between 5 and 86400 seconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
