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
    public async Task EndpointReturnsPreservedPublicationMetadataWithFreshMetrics()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();
        DateTimeOffset capturedAt =
            new(2026, 7, 17, 14, 0, 0, TimeSpan.Zero);
        DateTimeOffset attemptedAt =
            capturedAt.AddSeconds(10);
        var firstSnapshot = new NodeSnapshotEvent(
            Node: "node-1",
            CapturedAt: capturedAt,
            Host: new HostMetric(4, 10, 1_000, 500),
            Protocols: [],
            Containers: [],
            DockerAvailable: true,
            DockerError: null);
        NodeSnapshotEvent suppressedSnapshot =
            firstSnapshot with
            {
                CapturedAt = capturedAt.AddMinutes(1),
                Host = new HostMetric(4, 80, 1_000, 250)
            };

        await fixture.SnapshotStore.SaveAsync(
            firstSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            firstSnapshot.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            suppressedSnapshot,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await fixture.SnapshotStore.GetAsync(
                firstSnapshot.Node,
                CancellationToken.None) ??
            throw new Xunit.Sdk.XunitException(
                "Expected a stored snapshot.");
        var publicSnapshot =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    suppressedSnapshot.CapturedAt.AddSeconds(5),
                    TimeSpan.FromMinutes(1));
        string json = JsonSerializer.Serialize(
            publicSnapshot,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));
        AgentSnapshotItem? dashboardSnapshot =
            JsonSerializer.Deserialize<AgentSnapshotItem>(
                json,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));

        Assert.NotNull(dashboardSnapshot);
        Assert.Equal(
            suppressedSnapshot.CapturedAt,
            dashboardSnapshot.CapturedAt);
        Assert.Equal(80, dashboardSnapshot.Host?.CpuPercent);
        Assert.True(dashboardSnapshot.MissionControlPublishSucceeded);
        Assert.Equal(
            attemptedAt,
            dashboardSnapshot.LastMissionControlPublishAttemptAt);
    }

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

    [Fact]
    public void PublicationStatusDistinguishesActualAttemptResults()
    {
        Assert.Equal(
            "NO PUBLISH ATTEMPT RECORDED",
            PublicationStatusPresentation.GetLabel(null));
        Assert.Equal(
            "LAST ATTEMPT SUCCEEDED",
            PublicationStatusPresentation.GetLabel(true));
        string failedLabel =
            PublicationStatusPresentation.GetLabel(false);
        Assert.Equal("LAST ATTEMPT FAILED", failedLabel);
        Assert.DoesNotContain("SUCCEEDED", failedLabel);
        Assert.Equal(
            "Last publish attempt",
            PublicationStatusPresentation.AttemptTimestampLabel);
    }
}
