
namespace MissionControl.Archive.Contracts;

public sealed record RandomSteamOverview(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long HomepageVisits,
    long IdentifiedVisitors,
    long PickerAttempts,
    long SuccessfulPicks,
    long FailedPicks,
    long CompletedExports);
