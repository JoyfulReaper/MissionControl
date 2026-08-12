using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MissionControl.Contracts;
using MissionControl.Messaging;
using MissionControl.Observability.RabbitMq;
using System.Collections.Concurrent;

namespace MissionControl.Tests;

internal sealed class GatewayTestApplicationFactory : WebApplicationFactory<Program>
{
    internal const string WebhookSecret =
        "test-webhook-secret-32-characters-min";

    internal const string EventSourceApiKey =
        "test-event-source-api-key-32-chars";

    private readonly Dictionary<string, string?> _configuration;

    public GatewayTestApplicationFactory(
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        _configuration = new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "localhost",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:UserName"] = "guest",
            ["RabbitMq:Password"] = "guest",
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:ClientProvidedName"] = "mission-control-tests",
            ["EventSources:Sources:0:Name"] = "configured-source",
            ["EventSources:Sources:0:ApiKey"] = EventSourceApiKey,
            ["GitHubWebhook:Enabled"] = "true",
            ["GitHubWebhook:Secret"] = WebhookSecret,
            ["GitHubWebhook:AllowedOwner"] = "JoyfulReaper",
            ["GitHubWebhook:MaxPayloadBytes"] = "1048576"
        };

        if (configuration is not null)
        {
            foreach (var item in configuration)
            {
                _configuration[item.Key] = item.Value;
            }
        }
    }

    public CapturingEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(
            (_, config) =>
            {
                config.AddInMemoryCollection(_configuration);
            });

        builder.ConfigureServices(
            services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IEventPublisher>();
                services.RemoveAll<IRabbitMqConnectionStatus>();

                services.AddSingleton<IEventPublisher>(Publisher);
                services.AddSingleton<IRabbitMqConnectionStatus>(
                    new FakeRabbitMqConnectionStatus());
            });
    }
}

internal sealed class CapturingEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<IntegrationEventEnvelope> _events = new();

    public IReadOnlyList<IntegrationEventEnvelope> Events =>
        _events.ToArray();

    public PublisherFailureMode FailureMode { get; set; }

    public async Task PublishAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        switch (FailureMode)
        {
            case PublisherFailureMode.ThrowException:
                throw new InvalidOperationException("Publisher failed.");

            case PublisherFailureMode.WaitForCancellation:
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                break;
        }

        _events.Enqueue(integrationEvent);
    }
}

internal enum PublisherFailureMode
{
    None,
    ThrowException,
    WaitForCancellation
}

internal sealed class FakeRabbitMqConnectionStatus : IRabbitMqConnectionStatus
{
    public RabbitMqConnectionSnapshot GetSnapshot() =>
        new(ConnectionOpen: true, ChannelOpen: true);
}
