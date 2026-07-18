namespace MissionControl.Dashboard.Agent;

public sealed record AgentProtocolStatusItem(
    string Service,
    bool Succeeded,
    long DurationMilliseconds,
    string? Endpoint = null,
    string? Error = null);
