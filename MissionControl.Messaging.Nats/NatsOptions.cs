namespace MissionControl.Messaging.Nats;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";
    public string Url { get; init; } = "nats://localhost:4222";
    public string ClientName { get; init; } = string.Empty;
    public string StreamName { get; init; } = "MISSION_CONTROL_EVENTS";
}