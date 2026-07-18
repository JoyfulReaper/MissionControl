extern alias AgentApp;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.DependencyInjection;
using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Host;
using AgentApp::MissionControl.Agent.Protocols;
using AgentApp::MissionControl.Agent.Publishing;
using AgentApp::MissionControl.Agent.Storage;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentServiceCollectionExtensionsTests
{
    [Fact]
    public void AgentRuntimeServicesResolveThroughRealProvider()
    {
        string tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                $"missioncontrol-agent-runtime-di-{Guid.NewGuid():N}");

        try
        {
            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new("Agent:NodeName", "test-node"),
                        new("Agent:IntervalSeconds", "60"),
                        new("Agent:DockerEnabled", "false"),
                        new("AgentStorage:DatabaseFileName", "agent-di.db"),
                        new("AgentStorage:BasePath", tempDirectory),
                        new("MissionControl:Enabled", "false"),
                        new("MissionControl:BaseUrl", "http://127.0.0.1:5190"),
                        new("MissionControl:TimeoutMilliseconds", "1000")
                    ])
                    .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMissionControlAgent(configuration);
            services.AddAgentSnapshotStorage(configuration);

            using ServiceProvider serviceProvider =
                services.BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateOnBuild = true,
                        ValidateScopes = true
                    });

            Assert.IsType<HostMetricsCollector>(
                serviceProvider.GetRequiredService<IHostMetricsCollector>());
            Assert.NotNull(
                serviceProvider.GetRequiredService<IDockerMetricsCollector>());
            Assert.NotNull(
                serviceProvider.GetRequiredService<ProtocolProbeRunner>());
            Assert.NotNull(
                serviceProvider.GetRequiredService<SnapshotPublicationGate>());

            IHostedService worker = Assert.Single(
                serviceProvider.GetServices<IHostedService>(),
                service => service is AgentWorker);
            Assert.IsType<AgentWorker>(worker);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void AddAgentSnapshotStorageRegistersSingleDatabaseAndStoreAndCreatesSchemaFile()
    {
        string tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                $"missioncontrol-agent-di-tests-{Guid.NewGuid():N}");

        try
        {
            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>(
                            "AgentStorage:DatabaseFileName",
                            "agent-di.db"),
                        new KeyValuePair<string, string?>(
                            "AgentStorage:BasePath",
                            tempDirectory)
                    ])
                    .Build();

            var services = new ServiceCollection();

            services.AddAgentSnapshotStorage(configuration);

            ServiceDescriptor[] agentDatabaseRegistrations =
                services
                    .Where(
                        descriptor =>
                            descriptor.ServiceType ==
                            typeof(AgentDatabase))
                    .ToArray();
            ServiceDescriptor[] snapshotStoreRegistrations =
                services
                    .Where(
                        descriptor =>
                            descriptor.ServiceType ==
                            typeof(INodeSnapshotStore))
                    .ToArray();

            Assert.Single(agentDatabaseRegistrations);
            Assert.Single(snapshotStoreRegistrations);
            Assert.Equal(
                typeof(SqliteNodeSnapshotStore),
                snapshotStoreRegistrations[0].ImplementationType);

            using ServiceProvider serviceProvider =
                services.BuildServiceProvider();

            INodeSnapshotStore resolvedStore =
                serviceProvider.GetRequiredService<INodeSnapshotStore>();
            AgentDatabase resolvedDatabase =
                serviceProvider.GetRequiredService<AgentDatabase>();

            Assert.IsType<SqliteNodeSnapshotStore>(resolvedStore);
            Assert.Contains(
                Path.Combine(tempDirectory, "agent-di.db"),
                resolvedDatabase.ConnectionString,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(
                File.Exists(Path.Combine(tempDirectory, "agent-di.db")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
