using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MissionControl.Messaging.Nats;

public static class NatsServiceCollectionExtensions
{
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