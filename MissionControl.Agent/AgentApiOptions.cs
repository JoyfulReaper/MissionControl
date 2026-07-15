namespace MissionControl.Agent;

public sealed class AgentApiOptions
{
    public const string SectionName = "AgentApi";

    public int StaleAfterSeconds { get; init; } = 180;

    public string[] AllowedOrigins { get; init; } =
    [
        "https://kgivler.com",
        "https://www.kgivler.com"
    ];
}