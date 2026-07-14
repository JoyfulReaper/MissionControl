namespace MissionControl.Gateway.Integrations.GitHub;

public sealed record GitActivityItem(
    string Repository,
    string Branch,
    string Sha,
    string Message,
    string Author,
    DateTimeOffset Timestamp,
    string Url);