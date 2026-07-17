namespace MissionControl.Agent.Models;

public sealed record NodeSnapshotEvent(
    string Node,
    DateTimeOffset CapturedAt,
    HostMetric? Host,
    IReadOnlyList<ProtocolProbeResult> Protocols,
    IReadOnlyList<ContainerMetric> Containers);
