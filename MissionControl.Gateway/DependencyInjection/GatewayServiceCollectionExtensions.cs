/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Gateway.Integrations.GitHub;
using MissionControl.Gateway.Messaging.RabbitMq;
using MissionControl.Gateway.Security;
using MissionControl.Messaging;
using MissionControl.Messaging.Nats;

namespace MissionControl.Gateway.DependencyInjection;

public static class GatewayServiceCollectionExtensions
{
    public static IServiceCollection AddMissionControlGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddGatewayRabbitMqOptions(configuration)
            .AddWindowsService(options =>
            {
                options.ServiceName = "Mission Control Gateway";
            })
            .AddSingleton<NatsEventPublisher>()
            .AddSingleton<IEventPublisher>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<NatsEventPublisher>())
            .AddEventSourceOptions(configuration)
            .AddGitHubWebhookOptions(configuration)
            .AddSingleton<GitHubWebhookSignatureValidator>()
            .AddSingleton<
                IEventSourceResolver,
                ApiKeyEventSourceResolver>();

        services.AddHealthChecks();

        services.AddNatsConnection(configuration);

        return services;
    }

    private static IServiceCollection AddGatewayRabbitMqOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection rabbitMqSection =
            configuration.GetRequiredSection(
                RabbitMqOptions.SectionName);

        services
            .AddOptions<RabbitMqOptions>()
            .Bind(rabbitMqSection)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.HostName),
                "RabbitMQ hostname is required.")
            .Validate(
                options => options.Port is > 0 and <= 65535,
                "RabbitMQ port must be between 1 and 65535.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.UserName),
                "RabbitMQ username is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Password),
                "RabbitMQ password is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.VirtualHost),
                "RabbitMQ virtual host is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.ClientProvidedName),
                "RabbitMQ client-provided name is required.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddEventSourceOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EventSourceOptions>()
            .Bind(configuration.GetSection(EventSourceOptions.SectionName))
            .Validate(
                options => options.Sources.Count > 0,
                "At least one event source must be configured.")
            .Validate(
                options => options.Sources.All(source =>
                    !string.IsNullOrWhiteSpace(source.Name)),
                "Every event source must have a name.")
            .Validate(
                options => options.Sources.All(source =>
                    !string.IsNullOrWhiteSpace(source.ApiKey)),
                "Every event source must have an API key.")
            .Validate(
                options => options.Sources.All(source =>
                    source.ApiKey.Length >= 32),
                "Every event source API key must contain at least 32 characters.")
            .Validate(
                options =>
                    options.Sources
                        .Select(source => source.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count() == options.Sources.Count,
                "Event source names must be unique.")
            .Validate(
                options =>
                    options.Sources
                        .Select(source => source.ApiKey)
                        .Distinct(StringComparer.Ordinal)
                        .Count() == options.Sources.Count,
                "Event source API keys must be unique.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddGitHubWebhookOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<GitHubWebhookOptions>()
            .Bind(configuration.GetSection(GitHubWebhookOptions.SectionName))
            .Validate(
                options =>
                    !options.Enabled ||
                    !string.IsNullOrWhiteSpace(options.Secret),
                "GitHubWebhook:Secret is required when the webhook is enabled.")
            .Validate(
                options =>
                    !options.Enabled ||
                    options.Secret.Length >= 32,
                "GitHubWebhook:Secret must contain at least 32 characters when the webhook is enabled.")
            .Validate(
                options =>
                    !options.Enabled ||
                    !string.IsNullOrWhiteSpace(options.AllowedOwner),
                "GitHubWebhook:AllowedOwner is required when the webhook is enabled.")
            .Validate(
                options =>
                    options.MaxPayloadBytes is > 0 and <= 25 * 1024 * 1024,
                "GitHubWebhook:MaxPayloadBytes must be between 1 byte and 25 MB.")
            .ValidateOnStart();

        return services;
    }
}
