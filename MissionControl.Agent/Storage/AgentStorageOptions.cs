namespace MissionControl.Agent.Storage;

internal sealed class AgentStorageOptions
{
    public const string SectionName = "AgentStorage";

    public string DatabaseFileName { get; init; } =
        "mission-control-agent.db";

    public string? BasePath { get; init; }
}