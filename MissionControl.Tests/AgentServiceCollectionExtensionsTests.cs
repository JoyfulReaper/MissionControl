extern alias AgentApp;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AgentApp::MissionControl.Agent.DependencyInjection;
using AgentApp::MissionControl.Agent.Storage;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentServiceCollectionExtensionsTests
{
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
