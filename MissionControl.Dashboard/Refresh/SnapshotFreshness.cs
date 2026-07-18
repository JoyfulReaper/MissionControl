using MissionControl.Dashboard.Agent;

namespace MissionControl.Dashboard.Refresh;

internal static class SnapshotFreshness
{
    public static AgentSnapshotItem Apply(
        AgentSnapshotItem snapshot,
        DateTimeOffset utcNow,
        TimeSpan staleAfter)
    {
        TimeSpan age = utcNow - snapshot.CapturedAt;

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return snapshot with
        {
            AgeSeconds = (long)Math.Floor(age.TotalSeconds),
            Stale = age > staleAfter
        };
    }
}
