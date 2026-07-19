using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Services;

namespace MissionControl.UI.Services;

public sealed record ServiceItemView(
    ServiceDefinition Service,
    PublicContainerStatus? Container,
    PublicProtocolStatus? Protocol);

public sealed record ServiceGroupView(
    string Name,
    IReadOnlyList<ServiceItemView> Services);

public sealed record ServiceCatalogView(
    int ConfiguredServices,
    int RunningContainers,
    int PublicServices,
    int SnapshotContainers,
    int SuccessfulProbes,
    int FailedProbes,
    int MatchingServices,
    IReadOnlyList<PublicContainerStatus> UncataloguedContainers,
    IReadOnlyList<PublicProtocolStatus> UncataloguedProtocols,
    IReadOnlyList<ServiceGroupView> Groups);