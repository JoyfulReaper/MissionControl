namespace MissionControl.Agent.Models;

public sealed record NodeSnapshotEvent(
    string Node,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ProtocolProbeResult> Protocols,
    IReadOnlyList<ContainerMetric> Containers);