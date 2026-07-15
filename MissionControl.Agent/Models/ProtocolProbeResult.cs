namespace MissionControl.Agent.Models;

public sealed record ProtocolProbeResult(
    string Service,
    string Endpoint,
    bool Succeeded,
    long DurationMilliseconds,
    string? Error);