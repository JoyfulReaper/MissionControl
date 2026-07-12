namespace MissionControl.Observability.RabbitMq;

public interface IRabbitMqConnectionStatus
{
    RabbitMqConnectionSnapshot GetSnapshot();
}

public readonly record struct RabbitMqConnectionSnapshot(
    bool ConnectionOpen,
    bool ChannelOpen)
{
    public bool IsConnected =>
        ConnectionOpen && ChannelOpen;
}