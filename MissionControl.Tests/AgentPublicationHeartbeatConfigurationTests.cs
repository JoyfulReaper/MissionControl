extern alias AgentApp;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.DependencyInjection;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Publishing;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentPublicationHeartbeatConfigurationTests
{
    [Fact]
    public void UnconfiguredHeartbeatUsesFifteenMinuteDefault()
    {
        using ServiceProvider provider =
            BuildProvider(CreateConfiguration());
        SnapshotPublicationGate gate =
            provider.GetRequiredService<
                SnapshotPublicationGate>();
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent snapshot = CreateSnapshot();

        gate.MarkPublished(snapshot, publishedAt);

        Assert.False(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(15).AddTicks(-1)));
        Assert.True(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(15)));
    }

    [Fact]
    public void CustomHeartbeatIsUsedByResolvedPublicationGate()
    {
        using ServiceProvider provider =
            BuildProvider(
                CreateConfiguration(
                    publicationHeartbeatMinutes: "2"));
        SnapshotPublicationGate gate =
            provider.GetRequiredService<
                SnapshotPublicationGate>();
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent snapshot = CreateSnapshot();

        gate.MarkPublished(snapshot, publishedAt);

        Assert.False(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(2).AddTicks(-1)));
        Assert.True(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(2)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveHeartbeatFailsOptionsValidation(
        string configuredValue)
    {
        using ServiceProvider provider =
            BuildProvider(
                CreateConfiguration(
                    publicationHeartbeatMinutes:
                        configuredValue));

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
                () => provider.GetRequiredService<
                    IOptions<AgentOptions>>().Value);

        Assert.Contains(
            "Agent:PublicationHeartbeatMinutes must be greater than zero.",
            exception.Message);
    }

    [Fact]
    public void EnvironmentVariableHeartbeatOverridesJsonConfiguration()
    {
        const string variableName =
            "Agent__PublicationHeartbeatMinutes";
        string? originalValue =
            Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(
                variableName,
                "4");

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        CreateConfigurationValues("15"))
                    .AddEnvironmentVariables()
                    .Build();

            using ServiceProvider provider =
                BuildProvider(configuration);
            AgentOptions options =
                provider.GetRequiredService<
                    IOptions<AgentOptions>>().Value;
            SnapshotPublicationGate gate =
                provider.GetRequiredService<
                    SnapshotPublicationGate>();
            DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
            NodeSnapshotEvent snapshot = CreateSnapshot();

            gate.MarkPublished(snapshot, publishedAt);

            Assert.Equal(4, options.PublicationHeartbeatMinutes);
            Assert.False(
                gate.IsDue(
                    snapshot,
                    publishedAt.AddMinutes(4).AddTicks(-1)));
            Assert.True(
                gate.IsDue(
                    snapshot,
                    publishedAt.AddMinutes(4)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                variableName,
                originalValue);
        }
    }

    private static ServiceProvider BuildProvider(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMissionControlAgent(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateConfiguration(
        string? publicationHeartbeatMinutes = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                CreateConfigurationValues(
                    publicationHeartbeatMinutes))
            .Build();
    }

    private static Dictionary<string, string?>
        CreateConfigurationValues(
            string? publicationHeartbeatMinutes)
    {
        var values = new Dictionary<string, string?>
        {
            ["Agent:NodeName"] = "test-node",
            ["Agent:IntervalSeconds"] = "60",
            ["Agent:DockerEnabled"] = "false",
            ["MissionControl:Enabled"] = "false",
            ["MissionControl:BaseUrl"] =
                "http://127.0.0.1:5190",
            ["MissionControl:TimeoutMilliseconds"] = "1000"
        };

        if (publicationHeartbeatMinutes is not null)
        {
            values["Agent:PublicationHeartbeatMinutes"] =
                publicationHeartbeatMinutes;
        }

        return values;
    }

    private static NodeSnapshotEvent CreateSnapshot()
    {
        return new NodeSnapshotEvent(
            Node: "test-node",
            CapturedAt: DateTimeOffset.UtcNow,
            Host: null,
            Protocols: [],
            Containers: [],
            DockerAvailable: false,
            DockerError: "Docker metric collection is disabled.");
    }
}
