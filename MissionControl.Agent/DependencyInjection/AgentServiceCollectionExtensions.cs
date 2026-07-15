using MissionControl.Agent.Docker;
using MissionControl.Agent.Protocols;

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

        services.AddSingleton<IDockerMetricsCollector, DockerMetricsCollector>();

        services.AddSingleton<IProtocolProbe, EchoProbe>();
        services.AddSingleton<IProtocolProbe, QotdProbe>();
        services.AddSingleton<IProtocolProbe, GopherProbe>();
        services.AddSingleton<ProtocolProbeRunner>();

        services.AddHostedService<AgentWorker>();

        return services;
    }
}