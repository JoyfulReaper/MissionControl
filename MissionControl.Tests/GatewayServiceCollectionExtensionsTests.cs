using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MissionControl.Gateway.DependencyInjection;
using MissionControl.Messaging;
using MissionControl.Messaging.Nats;
using Xunit;

namespace MissionControl.Tests;

public sealed class GatewayServiceCollectionExtensionsTests
{
    [Fact]
    public async Task ProductionPublisherAndHealthServicesResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMissionControlGateway(CreateConfiguration());

        await using ServiceProvider provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        NatsEventPublisher concretePublisher =
            provider.GetRequiredService<NatsEventPublisher>();

        IEventPublisher eventPublisher =
            provider.GetRequiredService<IEventPublisher>();

        Assert.Same(concretePublisher, eventPublisher);

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is NatsJetStreamInitializer);

        HealthCheckServiceOptions healthOptions =
            provider.GetRequiredService<
                IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Contains(
            healthOptions.Registrations,
            registration => registration.Name == "nats");
    }

    [Fact]
    public async Task NatsEnvironmentVariablesBindValidatedOptions()
    {
        string prefix = $"MC_GATEWAY_{Guid.NewGuid():N}_";

        var variables = new Dictionary<string, string?>
        {
            [$"{prefix}Nats__Url"] = "nats://nats.internal:4222",
            [$"{prefix}Nats__ClientName"] = "gateway-production",
            [$"{prefix}Nats__StreamName"] = "MISSION_CONTROL_EVENTS",
            [$"{prefix}EventSources__Sources__0__Name"] = "test-source",
            [$"{prefix}EventSources__Sources__0__ApiKey"] =
                "test-event-source-api-key-32-characters",
            [$"{prefix}GitHubWebhook__Enabled"] = "false",
            [$"{prefix}GitHubWebhook__MaxPayloadBytes"] = "1048576"
        };

        try
        {
            foreach ((string variable, string? value) in variables)
            {
                Environment.SetEnvironmentVariable(variable, value);
            }

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables(prefix)
                    .Build();

            await using ServiceProvider provider =
                CreateProvider(configuration);

            NatsOptions options = provider
                .GetRequiredService<IOptions<NatsOptions>>()
                .Value;

            Assert.Equal("nats://nats.internal:4222", options.Url);
            Assert.Equal("gateway-production", options.ClientName);
            Assert.Equal("MISSION_CONTROL_EVENTS", options.StreamName);
        }
        finally
        {
            foreach (string variable in variables.Keys)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }
    }

    private static ServiceProvider CreateProvider(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddMissionControlGateway(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateConfiguration(
        params KeyValuePair<string, string?>[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Nats:Url"] = "nats://localhost:4222",
            ["Nats:ClientName"] = "mission-control-gateway-tests",
            ["Nats:StreamName"] = "MISSION_CONTROL_EVENTS",

            ["EventSources:Sources:0:Name"] =
                "test-source",
            ["EventSources:Sources:0:ApiKey"] =
                "test-event-source-api-key-32-characters",

            ["GitHubWebhook:Enabled"] = "false",
            ["GitHubWebhook:MaxPayloadBytes"] =
                "1048576"
        };

        foreach (KeyValuePair<string, string?> item in overrides)
        {
            values[item.Key] = item.Value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}