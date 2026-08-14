namespace MissionControl.Messaging.Nats;

public sealed class NatsConsumerOptions
{
    public const string SectionName = "NatsConsumer";
    public string DurableName { get; init; } = string.Empty;
    public string FilterSubject { get; init; } = string.Empty;
    public int MaxDeliveries { get; init; } = 2;
}