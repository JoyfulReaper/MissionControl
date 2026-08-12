using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MissionControl.Gateway.DependencyInjection;
using MissionControl.Gateway.Messaging.RabbitMq;
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
        services.AddMissionControlGateway(
            CreateConfiguration());

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
            service =>
                service is NatsJetStreamInitializer);

        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service =>
                service is RabbitMqPublisherConnectionWorker);

        HealthCheckServiceOptions healthOptions =
            provider.GetRequiredService<
                IOptions<HealthCheckServiceOptions>>().Value;

        Assert.DoesNotContain(
            healthOptions.Registrations,
            registration =>
                registration.Name == "rabbitmq");
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

        OptionsValidationException exception =
            Assert.Throws<OptionsValidationException>(
                () => provider
                    .GetRequiredService<IOptions<RabbitMqOptions>>()
                    .Value);

        Assert.Contains(
            "RabbitMQ password is required.",
            exception.Message);
    }

    [Fact]
    public void MissingRabbitMqSectionFailsRegistration()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => services.AddMissionControlGateway(
                    new ConfigurationBuilder().Build()));

        Assert.Contains("RabbitMq", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RabbitMqHostnameMustBeExplicitAndNonblank(
        string? hostname)
    {
        AssertRabbitMqValidationFails(
            "RabbitMq:HostName",
            hostname,
            "RabbitMQ hostname is required.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void RabbitMqPortOutsideTcpRangeFails(string port)
    {
        AssertRabbitMqValidationFails(
            "RabbitMq:Port",
            port,
            "RabbitMQ port must be between 1 and 65535.");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("5672", 5672)]
    [InlineData("65535", 65535)]
    public void RabbitMqValidPortsBind(
        string configuredPort,
        int expected)
    {
        using ServiceProvider provider =
            CreateProvider(
                CreateConfiguration(
                    new KeyValuePair<string, string?>(
                        "RabbitMq:Port",
                        configuredPort)));

        RabbitMqOptions options = provider
            .GetRequiredService<IOptions<RabbitMqOptions>>()
            .Value;

        Assert.Equal(expected, options.Port);
    }

    [Theory]
    [InlineData(
        "RabbitMq:UserName",
        "RabbitMQ username is required.")]
    [InlineData(
        "RabbitMq:Password",
        "RabbitMQ password is required.")]
    [InlineData(
        "RabbitMq:VirtualHost",
        "RabbitMQ virtual host is required.")]
    [InlineData(
        "RabbitMq:ClientProvidedName",
        "RabbitMQ client-provided name is required.")]
    public void RabbitMqRequiredTextSettingsRejectMissingAndBlank(
        string key,
        string expectedFailure)
    {
        AssertRabbitMqValidationFails(
            key,
            null,
            expectedFailure);

        AssertRabbitMqValidationFails(
            key,
            " ",
            expectedFailure);
    }

    [Fact]
    public async Task RabbitMqEnvironmentVariablesBindValidatedOptions()
    {
        string prefix = $"MC_GATEWAY_{Guid.NewGuid():N}_";

        var variables = new Dictionary<string, string?>
        {
            [$"{prefix}RabbitMq__HostName"] = "broker.internal",
            [$"{prefix}RabbitMq__Port"] = "5673",
            [$"{prefix}RabbitMq__UserName"] = "gateway",
            [$"{prefix}RabbitMq__Password"] = "secret",
            [$"{prefix}RabbitMq__VirtualHost"] =
                "/mission-control",
            [$"{prefix}RabbitMq__ClientProvidedName"] =
                "gateway-production",

            [$"{prefix}Nats__Url"] =
                "nats://localhost:4222",
            [$"{prefix}Nats__ClientName"] =
                "mission-control-gateway-tests",
            [$"{prefix}Nats__StreamName"] =
                "MISSION_CONTROL_EVENTS",

            [$"{prefix}EventSources__Sources__0__Name"] =
                "test-source",
            [$"{prefix}EventSources__Sources__0__ApiKey"] =
                "test-event-source-api-key-32-characters",

            [$"{prefix}GitHubWebhook__Enabled"] = "false",
            [$"{prefix}GitHubWebhook__MaxPayloadBytes"] =
                "1048576"
        };

        try
        {
            foreach ((string variable, string? value) in variables)
            {
                Environment.SetEnvironmentVariable(
                    variable,
                    value);
            }

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables(prefix)
                    .Build();

            await using ServiceProvider provider =
                CreateProvider(configuration);

            RabbitMqOptions options = provider
                .GetRequiredService<IOptions<RabbitMqOptions>>()
                .Value;

            Assert.Equal(
                "broker.internal",
                options.HostName);

            Assert.Equal(
                5673,
                options.Port);

            Assert.Equal(
                "gateway-production",
                options.ClientProvidedName);
        }
        finally
        {
            foreach (string variable in variables.Keys)
            {
                Environment.SetEnvironmentVariable(
                    variable,
                    null);
            }
        }
    }

    private static void AssertRabbitMqValidationFails(
        string key,
        string? value,
        string expectedFailure)
    {
        using ServiceProvider provider =
            CreateProvider(
                CreateConfiguration(
                    new KeyValuePair<string, string?>(
                        key,
                        value)));

        OptionsValidationException exception =
            Assert.Throws<OptionsValidationException>(
                () => provider
                    .GetRequiredService<IOptions<RabbitMqOptions>>()
                    .Value);

        Assert.Contains(
            expectedFailure,
            exception.Failures);
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
            ["RabbitMq:HostName"] = "localhost",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:UserName"] = "guest",
            ["RabbitMq:Password"] = "guest",
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:ClientProvidedName"] =
                "mission-control-gateway-tests",

            ["Nats:Url"] = "nats://localhost:4222",
            ["Nats:ClientName"] =
                "mission-control-gateway-tests",
            ["Nats:StreamName"] =
                "MISSION_CONTROL_EVENTS",

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