extern alias AgentApp;
extern alias DashboardApp;

using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Contracts;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Storage;
using DashboardApp::MissionControl.Dashboard.Agent;
using DashboardApp::MissionControl.Dashboard.Components.Services;
using System.Net.Http.Json;
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
        const long totalMemoryBytes = 17_179_869_184;
        const long availableMemoryBytes = 6_442_450_944;
        var firstSnapshot = new NodeSnapshotEvent(
            Node: "node-1",
            CapturedAt: capturedAt,
            Host: new HostMetric(12, 25.25, totalMemoryBytes, 8_589_934_592),
            Protocols:
            [
                new ProtocolProbeResult(
                    "echo",
                    "localhost:7",
                    true,
                    123,
                    null),
                new ProtocolProbeResult(
                    "qotd",
                    "localhost:17",
                    false,
                    456,
                    "Connection refused")
            ],
            Containers:
            [
                new ContainerMetric(
                    "missioncontrol-agent",
                    "missioncontrol/agent:3.0",
                    "running",
                    987_654_321,
                    2_147_483_648,
                    45.99,
                    12.75,
                    3)
            ],
            DockerAvailable: true,
            DockerError: null);
        NodeSnapshotEvent suppressedSnapshot =
            firstSnapshot with
            {
                CapturedAt = capturedAt.AddMinutes(1),
                Host = new HostMetric(
                    12,
                    37.5,
                    totalMemoryBytes,
                    availableMemoryBytes)
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
        Assert.Equal("node-1", publicSnapshot.Node);
        Assert.Equal(
            suppressedSnapshot.CapturedAt,
            publicSnapshot.CapturedAt);
        Assert.Equal(5, publicSnapshot.AgeSeconds);
        Assert.False(publicSnapshot.Stale);
        Assert.Equal(12, publicSnapshot.Host?.LogicalProcessorCount);
        Assert.Equal(37.5, publicSnapshot.Host?.CpuPercent);
        Assert.Equal(
            totalMemoryBytes,
            publicSnapshot.Host?.MemoryTotalBytes);
        Assert.Equal(
            availableMemoryBytes,
            publicSnapshot.Host?.MemoryAvailableBytes);
        Assert.True(publicSnapshot.DockerAvailable);
        PublicContainerStatus publicContainer =
            Assert.Single(publicSnapshot.Containers);
        Assert.Equal("missioncontrol-agent", publicContainer.Name);
        Assert.Equal("running", publicContainer.State);
        Assert.Equal(987_654_321, publicContainer.MemoryUsageBytes);
        Assert.Equal(2_147_483_648, publicContainer.MemoryLimitBytes);
        Assert.Equal(45.99, publicContainer.MemoryPercent);
        Assert.Equal(12.75, publicContainer.CpuPercent);
        Assert.Equal(3, publicContainer.RestartCount);
        Assert.Equal(
            "missioncontrol/agent:3.0",
            publicContainer.Image);
        Assert.Collection(
            publicSnapshot.Protocols,
            protocol =>
            {
                Assert.Equal("echo", protocol.Service);
                Assert.True(protocol.Succeeded);
                Assert.Equal(123, protocol.DurationMilliseconds);
                Assert.Equal("localhost:7", protocol.Endpoint);
                Assert.Null(protocol.Error);
            },
            protocol =>
            {
                Assert.Equal("qotd", protocol.Service);
                Assert.False(protocol.Succeeded);
                Assert.Equal(456, protocol.DurationMilliseconds);
                Assert.Equal("localhost:17", protocol.Endpoint);
                Assert.Equal("Connection refused", protocol.Error);
            });
        Assert.True(publicSnapshot.MissionControlPublishSucceeded);
        Assert.Equal(
            attemptedAt,
            publicSnapshot.LastMissionControlPublishAttemptAt);

        using JsonContent content =
            JsonContent.Create(publicSnapshot);
        AgentSnapshotItem? dashboardSnapshot =
            await content.ReadFromJsonAsync<AgentSnapshotItem>();

        Assert.NotNull(dashboardSnapshot);
        Assert.Equal(
            suppressedSnapshot.CapturedAt,
            dashboardSnapshot.CapturedAt);
        Assert.Equal(12, dashboardSnapshot.Host?.LogicalProcessorCount);
        Assert.Equal(37.5, dashboardSnapshot.Host?.CpuPercent);
        Assert.Equal(
            totalMemoryBytes,
            dashboardSnapshot.Host?.MemoryTotalBytes);
        Assert.Equal(
            availableMemoryBytes,
            dashboardSnapshot.Host?.MemoryAvailableBytes);
        Assert.True(dashboardSnapshot.DockerAvailable);
        Assert.True(dashboardSnapshot.MissionControlPublishSucceeded);
        Assert.Equal(
            attemptedAt,
            dashboardSnapshot.LastMissionControlPublishAttemptAt);
        AgentContainerStatusItem dashboardContainer =
            Assert.Single(dashboardSnapshot.Containers);
        Assert.Equal(987_654_321, dashboardContainer.MemoryUsageBytes);
        Assert.Equal(2_147_483_648, dashboardContainer.MemoryLimitBytes);
        Assert.Equal(45.99, dashboardContainer.MemoryPercent);
        Assert.Equal(12.75, dashboardContainer.CpuPercent);
        Assert.Equal(
            "missioncontrol/agent:3.0",
            dashboardContainer.Image);
        Assert.Collection(
            dashboardSnapshot.Protocols,
            protocol =>
            {
                Assert.Equal("echo", protocol.Service);
                Assert.True(protocol.Succeeded);
                Assert.Equal(123, protocol.DurationMilliseconds);
                Assert.Equal("localhost:7", protocol.Endpoint);
                Assert.Null(protocol.Error);
            },
            protocol =>
            {
                Assert.Equal("qotd", protocol.Service);
                Assert.False(protocol.Succeeded);
                Assert.Equal(456, protocol.DurationMilliseconds);
                Assert.Equal("localhost:17", protocol.Endpoint);
                Assert.Equal("Connection refused", protocol.Error);
            });
    }

    [Fact]
    public void CriticalAgentAndDashboardContractTypesRemainCompatible()
    {
        Assert.Equal(
            typeof(double?),
            typeof(PublicHostMetric)
                .GetProperty(nameof(PublicHostMetric.CpuPercent))!
                .PropertyType);
        Assert.Equal(
            typeof(double?),
            typeof(HostMetricItem)
                .GetProperty(nameof(HostMetricItem.CpuPercent))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(PublicHostMetric)
                .GetProperty(nameof(PublicHostMetric.MemoryTotalBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(HostMetricItem)
                .GetProperty(nameof(HostMetricItem.MemoryTotalBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(PublicHostMetric)
                .GetProperty(nameof(PublicHostMetric.MemoryAvailableBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(HostMetricItem)
                .GetProperty(nameof(HostMetricItem.MemoryAvailableBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(DateTimeOffset),
            typeof(PublicNodeSnapshot)
                .GetProperty(nameof(PublicNodeSnapshot.CapturedAt))!
                .PropertyType);
        Assert.Equal(
            typeof(DateTimeOffset?),
            typeof(PublicNodeSnapshot)
                .GetProperty(
                    nameof(PublicNodeSnapshot.LastMissionControlPublishAttemptAt))!
                .PropertyType);
        Assert.Equal(
            typeof(DateTimeOffset?),
            typeof(AgentSnapshotItem)
                .GetProperty(
                    nameof(AgentSnapshotItem.LastMissionControlPublishAttemptAt))!
                .PropertyType);
        Assert.Equal(
            typeof(bool?),
            typeof(PublicNodeSnapshot)
                .GetProperty(nameof(PublicNodeSnapshot.DockerAvailable))!
                .PropertyType);
        Assert.Equal(
            typeof(bool?),
            typeof(AgentSnapshotItem)
                .GetProperty(nameof(AgentSnapshotItem.DockerAvailable))!
                .PropertyType);
        Assert.Equal(
            typeof(bool?),
            typeof(PublicNodeSnapshot)
                .GetProperty(
                    nameof(PublicNodeSnapshot.MissionControlPublishSucceeded))!
                .PropertyType);
        Assert.Equal(
            typeof(bool?),
            typeof(AgentSnapshotItem)
                .GetProperty(
                    nameof(AgentSnapshotItem.MissionControlPublishSucceeded))!
                .PropertyType);
        Assert.Equal(
            typeof(long),
            typeof(PublicProtocolStatus)
                .GetProperty(nameof(PublicProtocolStatus.DurationMilliseconds))!
                .PropertyType);
        Assert.Equal(
            typeof(long),
            typeof(AgentProtocolStatusItem)
                .GetProperty(nameof(AgentProtocolStatusItem.DurationMilliseconds))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(PublicProtocolStatus)
                .GetProperty(nameof(PublicProtocolStatus.Endpoint))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(AgentProtocolStatusItem)
                .GetProperty(nameof(AgentProtocolStatusItem.Endpoint))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(PublicProtocolStatus)
                .GetProperty(nameof(PublicProtocolStatus.Error))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(AgentProtocolStatusItem)
                .GetProperty(nameof(AgentProtocolStatusItem.Error))!
                .PropertyType);
        Assert.Equal(
            typeof(double?),
            typeof(PublicContainerStatus)
                .GetProperty(nameof(PublicContainerStatus.CpuPercent))!
                .PropertyType);
        Assert.Equal(
            typeof(double?),
            typeof(AgentContainerStatusItem)
                .GetProperty(nameof(AgentContainerStatusItem.CpuPercent))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(PublicContainerStatus)
                .GetProperty(nameof(PublicContainerStatus.MemoryUsageBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(long?),
            typeof(AgentContainerStatusItem)
                .GetProperty(nameof(AgentContainerStatusItem.MemoryUsageBytes))!
                .PropertyType);
        Assert.Equal(
            typeof(int?),
            typeof(PublicContainerStatus)
                .GetProperty(nameof(PublicContainerStatus.RestartCount))!
                .PropertyType);
        Assert.Equal(
            typeof(int?),
            typeof(AgentContainerStatusItem)
                .GetProperty(nameof(AgentContainerStatusItem.RestartCount))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(PublicContainerStatus)
                .GetProperty(nameof(PublicContainerStatus.Image))!
                .PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(AgentContainerStatusItem)
                .GetProperty(nameof(AgentContainerStatusItem.Image))!
                .PropertyType);
    }

    [Fact]
    public void NonRunningContainerSerializesThroughAgentAndDashboardContracts()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        var stored = new StoredNodeSnapshot(
            Snapshot: new NodeSnapshotEvent(
                Node: "node-1",
                CapturedAt: capturedAt,
                Host: null,
                Protocols: [],
                Containers:
                [
                    new ContainerMetric(
                        Name: "worker",
                        Image: "private/internal-image:1",
                        State: "exited",
                        MemoryUsageBytes: null,
                        MemoryLimitBytes: null,
                        MemoryPercent: null,
                        CpuPercent: null,
                        RestartCount: 6)
                ],
                DockerAvailable: true,
                DockerError: null),
            PublishSucceeded: true,
            LastPublishAttemptAt: capturedAt,
            UpdatedAt: capturedAt);

        PublicNodeSnapshot publicSnapshot =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    capturedAt,
                    TimeSpan.FromMinutes(1));
        PublicContainerStatus publicContainer =
            Assert.Single(publicSnapshot.Containers);

        Assert.Equal("worker", publicContainer.Name);
        Assert.Equal("exited", publicContainer.State);
        Assert.Null(publicContainer.MemoryUsageBytes);
        Assert.Null(publicContainer.MemoryLimitBytes);
        Assert.Null(publicContainer.MemoryPercent);
        Assert.Null(publicContainer.CpuPercent);
        Assert.Equal(6, publicContainer.RestartCount);
        Assert.Equal(
            "private/internal-image:1",
            publicContainer.Image);
        Assert.True(publicSnapshot.DockerAvailable);

        string json = JsonSerializer.Serialize(
            publicSnapshot,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));
        Assert.Contains(
            "\"image\":\"private/internal-image:1\"",
            json);

        AgentSnapshotItem? dashboardSnapshot =
            JsonSerializer.Deserialize<AgentSnapshotItem>(
                json,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));
        AgentContainerStatusItem dashboardContainer =
            Assert.Single(
                Assert.IsType<AgentSnapshotItem>(
                    dashboardSnapshot).Containers);

        Assert.Equal("exited", dashboardContainer.State);
        Assert.Null(dashboardContainer.MemoryUsageBytes);
        Assert.Null(dashboardContainer.CpuPercent);
        Assert.Equal(6, dashboardContainer.RestartCount);
        Assert.Equal(
            "private/internal-image:1",
            dashboardContainer.Image);
    }

    [Fact]
    public void LargeLongDurationAndDiagnosticFieldsSurviveJsonRoundTrip()
    {
        const long duration = 9_007_199_254_740_993;
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        var stored = new StoredNodeSnapshot(
            Snapshot: new NodeSnapshotEvent(
                Node: "node-1",
                CapturedAt: capturedAt,
                Host: null,
                Protocols:
                [
                    new ProtocolProbeResult(
                        "qotd",
                        "example.internal:17",
                        false,
                        duration,
                        "SocketException: Connection refused")
                ],
                Containers: [],
                DockerAvailable: true,
                DockerError: null),
            PublishSucceeded: true,
            LastPublishAttemptAt: capturedAt,
            UpdatedAt: capturedAt);
        PublicNodeSnapshot publicSnapshot =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    capturedAt,
                    TimeSpan.FromMinutes(1));

        string json = JsonSerializer.Serialize(
            publicSnapshot,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));

        Assert.Contains($"\"durationMilliseconds\":{duration}", json);
        Assert.Contains(
            "\"endpoint\":\"example.internal:17\"",
            json);
        Assert.Contains(
            "\"error\":\"Connection refused\"",
            json);

        AgentSnapshotItem dashboardSnapshot =
            Assert.IsType<AgentSnapshotItem>(
                JsonSerializer.Deserialize<AgentSnapshotItem>(
                    json,
                    new JsonSerializerOptions(
                        JsonSerializerDefaults.Web)));
        AgentProtocolStatusItem protocol =
            Assert.Single(dashboardSnapshot.Protocols);

        Assert.Equal(duration, protocol.DurationMilliseconds);
        Assert.Equal("example.internal:17", protocol.Endpoint);
        Assert.Equal("Connection refused", protocol.Error);
    }

    [Fact]
    public void DashboardDeserializesOlderPayloadWithoutOptionalDiagnostics()
    {
        const string json =
            """
            {
              "node": "older-agent",
              "capturedAt": "2026-07-18T12:00:00+00:00",
              "ageSeconds": 5,
              "stale": false,
              "host": null,
              "missionControlPublishSucceeded": null,
              "lastMissionControlPublishAttemptAt": null,
              "protocols": [
                {
                  "service": "echo",
                  "succeeded": true,
                  "durationMilliseconds": 42
                }
              ],
              "containers": [
                {
                  "name": "api",
                  "state": "running",
                  "memoryUsageBytes": 100,
                  "memoryLimitBytes": 1000,
                  "memoryPercent": 10,
                  "cpuPercent": 2,
                  "restartCount": 0
                }
              ],
              "dockerAvailable": true,
              "dockerError": null
            }
            """;

        AgentSnapshotItem snapshot =
            Assert.IsType<AgentSnapshotItem>(
                JsonSerializer.Deserialize<AgentSnapshotItem>(
                    json,
                    new JsonSerializerOptions(
                        JsonSerializerDefaults.Web)));
        AgentProtocolStatusItem protocol =
            Assert.Single(snapshot.Protocols);
        AgentContainerStatusItem container =
            Assert.Single(snapshot.Containers);

        Assert.Equal(42, protocol.DurationMilliseconds);
        Assert.Null(protocol.Endpoint);
        Assert.Null(protocol.Error);
        Assert.Null(container.Image);
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
        Assert.Equal(
            "UNKNOWN",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: false,
                dockerAvailable: true,
                containerState: "running"));
        Assert.Equal(
            "EXITED",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: true,
                containerState: "exited"));
        Assert.Equal(
            "STOPPED",
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: true,
                containerState: " stopped "));
        Assert.Equal(
            "status-stopped",
            ContainerStatusPresentation.GetCssClass("EXITED"));
        Assert.Equal(
            "status-warning",
            ContainerStatusPresentation.GetCssClass("PAUSED"));
    }

    [Theory]
    [InlineData("running", "RUNNING")]
    [InlineData("exited", "EXITED")]
    [InlineData("stopped", "STOPPED")]
    [InlineData("created", "CREATED")]
    [InlineData("restarting", "RESTARTING")]
    [InlineData("paused", "PAUSED")]
    [InlineData("dead", "DEAD")]
    [InlineData("removing", "REMOVING")]
    public void KnownDockerStatesRemainAccurate(
        string dockerState,
        string expectedPresentation)
    {
        Assert.Equal(
            expectedPresentation,
            ContainerStatusPresentation.GetState(
                isSnapshotAvailable: true,
                dockerAvailable: true,
                containerState: dockerState));
    }

    [Fact]
    public void UnavailableContainerResourcesUseNeutralMarkers()
    {
        Assert.Equal(
            "—",
            ContainerResourcePresentation.FormatCpu(null));
        Assert.Equal(
            "Unavailable",
            ContainerResourcePresentation.FormatMemory(
                null,
                null,
                null));
        Assert.Equal(
            "—",
            ContainerResourcePresentation.FormatRestartCount(null));
        Assert.Equal(
            "12.5%",
            ContainerResourcePresentation.FormatCpu(12.5));
        Assert.Equal(
            "—",
            ContainerResourcePresentation.FormatMemorySummary(
                null));
        Assert.Equal(
            "94.6 MB",
            ContainerResourcePresentation.FormatMemorySummary(
                99_194_880));
        Assert.Equal(
            "1 MB",
            ContainerResourcePresentation.FormatMemorySummary(
                1_048_576));
    }

    [Fact]
    public void DashboardDiagnosticPresentationIsExplicitAndNeutral()
    {
        Assert.NotEqual(
            AgentDiagnosticPresentation.ConfiguredEndpointLabel,
            AgentDiagnosticPresentation.ObservedEndpointLabel);
        Assert.NotEqual(
            AgentDiagnosticPresentation.ConfiguredImageLabel,
            AgentDiagnosticPresentation.ObservedImageLabel);
        Assert.Equal(
            "probe.internal:17",
            AgentDiagnosticPresentation.FormatOptional(
                "probe.internal:17"));
        Assert.Equal(
            "observed/image:1",
            AgentDiagnosticPresentation.FormatOptional(
                "observed/image:1"));
        Assert.Equal(
            "—",
            AgentDiagnosticPresentation.FormatOptional(null));
        Assert.Equal(
            "9,007,199,254,740,993 ms",
            AgentDiagnosticPresentation.FormatDuration(
                9_007_199_254_740_993));
        Assert.Equal(
            "0 ms",
            AgentDiagnosticPresentation.FormatDuration(0));
        Assert.Equal(
            "42 ms",
            AgentDiagnosticPresentation.FormatDuration(42));
        Assert.Equal(
            "Connection refused",
            AgentDiagnosticPresentation.GetFailureReason(
                succeeded: false,
                "Connection refused"));
        Assert.Equal(
            AgentDiagnosticPresentation.MissingFailureReason,
            AgentDiagnosticPresentation.GetFailureReason(
                succeeded: false,
                error: null));
        Assert.Null(
            AgentDiagnosticPresentation.GetFailureReason(
                succeeded: true,
                "stale error"));
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
