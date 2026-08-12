using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Extensions.Microsoft.DependencyInjection;
using NATS.Net;

namespace MissionControl.Messaging.Nats;

public static class NatsServiceCollectionExtensions
{

    public static IServiceCollection AddNatsConnection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNatsOptions(configuration);
        services.AddNatsClient(nats =>
        {
            nats.ConfigureOptions(options =>
            {
                options.Configure<IOptions<NatsOptions>>(
                    (natsOptionsBuilder, configuredOptions) =>
                    {
                        var configured = configuredOptions.Value;
                        natsOptionsBuilder.Opts =
                            natsOptionsBuilder.Opts with
                            {
                                Url = configured.Url,
                                Name = configured.ClientName
                            };
                    });
            });
        });

        services.AddSingleton<INatsJSContext>(serviceProvider =>
        {
            var client = serviceProvider.GetRequiredService<INatsClient>();

            return client.CreateJetStreamContext();
        });

        services.AddHostedService<NatsJetStreamInitializer>();

        return services;
    }

    public static IServiceCollection AddNatsEventConsumer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNatsConnection(configuration);
        services.AddNatsConsumerOptions(configuration);
        services.AddHostedService<NatsEventConsumer>();

        return services;
    }

    public static IServiceCollection AddNatsOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<NatsOptions>()
            .Bind(configuration.GetSection(NatsOptions.SectionName))
            .Validate(
                options => IsValidNatsUrl(options.Url),
                "Nats:Url must be a valid NATS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.StreamName),
                "Nats:StreamName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientName),
                "Nats:ClientName is required.")
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddNatsConsumerOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<NatsConsumerOptions>()
            .Bind(configuration.GetSection(NatsConsumerOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DurableName),
                "NatsConsumer:DurableName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FilterSubject),
                "NatsConsumer:FilterSubject is required.")
            .Validate(
                options => options.MaxDeliveries > 0,
                "NatsConsumer:MaxDeliveries must be greater than zero.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsValidNatsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "nats" or "tls";
    }
}