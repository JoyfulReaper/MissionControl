using MissionControl.Contracts.Agent;

namespace MissionControl.Dashboard.Refresh;

internal static class SnapshotFreshness
{
    public static PublicNodeSnapshot Apply(
        PublicNodeSnapshot snapshot,
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