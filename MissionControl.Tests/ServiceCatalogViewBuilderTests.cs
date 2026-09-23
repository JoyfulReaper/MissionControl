using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Services;
using MissionControl.UI.Services;
using Xunit;

namespace MissionControl.Tests;

public sealed class ServiceCatalogViewBuilderTests
{
    [Fact]
    public void BuildForNodeOnlyUsesServicesOwnedByThatNode()
    {
        ServiceDefinition[] services =
        [
            new()
        {
            Id = "clanker-service",
            Name = "Clanker Service",
            NodeId = "clanker",
            Group = "Applications",
            Summary = "Clanker service",
            Description = "Runs on Clanker.",
            ContainerName = "shared-container",
            Visibility = "Private"
        },
        new()
        {
            Id = "scopecreep-service",
            Name = "ScopeCreep Service",
            NodeId = "scopecreep",
            Group = "Applications",
            Summary = "ScopeCreep service",
            Description = "Runs on ScopeCreep.",
            ContainerName = "shared-container",
            Visibility = "Private"
        }
        ];

        var snapshot =
            new PublicNodeSnapshot(
                Node: "scopecreep",
                CapturedAt: DateTimeOffset.UtcNow,
                AgeSeconds: 0,
                Stale: false,
                Host: null,
                MissionControlPublishSucceeded: true,
                LastMissionControlPublishAttemptAt: null,
                Protocols: [],
                Containers:
                [
                    new PublicContainerStatus(
                    "shared-container",
                    "running",
                    null,
                    null,
                    null,
                    null,
                    0)
                ],
                DockerAvailable: true)
            {
                NodeId = "scopecreep"
            };

        ServiceCatalogView view =
            ServiceCatalogViewBuilder.BuildForNode(
                services,
                "scopecreep",
                snapshot,
                filter: null);

        Assert.Equal(
            1,
            view.ConfiguredServices);

        ServiceGroupView group =
            Assert.Single(view.Groups);

        ServiceItemView item =
            Assert.Single(group.Services);

        Assert.Equal(
            "scopecreep-service",
            item.Service.Id);

        Assert.Equal(
            "shared-container",
            item.Container?.Name);
    }

    [Fact]
    public void BuildMatchesObservationsAndFindsUncataloguedItems()
    {
        ServiceDefinition[] services =
        [
            new()
            {
                Id = "api",
                Name = "Example API",
                Group = "Applications",
                Summary = "Example service",
                Description = "An example API.",
                ContainerName = "API-CONTAINER",
                ProtocolServiceKey = "api-probe",
                Visibility = "Public",
                SearchTerms = ["example", "api"]
            },
            new()
            {
                Id = "worker",
                Name = "Worker",
                Group = "Internal",
                Summary = "Background worker",
                Description = "Processes jobs.",
                ContainerName = "worker-container",
                Visibility = "Internal"
            }
        ];

        var snapshot = new PublicNodeSnapshot(
            Node: "node-1",
            CapturedAt: DateTimeOffset.UtcNow,
            AgeSeconds: 0,
            Stale: false,
            Host: null,
            MissionControlPublishSucceeded: true,
            LastMissionControlPublishAttemptAt: null,
            Protocols:
            [
                new PublicProtocolStatus(
                    "API-PROBE",
                    true,
                    12),
                new PublicProtocolStatus(
                    "orphan-probe",
                    false,
                    30)
            ],
            Containers:
            [
                new PublicContainerStatus(
                    "api-container",
                    "running",
                    null,
                    null,
                    null,
                    null,
                    0),
                new PublicContainerStatus(
                    "orphan-container",
                    "exited",
                    null,
                    null,
                    null,
                    null,
                    2)
            ],
            DockerAvailable: true);

        ServiceCatalogView view =
            ServiceCatalogViewBuilder.Build(
                services,
                snapshot,
                filter: "example");

        Assert.Equal(2, view.ConfiguredServices);
        Assert.Equal(1, view.RunningContainers);
        Assert.Equal(1, view.PublicServices);
        Assert.Equal(2, view.SnapshotContainers);
        Assert.Equal(1, view.SuccessfulProbes);
        Assert.Equal(1, view.FailedProbes);
        Assert.Equal(1, view.MatchingServices);

        ServiceGroupView group = Assert.Single(view.Groups);
        ServiceItemView item = Assert.Single(group.Services);

        Assert.Equal("Example API", item.Service.Name);
        Assert.Equal("api-container", item.Container?.Name);
        Assert.Equal("API-PROBE", item.Protocol?.Service);

        Assert.Equal(
            "orphan-container",
            Assert.Single(view.UncataloguedContainers).Name);

        Assert.Equal(
            "orphan-probe",
            Assert.Single(view.UncataloguedProtocols).Service);
    }
}