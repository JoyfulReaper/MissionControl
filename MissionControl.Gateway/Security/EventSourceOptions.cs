namespace MissionControl.Gateway.Security;

public sealed class EventSourceOptions
{
    public const string SectionName = "EventSources";

    public const string ApiKeyHeaderName =
        "X-Mission-Control-Key";

    public List<EventSourceRegistration> Sources { get; init; } = [];
}

public sealed class EventSourceRegistration
{
    public required string Name { get; init; }

    public required string ApiKey { get; init; }
}