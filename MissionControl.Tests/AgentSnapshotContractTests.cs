extern alias AgentApp;
extern alias DashboardApp;

using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Storage;
using DashboardApp::MissionControl.Dashboard.Agent;
using DashboardApp::MissionControl.Dashboard.Components.Services;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentSnapshotContractTests
{
    [Fact]
    public void EndpointContractSerializesDockerUnavailabilityForDashboard()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        var stored = new StoredNodeSnapshot(
            Snapshot: new NodeSnapshotEvent(
                Node: "node-1",
                CapturedAt: capturedAt,
                Host: null,
                Protocols: [],
                Containers: [],
                DockerAvailable: false,
                DockerError:
                    "Docker metric collection is unavailable."),
            PublishSucceeded: null,
            LastPublishAttemptAt: null,
            UpdatedAt: capturedAt);

        var publicSnapshot =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    capturedAt.AddSeconds(5),
                    TimeSpan.FromMinutes(1));

        string json = JsonSerializer.Serialize(
            publicSnapshot,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));

        Assert.Contains("\"dockerAvailable\":false", json);
        Assert.Contains(
            "\"dockerError\":\"Docker metric collection is unavailable.\"",
            json);

        AgentSnapshotItem? dashboardSnapshot =
            JsonSerializer.Deserialize<AgentSnapshotItem>(
                json,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));

        Assert.NotNull(dashboardSnapshot);
        Assert.False(dashboardSnapshot.DockerAvailable);
        Assert.Equal(
            "Docker metric collection is unavailable.",
            dashboardSnapshot.DockerError);
    }

    [Fact]
    public void UnavailableDockerDoesNotPresentContainerAsMissing()
    {
        Assert.Equal(
            "UNAVAILABLE",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: false,
                containerState: null));

        Assert.Equal(
            "MISSING",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: true,
                containerState: null));

        Assert.Equal(
            "UNKNOWN",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: null,
                containerState: null));
    }
}
