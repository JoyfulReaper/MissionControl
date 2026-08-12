namespace MissionControl.Messaging;

public sealed class PermanentIntegrationEventException : Exception
{
    public PermanentIntegrationEventException(string message)
        : base(message)
    {
    }

    public PermanentIntegrationEventException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}