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
                    !string.IsNullOrWhiteSpace(
                        options.DockerSocketPath),
                "Agent:DockerSocketPath is required.")
            .Validate(
                options =>
                    options.DockerTimeoutSeconds > 0,
                "Agent:DockerTimeoutSeconds must be greater than zero.")
            .Validate(
                options =>
                    options.Probes.All(
                        probe =>
                            !string.IsNullOrWhiteSpace(probe.Name) &&
                            !string.IsNullOrWhiteSpace(probe.Host) &&
                            !string.IsNullOrWhiteSpace(probe.Protocol) &&
                            probe.Port is > 0 and <= 65535),
                "Every Agent probe must have a name, host, protocol, and valid port.")
            .ValidateOnStart();

        services.AddHostedService<AgentWorker>();

        return services;
    }
}