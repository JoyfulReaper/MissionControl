using MissionControl.Agent.Models;

namespace MissionControl.Agent.Publishing;

internal sealed class SnapshotPublicationGate
{
    private readonly TimeSpan _heartbeatInterval;
    private SnapshotFingerprint? _lastPublishedFingerprint;
    private DateTimeOffset? _lastPublishedAt;

    public SnapshotPublicationGate(
        TimeSpan heartbeatInterval)
    {
        if (heartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval),
                heartbeatInterval,
                "Heartbeat interval must be positive.");
        }

        _heartbeatInterval = heartbeatInterval;
    }

    public bool IsDue(
        NodeSnapshotEvent snapshot,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_lastPublishedFingerprint is null ||
            _lastPublishedAt is null)
        {
            return true;
        }

        // Be conservative if the system clock moves backward.
        if (now < _lastPublishedAt.Value)
        {
            return true;
        }

        SnapshotFingerprint currentFingerprint =
            SnapshotFingerprint.Create(snapshot);

        if (currentFingerprint != _lastPublishedFingerprint)
        {
            return true;
        }

        return now - _lastPublishedAt.Value >=
            _heartbeatInterval;
    }

    public void MarkPublished(
        NodeSnapshotEvent snapshot,
        DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _lastPublishedFingerprint =
            SnapshotFingerprint.Create(snapshot);

        _lastPublishedAt = publishedAt;
    }

    private sealed record SnapshotFingerprint(
        string Node,
        bool? DockerAvailable,
        ProtocolFingerprint[] Protocols,
        ContainerFingerprint[] Containers)
    {
        public static SnapshotFingerprint Create(
            NodeSnapshotEvent snapshot)
        {
            ProtocolFingerprint[] protocols =
                snapshot.Protocols
                    .Select(
                        protocol =>
                            new ProtocolFingerprint(
                                protocol.Service,
                                protocol.Endpoint,
                                protocol.Succeeded))
                    .OrderBy(
                        protocol => protocol.Service,
                        StringComparer.Ordinal)
                    .ThenBy(
                        protocol => protocol.Endpoint,
                        StringComparer.Ordinal)
                    .ToArray();

            ContainerFingerprint[] containers =
                snapshot.Containers
                    .Select(
                        container =>
                            new ContainerFingerprint(
                                container.Name,
                                container.Image,
                                container.State,
                                container.RestartCount))
                    .OrderBy(
                        container => container.Name,
                        StringComparer.Ordinal)
                    .ToArray();

            return new SnapshotFingerprint(
                snapshot.Node,
                snapshot.DockerAvailable,
                protocols,
                containers);
        }

        public bool Equals(
            SnapshotFingerprint? other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(
                       Node,
                       other.Node,
                       StringComparison.Ordinal) &&
                   DockerAvailable == other.DockerAvailable &&
                   Protocols.SequenceEqual(
                       other.Protocols) &&
                   Containers.SequenceEqual(
                       other.Containers);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();

            hash.Add(
                Node,
                StringComparer.Ordinal);

            hash.Add(DockerAvailable);

            foreach (ProtocolFingerprint protocol in Protocols)
            {
                hash.Add(protocol);
            }

            foreach (ContainerFingerprint container in Containers)
            {
                hash.Add(container);
            }

            return hash.ToHashCode();
        }
    }

    private sealed record ProtocolFingerprint(
        string Service,
        string Endpoint,
        bool Succeeded);

    private sealed record ContainerFingerprint(
        string Name,
        string Image,
        string State,
        int RestartCount);
}
