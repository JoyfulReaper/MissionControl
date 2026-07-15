namespace MissionControl.Agent.Protocols;

internal interface IProtocolProbe
{
    string Protocol { get; }

    Task ExecuteAsync(
        ProbeOptions options,
        CancellationToken cancellationToken);
}