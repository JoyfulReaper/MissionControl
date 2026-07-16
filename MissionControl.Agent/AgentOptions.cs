namespace MissionControl.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string NodeName { get; init; } =
        Environment.MachineName;

    public int IntervalSeconds { get; init; } = 60;

    public bool DockerEnabled { get; init; } =
        !OperatingSystem.IsWindows();

    public string DockerSocketPath { get; init; } =
        "/var/run/docker.sock";

    public int DockerTimeoutSeconds { get; init; } = 5;

    public ProbeOptions[] Probes { get; init; } = [];
}

public sealed class ProbeOptions
{
    public required string Name { get; init; }

    public required string Host { get; init; }

    public required string Protocol { get; init; }

    public int Port { get; init; }
    public int TimeoutMilliseconds { get; init; } = 2_000;
}