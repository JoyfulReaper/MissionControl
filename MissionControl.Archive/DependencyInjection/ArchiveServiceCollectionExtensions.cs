/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using JoyfulReaperLib.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MissionControl.Archive.Health;
using MissionControl.Archive.Processing;
using MissionControl.Archive.Processing.RabbitMq;
using MissionControl.Archive.Storage;
using MissionControl.Archive.Storage.Sqlite;
using MissionControl.Messaging.RabbitMq;
using MissionControl.Observability.RabbitMq;

namespace MissionControl.Archive.DependencyInjection;

public static class ArchiveServiceCollectionExtensions
{
    public static IServiceCollection AddMissionControlArchive(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var archiveOptions =
            configuration
                .GetSection(SqliteEventArchiveOptions.SectionName)
                .Get<SqliteEventArchiveOptions>()
            ?? new SqliteEventArchiveOptions();

        var archiveConnectionString = SqliteDatabaseInitializer.Initialize(
            archiveOptions.DatabaseFileName,
            SqliteEventArchiveSchema.Sql,
            archiveOptions.BasePath);

        services
            .AddWindowsService(options =>
            {
                options.ServiceName = "Mission Control Archive";
            })
            .AddRabbitMqConnectionOptions(configuration)
            .AddRabbitMqConsumerOptions(configuration)
            .AddSingleton(
                new SqliteEventArchiveConnection(
                    archiveConnectionString))
            .AddSingleton<
                IIntegrationEventArchive,
                SqliteEventArchive>()
            .AddSingleton<
                IIntegrationEventProcessor,
                ArchivingIntegrationEventProcessor>()
            .AddSingleton<
                IIntegrationEventQuery,
                SqliteEventQuery>()
            .AddSingleton<RabbitMqEventConsumer>()
            .AddSingleton<IRabbitMqConnectionStatus>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<
                        RabbitMqEventConsumer>())
            .AddHostedService(
                serviceProvider =>
                    serviceProvider.GetRequiredService<
                        RabbitMqEventConsumer>());

        services
            .AddHealthChecks()
            .AddCheck<SqliteArchiveHealthCheck>(
                "sqlite",
                tags: ["ready"])
            .AddCheck<RabbitMqConnectionHealthCheck>(
                "rabbitmq",
                tags: ["ready"]);

        return services;
    }
}
