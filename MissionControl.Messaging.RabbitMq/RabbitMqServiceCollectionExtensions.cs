/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

// NOTE: Currently there is no DLQ for RabbitMQ

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MissionControl.Observability.RabbitMq;

namespace MissionControl.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventConsumer(
    this IServiceCollection services)
    {
        services.AddSingleton<RabbitMqEventConsumer>();

        services.AddSingleton<IRabbitMqConnectionStatus>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    RabbitMqEventConsumer>());

        services.AddHostedService(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    RabbitMqEventConsumer>());

        return services;
    }

    public static IServiceCollection AddRabbitMqConnectionOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.HostName),
                "RabbitMq:HostName is required.")
            .Validate(
                options => options.Port is > 0 and <= 65535,
                "RabbitMq:Port must be between 1 and 65535.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.UserName),
                "RabbitMq:UserName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Password),
                "RabbitMq:Password is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.VirtualHost),
                "RabbitMq:VirtualHost is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.ClientProvidedName),
                "RabbitMq:ClientProvidedName is required.")
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddRabbitMqConsumerOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RabbitMqConsumerOptions>()
            .Bind(configuration.GetSection(
                RabbitMqConsumerOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.ExchangeName),
                "RabbitMqConsumer:ExchangeName is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.QueueName),
                "RabbitMqConsumer:QueueName is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.RoutingKey),
                "RabbitMqConsumer:RoutingKey is required.")
            .Validate(
                options => options.PrefetchCount > 0,
                "RabbitMqConsumer:PrefetchCount must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
