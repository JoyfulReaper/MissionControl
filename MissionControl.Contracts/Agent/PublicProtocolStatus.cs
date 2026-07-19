namespace MissionControl.Contracts.Agent;

public sealed record PublicProtocolStatus(
    string Service,
    bool Succeeded,
    long DurationMilliseconds,
    string? Endpoint = null,
    string? Error = null);