using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Agent;
using MissionControl.Dashboard.Services;

namespace MissionControl.Dashboard.Components.Pages;

public partial class Services
{
    private AgentSnapshotItem? _snapshot;
    private bool _isLoading = true;
    private string? _snapshotError;
    private string? _filter;

    [Inject]
    private IAgentSnapshotClient AgentClient { get; set; } = null!;

    [Inject]
    private IOptions<ServiceCatalogOptions> CatalogOptions { get; set; } =
        null!;

    protected override Task OnInitializedAsync()
    {
        return LoadSnapshotAsync();
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        _snapshotError = null;

        await LoadSnapshotAsync();
    }

    private async Task LoadSnapshotAsync()
    {
        try
        {
            _snapshot = await AgentClient.GetSnapshotAsync();
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                TaskCanceledException or
                InvalidOperationException)
        {
            _snapshotError = exception.Message;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private ServicesView CreateView()
    {
        IReadOnlyList<ServiceDefinition> services =
            CatalogOptions.Value.Services;

        Dictionary<string, AgentContainerStatusItem> containersByName =
            (_snapshot?.Containers ?? [])
                .GroupBy(
                    container => container.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, AgentProtocolStatusItem> protocolsByService =
            (_snapshot?.Protocols ?? [])
                .GroupBy(
                    protocol => protocol.Service,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        ServiceDefinition[] filteredServices = services
            .Where(MatchesFilter)
            .OrderBy(service => service.Group)
            .ThenBy(service => service.Name)
            .ToArray();

        ServiceGroupView[] groups = filteredServices
            .GroupBy(service => service.Group)
            .Select(group => new ServiceGroupView(
                group.Key,
                group.Select(service => new ServiceItemView(
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

        AgentContainerStatusItem[] uncataloguedContainers =
            (_snapshot?.Containers ?? [])
                .Where(container =>
                    !cataloguedContainerNames.Contains(container.Name))
                .ToArray();

        AgentProtocolStatusItem[] uncataloguedProtocols =
            (_snapshot?.Protocols ?? [])
                .Where(protocol =>
                    !cataloguedProtocolKeys.Contains(protocol.Service))
                .ToArray();

        int runningContainers = services.Count(service =>
            string.Equals(
                FindContainer(service, containersByName)?.State,
                "running",
                StringComparison.OrdinalIgnoreCase));

        return new ServicesView(
            services.Count,
            runningContainers,
            services.Count(service =>
                string.Equals(
                    service.Visibility,
                    "Public",
                    StringComparison.OrdinalIgnoreCase)),
            _snapshot?.Containers.Count ?? 0,
            _snapshot?.Protocols.Count(protocol => protocol.Succeeded) ?? 0,
            _snapshot?.Protocols.Count(protocol => !protocol.Succeeded) ?? 0,
            filteredServices.Length,
            uncataloguedContainers,
            uncataloguedProtocols,
            groups);
    }

    private bool MatchesFilter(ServiceDefinition service)
    {
        if (string.IsNullOrWhiteSpace(_filter))
        {
            return true;
        }

        string filter = _filter.Trim();

        return Contains(service.Name, filter) ||
               Contains(service.Group, filter) ||
               Contains(service.Summary, filter) ||
               Contains(service.Description, filter) ||
               Contains(service.ContainerName, filter) ||
               Contains(service.Protocol, filter) ||
               Contains(service.ProtocolServiceKey, filter) ||
               Contains(service.Endpoint, filter) ||
               Contains(service.Image, filter) ||
               service.SearchTerms.Any(term => Contains(term, filter));
    }

    private static AgentContainerStatusItem? FindContainer(
        ServiceDefinition service,
        IReadOnlyDictionary<string, AgentContainerStatusItem> containers)
    {
        return !string.IsNullOrWhiteSpace(service.ContainerName) &&
               containers.TryGetValue(service.ContainerName, out var container)
            ? container
            : null;
    }

    private static AgentProtocolStatusItem? FindProtocol(
        ServiceDefinition service,
        IReadOnlyDictionary<string, AgentProtocolStatusItem> protocols)
    {
        return !string.IsNullOrWhiteSpace(service.ProtocolServiceKey) &&
               protocols.TryGetValue(service.ProtocolServiceKey, out var protocol)
            ? protocol
            : null;
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.Contains(
            filter,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record ServiceItemView(
        ServiceDefinition Service,
        AgentContainerStatusItem? Container,
        AgentProtocolStatusItem? Protocol);

    private sealed record ServiceGroupView(
        string Name,
        IReadOnlyList<ServiceItemView> Services);

    private sealed record ServicesView(
        int ConfiguredServices,
        int RunningContainers,
        int PublicServices,
        int SnapshotContainers,
        int SuccessfulProbes,
        int FailedProbes,
        int MatchingServices,
        IReadOnlyList<AgentContainerStatusItem> UncataloguedContainers,
        IReadOnlyList<AgentProtocolStatusItem> UncataloguedProtocols,
        IReadOnlyList<ServiceGroupView> Groups);
}
