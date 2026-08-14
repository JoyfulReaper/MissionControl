namespace MissionControl.Messaging.Nats;

public sealed class NatsConsumerStatus
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public void MarkRunning()
    {
        Volatile.Write(ref _isRunning, 1);
    }

    public void MarkStopped()
    {
        Volatile.Write(ref _isRunning, 0);
    }
}