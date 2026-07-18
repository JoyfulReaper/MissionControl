/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using JoyfulReaperLib.Sqlite;
using Microsoft.Extensions.Options;
using MissionControl.Archive.Health;
using MissionControl.Archive.Processing;
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
        IConfigurationSection archiveSection =
            configuration.GetRequiredSection(
                SqliteEventArchiveOptions.SectionName);

        services
            .AddOptions<SqliteEventArchiveOptions>()
            .Bind(archiveSection)
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<SqliteEventArchiveOptions>,
            SqliteEventArchiveOptionsValidator>();

        services
            .AddWindowsService(options =>
            {
                options.ServiceName = "Mission Control Archive";
            })
            .AddRabbitMqConnectionOptions(configuration)
            .AddRabbitMqConsumerOptions(configuration)
            .AddSingleton(CreateArchiveConnection)
            .AddSingleton<
                IIntegrationEventArchive,
                SqliteEventArchive>()
            .AddSingleton<
                IIntegrationEventProcessor,
                ArchivingIntegrationEventProcessor>()
            .AddSingleton<
                IIntegrationEventQuery,
                SqliteEventQuery>()
            .AddRabbitMqEventConsumer();

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

    private static SqliteEventArchiveConnection CreateArchiveConnection(
        IServiceProvider serviceProvider)
    {
        SqliteEventArchiveOptions options =
            serviceProvider
                .GetRequiredService<
                    IOptions<SqliteEventArchiveOptions>>()
                .Value;

        string databasePath =
            SqliteEventArchivePath.ResolveDatabasePath(options);

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                dbFileName: Path.GetFileName(databasePath),
                schemaSql: SqliteEventArchiveSchema.Sql,
                basePath: Path.GetDirectoryName(databasePath));

        return new SqliteEventArchiveConnection(
            connectionString);
    }
}
