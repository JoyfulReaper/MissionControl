namespace MissionControl.Gateway.Messaging.RabbitMq;

public interface IMessageBrokerStatus
{
    bool IsConnected { get; }
}