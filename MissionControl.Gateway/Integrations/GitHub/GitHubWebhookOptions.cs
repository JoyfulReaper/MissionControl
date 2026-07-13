namespace MissionControl.Gateway.Integrations.GitHub;

public sealed class GitHubWebhookOptions
{
    public const string SectionName = "GitHubWebhook";

    public bool Enabled { get; set; }

    public string Secret { get; set; } = string.Empty;

    public string AllowedOwner { get; set; } = "JoyfulReaper";

    public int MaxPayloadBytes { get; set; } = 5 * 1024 * 1024;
}