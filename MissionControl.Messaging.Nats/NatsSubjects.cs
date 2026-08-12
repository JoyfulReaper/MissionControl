namespace MissionControl.Messaging.Nats;

public static class NatsSubjects
{
    public const string EventPrefix = "events";
    public const string AllEvents = EventPrefix + ".>";

    public static string ForEventType(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        if (eventType.Contains('*') ||
            eventType.Contains('>') ||
            eventType.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Event type contains characters that are invalid in a NATS subject.",
                nameof(eventType));
        }

        return EventPrefix + "." + eventType;
    }
}