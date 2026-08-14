using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.Sqlite;
using Microsoft.Extensions.Options;
using MissionControl.Agent.Docker;
using MissionControl.Agent.Host;
using MissionControl.Agent.Protocols;
using MissionControl.Agent.Publishing;
using MissionControl.Agent.Storage;

namespace MissionControl.Agent.DependencyInjection;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddMissionControlAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AgentOptions>()
            .Bind(configuration.GetRequiredSection(
                AgentOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.NodeName),
                "Agent:NodeName is required.")
            .Validate(
                options =>
                    options.IntervalSeconds > 0,
                "Agent:IntervalSeconds must be greater than zero.")
            .Validate(
                options =>
                    options.PublicationHeartbeatMinutes > 0,
                "Agent:PublicationHeartbeatMinutes must be greater than zero.")
            .Validate(
                options =>
                    !options.DockerEnabled ||
                    !string.IsNullOrWhiteSpace(
                        options.DockerSocketPath),
                "Agent: DockerSocketPath is required when Docker is enabled.")
            .Validate(
                options =>
                    !options.DockerEnabled ||
                    options.DockerTimeoutSeconds > 0,
                "Agent: DockerTimeoutSeconds must be greater than zero when Docker is enabled.")
            .Validate(
                options =>
                    options.Probes.All(
                        probe =>
                            !string.IsNullOrWhiteSpace(probe.Name) &&
                            !string.IsNullOrWhiteSpace(probe.Host) &&
                            !string.IsNullOrWhiteSpace(probe.Protocol) &&
                            probe.Port is > 0 and <= 65535 &&
                            probe.TimeoutMilliseconds > 0),
                "Every Agent probe must have a name, host, protocol, valid port, and positive timeout.")
            .ValidateOnStart();


        services.AddMissionControlClient(
            configuration.GetSection(
                MissionControlClientOptions.SectionName));

        services.AddSingleton<IDockerMetricsCollector, DockerMetricsCollector>();
        services.AddSingleton<IHostMetricsCollector, HostMetricsCollector>();

        services.AddSingleton<IProtocolProbe, EchoProbe>();
        services.AddSingleton<IProtocolProbe, QotdProbe>();
        services.AddSingleton<IProtocolProbe, GopherProbe>();
        services.AddSingleton<IProtocolProbe, FingerProbe>();
        services.AddSingleton<IProtocolProbe, DaytimeProbe>();
        services.AddSingleton<IProtocolProbe, DiscardProbe>();
        services.AddSingleton<ProtocolProbeRunner>();

        services.AddSingleton(
            serviceProvider =>
            {
                AgentOptions options =
                    serviceProvider
                        .GetRequiredService<IOptions<AgentOptions>>()
                        .Value;

                return new SnapshotPublicationGate(
                    TimeSpan.FromMinutes(
                        options.PublicationHeartbeatMinutes));
            });

        services.AddHostedService<AgentWorker>();

        return services;
    }

    internal static IServiceCollection AddAgentSnapshotStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section =
            configuration.GetRequiredSection(
                AgentStorageOptions.SectionName);

        services
            .AddOptions<AgentStorageOptions>()
            .Bind(section)
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<AgentStorageOptions>,
            AgentStorageOptionsValidator>();

        services.AddSingleton(CreateAgentDatabase);

        services.AddSingleton<
            INodeSnapshotStore,
            SqliteNodeSnapshotStore>();

        return services;
    }

    private static AgentDatabase CreateAgentDatabase(
        IServiceProvider serviceProvider)
    {
        AgentStorageOptions options =
            serviceProvider
                .GetRequiredService<
                    IOptions<AgentStorageOptions>>()
                .Value;

        string databasePath =
            AgentStoragePath.ResolveDatabasePath(options);

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                dbFileName: Path.GetFileName(databasePath),
                schemaSql: AgentStorageSchema.Sql,
                basePath: Path.GetDirectoryName(databasePath));

        return new AgentDatabase(connectionString);
    }
}
