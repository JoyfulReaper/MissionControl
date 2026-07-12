namespace MissionControl.Gateway.Security;

public interface IEventSourceResolver
{
    bool TryResolve(
        string? apiKey,
        out string source);
}