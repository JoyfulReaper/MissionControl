namespace MissionControl.Agent.Contracts;

public sealed record PublicProtocolStatus(
    string Service,
    bool Succeeded,
    long DurationMilliseconds);