namespace MissionControl.Agent.Publishing;

internal sealed class MissionControlOptions
{
    public const string SectionName = "MissionControl";

    public bool Enabled { get; init; }

    public required string BaseUrl { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public int TimeoutMilliseconds { get; init; } = 2_000;
}