using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Services;

namespace MissionControl.UI.Services;

public static class ServiceCatalogViewBuilder
{
    public static ServiceCatalogView Build(
        IReadOnlyList<ServiceDefinition> services,
        PublicNodeSnapshot? snapshot,
        string? filter)
    {
        ArgumentNullException.ThrowIfNull(services);

        IReadOnlyList<PublicContainerStatus> snapshotContainers =
            snapshot?.Containers ?? [];

        IReadOnlyList<PublicProtocolStatus> snapshotProtocols =
            snapshot?.Protocols ?? [];

        Dictionary<string, PublicContainerStatus> containersByName =
            snapshotContainers
                .GroupBy(
                    container => container.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, PublicProtocolStatus> protocolsByService =
            snapshotProtocols
                .GroupBy(
                    protocol => protocol.Service,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        ServiceDefinition[] filteredServices = services
            .Where(service => MatchesFilter(service, filter))
            .OrderBy(service => service.Group)
            .ThenBy(service => service.Name)
            .ToArray();

        ServiceGroupView[] groups = filteredServices
            .GroupBy(service => service.Group)
            .Select(group => new ServiceGroupView(
                group.Key,
                group
                    .Select(service => new ServiceItemView(
                        service,
                        FindContainer(service, containersByName),
                        FindProtocol(service, protocolsByService)))
                    .ToArray()))
            .ToArray();

        HashSet<string> cataloguedContainerNames = services
            .Where(service =>
                !string.IsNullOrWhiteSpace(service.ContainerName))
            .Select(service => service.ContainerName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> cataloguedProtocolKeys = services
            .Where(service =>
                !string.IsNullOrWhiteSpace(service.ProtocolServiceKey))
            .Select(service => service.ProtocolServiceKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        PublicContainerStatus[] uncataloguedContainers =
            snapshotContainers
                .Where(container =>
                    !cataloguedContainerNames.Contains(container.Name))
                .ToArray();

        PublicProtocolStatus[] uncataloguedProtocols =
            snapshotProtocols
                .Where(protocol =>
                    !cataloguedProtocolKeys.Contains(protocol.Service))
                .ToArray();

        int runningContainers = services.Count(service =>
            string.Equals(
                FindContainer(service, containersByName)?.State,
                "running",
                StringComparison.OrdinalIgnoreCase));

        return new ServiceCatalogView(
            ConfiguredServices: services.Count,
            RunningContainers: runningContainers,
            PublicServices: services.Count(service =>
                string.Equals(
                    service.Visibility,
                    "Public",
                    StringComparison.OrdinalIgnoreCase)),
            SnapshotContainers: snapshotContainers.Count,
            SuccessfulProbes:
                snapshotProtocols.Count(protocol => protocol.Succeeded),
            FailedProbes:
                snapshotProtocols.Count(protocol => !protocol.Succeeded),
            MatchingServices: filteredServices.Length,
            UncataloguedContainers: uncataloguedContainers,
            UncataloguedProtocols: uncataloguedProtocols,
            Groups: groups);
    }

    private static bool MatchesFilter(
        ServiceDefinition service,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        string normalizedFilter = filter.Trim();

        return Contains(service.Name, normalizedFilter) ||
               Contains(service.Group, normalizedFilter) ||
               Contains(service.Summary, normalizedFilter) ||
               Contains(service.Description, normalizedFilter) ||
               Contains(service.ContainerName, normalizedFilter) ||
               Contains(service.Protocol, normalizedFilter) ||
               Contains(service.ProtocolServiceKey, normalizedFilter) ||
               Contains(service.Endpoint, normalizedFilter) ||
               Contains(service.Image, normalizedFilter) ||
               service.SearchTerms.Any(term =>
                   Contains(term, normalizedFilter));
    }

    private static PublicContainerStatus? FindContainer(
        ServiceDefinition service,
        IReadOnlyDictionary<string, PublicContainerStatus> containers)
    {
        return !string.IsNullOrWhiteSpace(service.ContainerName) &&
               containers.TryGetValue(
                   service.ContainerName,
                   out PublicContainerStatus? container)
            ? container
            : null;
    }

    private static PublicProtocolStatus? FindProtocol(
        ServiceDefinition service,
        IReadOnlyDictionary<string, PublicProtocolStatus> protocols)
    {
        return !string.IsNullOrWhiteSpace(service.ProtocolServiceKey) &&
               protocols.TryGetValue(
                   service.ProtocolServiceKey,
                   out PublicProtocolStatus? protocol)
            ? protocol
            : null;
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.Contains(
            filter,
            StringComparison.OrdinalIgnoreCase) == true;
    }
}