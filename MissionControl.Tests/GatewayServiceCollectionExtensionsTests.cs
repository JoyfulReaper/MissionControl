using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MissionControl.Gateway.DependencyInjection;
using MissionControl.Gateway.Messaging;
using MissionControl.Gateway.Messaging.RabbitMq;
using MissionControl.Observability.RabbitMq;
using Xunit;

namespace MissionControl.Tests;

public sealed class GatewayServiceCollectionExtensionsTests
{
    [Fact]
    public async Task ProductionPublisherAndHealthServicesResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMissionControlGateway(
            CreateConfiguration());

        await using ServiceProvider provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        RabbitMqEventPublisher concretePublisher =
            provider.GetRequiredService<
                RabbitMqEventPublisher>();
        IEventPublisher eventPublisher =
            provider.GetRequiredService<IEventPublisher>();
        IRabbitMqConnectionStatus connectionStatus =
            provider.GetRequiredService<
                IRabbitMqConnectionStatus>();

        Assert.Same(concretePublisher, eventPublisher);
        Assert.Same(concretePublisher, connectionStatus);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service =>
                service is RabbitMqPublisherConnectionWorker);

        HealthCheckServiceOptions healthOptions =
            provider.GetRequiredService<
                IOptions<HealthCheckServiceOptions>>().Value;
        Assert.Contains(
            healthOptions.Registrations,
            registration =>
                registration.Name == "rabbitmq" &&
                registration.Tags.Contains("ready"));
    }

    [Fact]
    public void ExistingRabbitMqOptionsValidationStillRejectsMissingPassword()
    {
        IConfiguration configuration =
            CreateConfiguration(
                new KeyValuePair<string, string?>(
                    "RabbitMq:Password",
                    ""));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMissionControlGateway(configuration);

        using ServiceProvider provider =
            services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
                () => provider.GetRequiredService<
                    IOptions<RabbitMqOptions>>().Value);

        Assert.Contains(
            "RabbitMQ password is required.",
            exception.Message);
    }

    private static IConfiguration CreateConfiguration(
        params KeyValuePair<string, string?>[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "localhost",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:UserName"] = "guest",
            ["RabbitMq:Password"] = "guest",
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:ClientProvidedName"] =
                "mission-control-gateway-tests",
            ["EventSources:Sources:0:Name"] =
                "test-source",
            ["EventSources:Sources:0:ApiKey"] =
                "test-event-source-api-key-32-characters",
            ["GitHubWebhook:Enabled"] = "false",
            ["GitHubWebhook:MaxPayloadBytes"] = "1048576"
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
