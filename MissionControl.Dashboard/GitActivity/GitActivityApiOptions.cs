namespace MissionControl.Dashboard.GitActivity;

public sealed class GitActivityApiOptions
{
    public const string SectionName = "GitActivityApi";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}
