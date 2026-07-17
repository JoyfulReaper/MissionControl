namespace MissionControl.Dashboard.Agent;

public sealed record AgentProtocolStatusItem(
    string Service,
    bool Succeeded,
    double DurationMilliseconds);